using System.Reflection;
using System.Text;
using System.Text.Json;
using TweakPilot.Catalog;
using TweakPilot.Core;
using TweakPilot.Engine;
using TweakPilot.Engine.Cleanup;
using TweakPilot.Engine.Insights;
using TweakPilot.Engine.Logging;
using TweakPilot.Engine.State;
using TweakPilot.Engine.Windows;
using TweakPilot.Ipc;

namespace TweakPilot.Cli;

/// <summary>
/// Command line. Uses the same engine as the app through <see cref="TweakService"/>.
/// Exit codes: 0 success, 1 error, 2 administrator rights missing, 3 Windows build not supported, 4 verification failed
/// (changes were undone), 5 invalid arguments, 6 backup failed (nothing was changed).
/// </summary>
internal static class Program
{
    private const int Success = 0;
    private const int GeneralError = 1;
    private const int AdminMissing = 2;
    private const int BuildUnsupported = 3;
    private const int VerificationFailed = 4;
    private const int InvalidArguments = 5;
    private const int BackupFailed = 6;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Text.L = new TweakPilot.Localization.Localizer(Text.ChooseCulture(args));
        Arguments parsed;
        try
        {
            parsed = Arguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Arguments.Usage);
            return InvalidArguments;
        }

        if (parsed.Command == "--version")
        {
            Console.WriteLine($"{ProductInfo.Name} {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
            return Success;
        }

        var log = new JsonLinesLog(JsonLinesLog.DefaultUserDirectory, "cli", TimeProvider.System);
        try
        {
            var catalog = CatalogLoader.LoadEmbedded();
            var environment = new WindowsSystemEnvironment();
            using var host = new Host(catalog, environment, parsed, log);
            return parsed.Command switch
            {
                "--list" => host.List(),
                "--autostart" => host.Autostart(),
                "--games" => host.Games(),
                "--clean-scan" => host.CleanScan(),
                "--large-scan" => host.LargeScan(),
                "--apps" => host.Apps(),
                "--onedrive-check" => host.OneDriveStatus(),
                "--volumes" => host.Volumes(),
                "--sysinfo" => host.SysInfo(),
                "--ping" => host.Ping(),
                "--anticheat" => host.AntiCheatStatus(),
                "--status" => host.Status(),
                "--apply" => host.Apply(),
                "--revert" => host.Revert(),
                "--revert-all" => host.RevertAll(),
                "--recover" => host.Recover(),
                "--profile" => host.ApplyProfile(),
                "--export" => host.Export(),
                _ => InvalidArguments,
            };
        }
        catch (TweakPilotException ex)
        {
            Console.Error.WriteLine(Text.L.Format("cli.error", ("kind", ex.Kind), ("message", ex.Message)));
            return ex.Kind switch
            {
                ErrorKind.PermissionDenied => AdminMissing,
                ErrorKind.BackupFailed => BackupFailed,
                _ => GeneralError,
            };
        }
        catch (Exception ex)
        {
            // Globale Fehlerbehandlung (Abschnitt 10.2): verstaendliche Meldung, Details nur im lokalen Protokoll.
            log.Write(new LogRecord { Event = "UnhandledException", Level = Core.LogLevel.Error, Error = ex.ToString() });
            Console.Error.WriteLine(Text.L.Format("cli.unexpected", ("file", log.CurrentFile)));
            return GeneralError;
        }
    }

    private sealed class Host : IDisposable
    {
        private readonly TweakCatalog _catalog;

        /// <summary>Katalog plus erzeugte Autostart-Tweaks (PERF-026/...); fuer Status, Anwenden und Ruecksetzen.</summary>
        private readonly CompositeCatalog _lookup;
        private readonly WindowsSystemEnvironment _environment;
        private readonly Arguments _args;
        private readonly TweakService _service;

        public Host(TweakCatalog catalog, WindowsSystemEnvironment environment, Arguments args, ILogSink log)
        {
            _catalog = catalog;
            _environment = environment;
            _args = args;

            // --state-root (nur Tests) ersetzt nur den Benutzer-Speicher; Maschinen-Tweaks verwaltet immer der Helfer in %ProgramData%.
            var userRoot = args.StateRoot ?? StateStore.DefaultUserRoot;
            var composite = new CompositeCatalog(catalog);
            _lookup = composite;
            _service = new TweakService(
                composite,
                EngineFactory.CreateForWindows(composite, userRoot, environment, log),
                EngineFactory.CreateForWindows(composite, StateStore.DefaultMachineRoot, environment, log),
                EngineFactory.CreateForWindows(composite, StateStore.UserPolicyRoot(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value), environment, log),
                () => new HelperClient(new ElevatedHelperLauncher()),
                TimeProvider.System);
            _service.AttachCleanup(CleanupFactory.User(environment), CleanupFactory.MachineEstimator(environment));

            if (args.Command != "--recover" && _service.GetInterruptedBatches().Count > 0)
            {
                Console.WriteLine(Text.L["cli.interrupted"]);
            }

            var support = SupportPolicy.Classify(environment.Build);
            if (support == BuildSupport.Untested)
            {
                Console.WriteLine(Text.L.Format("cli.untested", ("build", environment.Build)));
            }
            else if (support == BuildSupport.Unsupported)
            {
                Console.WriteLine(Text.L.Format("cli.unsupported", ("build", environment.Build)));
            }
        }

        private static string Culture => Text.L.Culture;

        /// <summary>Autostart-Uebersicht, nur lesend; Deaktivieren ueber --apply mit der angezeigten ID.</summary>
        public int Autostart()
        {
            var runner = new ProcessCommandRunner();
            var inventory = new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(runner, _environment.SystemDirectory), new WindowsSignatureVerifier());
            var entries = inventory.Read();
            if (_args.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(entries, CoreJson.Options));
                return Success;
            }

            foreach (var e in entries)
            {
                Console.WriteLine($"{(Text.L[e.Enabled ? "cli.on" : "cli.off"]),-6} {e.Rating,-9} {e.Source,-13} {e.Signature.Status,-11} {e.Name}");
                Console.WriteLine($"       {e.Signature.Publisher ?? "-"} | {e.ReasonKey} | {e.TweakId ?? Text.L["cli.notswitchable"]}");
            }

            return Success;
        }

        /// <summary>Feste Laufwerke mit Medientyp und DoH-Unterstuetzung, nur lesend.</summary>
        public int Volumes()
        {
            foreach (var v in new WindowsVolumeInventory().FixedVolumes())
            {
                Console.WriteLine(Text.L.Format("cli.volume", ("drive", v.Drive), ("media", v.Media), ("size", $"{v.SizeBytes / 1e9:0}"), ("free", $"{v.FreeBytes / 1e9:0}"), ("model", v.Model ?? "-")));
            }

            var network = new WindowsNetworkAccess(new ProcessCommandRunner(), _environment.SystemDirectory);
            var servers = network.PhysicalAdapters().SelectMany(network.GetDnsServers).ToList();
            var doh = TweakPilot.Engine.Tools.DohCheck.Evaluate(WindowsDoh.WellKnownServers(), servers);
            Console.WriteLine(Text.L.Format("cli.doh", ("supported", doh.Supported), ("servers", servers.Count == 0 ? Text.L["cli.dhcp"] : string.Join(", ", servers)), ("doh", doh.ServersWithDoh.Count)));
            return Success;
        }

        /// <summary>Systeminfos und Zustandsanzeigen des Dashboards (Abschnitt 6.1), nur lesend.</summary>
        public int SysInfo()
        {
            var source = new WindowsSystemInfo(new ProcessCommandRunner(), _environment.SystemDirectory);
            var info = source.Read();
            Console.WriteLine(Text.L.Format("cli.cpu", ("name", info.Cpu?.Name ?? "-"), ("cores", info.Cpu?.Cores ?? 0), ("threads", info.Cpu?.Threads ?? 0)));
            foreach (var gpu in info.Gpus)
            {
                Console.WriteLine(Text.L.Format("cli.gpu", ("vendor", gpu.Vendor), ("name", gpu.Name), ("driver", gpu.DriverVersion ?? "-"), ("date", $"{gpu.DriverDate:yyyy-MM-dd}"), ("memory", $"{gpu.MemoryBytes / 1073741824.0:0.0}")));
            }

            Console.WriteLine(Text.L.Format("cli.ram", ("total", $"{info.Ram?.TotalBytes / 1073741824.0:0.0}"), ("load", info.Ram?.LoadPercent ?? 0), ("type", info.Ram?.ModuleType ?? Text.L["cli.unknown"])));
            foreach (var disk in info.Disks)
            {
                Console.WriteLine(Text.L.Format("cli.disk", ("kind", disk.Kind), ("size", $"{disk.SizeBytes / 1e9:0}"), ("free", $"{disk.FreeBytes / 1e9:0}")));
            }

            Console.WriteLine(Text.L.Format("cli.windows", ("edition", info.Windows?.Edition ?? "-"), ("version", info.Windows?.Version ?? "-"), ("build", info.Windows?.Build ?? "-"), ("installed", $"{info.Windows?.InstalledUtc:yyyy-MM-dd}")));
            Console.WriteLine(Text.L.Format("cli.boot", ("last", $"{info.Boot.LastBootUtc:yyyy-MM-dd HH:mm}"), ("duration", info.Boot.Duration is { } d ? $"{d.TotalSeconds:0.0} s" : Text.L[info.Boot.DurationNeedsAdmin ? "cli.adminonly" : "cli.unknown"])));
            var health = source.ReadHealth();
            Console.WriteLine(Text.L.Format("cli.health", ("reboot", health.RebootPending?.ToString() ?? "-"), ("plan", health.PowerPlan ?? "-"), ("gamemode", health.GameMode?.ToString() ?? "-"), ("defender", health.DefenderRealtime?.ToString() ?? "-"), ("av", string.Join(", ", health.Antivirus)), ("free", health.SystemDriveFreePercent?.ToString() ?? "-"), ("ram", health.RamLoadPercent?.ToString() ?? "-")));
            return Success;
        }

        /// <summary>Ping- und Jitter-Test (NET-007).</summary>
        public int Ping()
        {
            var target = _args.Ids.FirstOrDefault() ?? "1.1.1.1";
            var stats = new TweakPilot.Engine.Tools.PingTest(new WindowsPinger()).RunAsync(target, null, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine(Text.L.Format("cli.ping", ("target", stats.Target), ("received", stats.Received), ("sent", stats.Sent), ("loss", $"{stats.LossPercent:0.#}"), ("min", stats.MinMs?.ToString() ?? "-"), ("median", stats.MedianMs?.ToString() ?? "-"), ("max", stats.MaxMs?.ToString() ?? "-"), ("jitter", $"{stats.JitterMs:0.0}")));
            return Success;
        }

        /// <summary>OneDrive-Vorbedingungen (DEB-028), nur lesend.</summary>
        public int OneDriveStatus()
        {
            var status = new TweakPilot.Engine.Debloat.OneDriveCheck(new TweakPilot.Engine.Debloat.WindowsOneDriveEnvironment(new WindowsSignatureVerifier())).Check();
            Console.WriteLine(Text.L.Format("cli.onedrive.installed", ("installed", status.Installed), ("can", status.CanUninstall)));
            Console.WriteLine(Text.L.Format("cli.onedrive.blocked", ("blocks", status.Blocks.Count == 0 ? "-" : string.Join(", ", status.Blocks))));
            Console.WriteLine(Text.L.Format("cli.onedrive.folders", ("folders", status.RedirectedFolders.Count == 0 ? "-" : string.Join(", ", status.RedirectedFolders)), ("online", status.OnlineOnlyFiles)));
            return Success;
        }

        /// <summary>Apps des Benutzers mit Katalog-Zuordnung und Sperrliste, nur lesend.</summary>
        public int Apps()
        {
            var installed = new TweakPilot.Appx.WindowsAppxAccess().ListForCurrentUser();
            foreach (var row in TweakPilot.Engine.Debloat.AppInventory.Build(installed, _environment.Build))
            {
                var kind = row.Locked ? "gesperrt" : row.Catalog?.Id ?? "weitere";
                Console.WriteLine($"{kind,-9} {(row.Installed is null ? "-" : Text.L["cli.installed"]),-11} {row.PackageName}{(row.Installed is { } p && row.Catalog is null ? $" ({p.PublisherDisplayName})" : string.Empty)}");
            }

            return Success;
        }

        /// <summary>Bereinigung: nur Scan, nichts wird geloescht. Systemordner ohne Admin geschaetzt.</summary>
        public int CleanScan()
        {
            foreach (var category in CleanupCategories.All)
            {
                var scan = _service.ScanCleanup(category.Id);
                var size = scan.SizeUnknown ? Text.L["cli.unknown"] : Text.L.Format("cli.clean.size", ("mb", $"{scan.Bytes / 1024.0 / 1024.0:0.0}"), ("files", scan.Files));
                Console.WriteLine($"{category.Id} {category.Scope,-8} {size}{(scan.Estimated ? " " + Text.L["cli.clean.estimated"] : string.Empty)}{(scan.Blocked is null ? string.Empty : " " + Text.L.Format("cli.clean.blocked", ("reason", scan.Blocked)))}{(scan.SkippedReparsePoints > 0 ? " " + Text.L.Format("cli.clean.links", ("n", scan.SkippedReparsePoints)) : string.Empty)}");
            }

            return Success;
        }

        /// <summary>
        /// Grosse Dateien klassifiziert (V2 Abschnitt 6), nur lesend: Anzahl je Klasse, Spiele, die Eintraege der Gruppen A und B und
        /// der Abgleich mit den erkannten Spielen (Epic, Steam): wie viele Spieldateien faelschlich in A oder B landen (Ziel: 0).
        /// </summary>
        public int LargeScan()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var roots = new WindowsVolumeInventory().FixedVolumes().Select(v => $"{v.Drive}:\\").ToList();
            var scanner = new TweakPilot.Engine.Tools.LargeFileScanner(new WindowsCleanupFileSystem(), new WindowsFileHasher(), WindowsLargeFileContext.Create([]), [windows]);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var scan = scanner.Scan(roots, TweakPilot.Engine.Tools.LargeFileScanner.DefaultThreshold, CancellationToken.None);
            Console.WriteLine(Text.L.Format("cli.large.folders", ("n", scan.Folders), ("s", $"{watch.Elapsed.TotalSeconds:0}")) + (scan.TimedOut ? " " + Text.L["cli.large.timeout"] : string.Empty) + (scan.Truncated ? " " + Text.L["cli.large.truncated"] : string.Empty));
            foreach (var group in scan.Items.GroupBy(i => i.Class).OrderBy(g => g.Key))
            {
                Console.WriteLine(Text.L.Format("cli.large.class", ("class", group.Key), ("n", group.Count()), ("gb", $"{group.Sum(i => i.SizeBytes) / 1024.0 / 1024 / 1024:0.0}")));
            }

            foreach (var item in scan.Items.Where(i => i.Class is TweakPilot.Engine.Tools.LargeFileClass.A or TweakPilot.Engine.Tools.LargeFileClass.B))
            {
                Console.WriteLine($"  {item.Class} {item.ReasonKey,-24} {item.SizeBytes / 1024 / 1024,8} MB  {item.Path}");
            }

            foreach (var game in scan.Games)
            {
                Console.WriteLine(Text.L.Format("cli.large.game", ("name", game.Name), ("gb", $"{game.SizeBytes / 1024.0 / 1024 / 1024:0.0}"), ("path", game.Path)));
            }

            var detected = new GameLibrary(new WindowsGameFileSystem(), WindowsGameFileSystem.EpicManifestFolder, WindowsGameFileSystem.SteamRoot()).Scan();
            var wrong = scan.Items.Count(i => i.Class is TweakPilot.Engine.Tools.LargeFileClass.A or TweakPilot.Engine.Tools.LargeFileClass.B
                && detected.Any(g => TweakPilot.Engine.Tools.LargeFileScanner.IsUnder(i.Path, g.InstallFolder)));
            Console.WriteLine(Text.L.Format("cli.large.check", ("n", detected.Count), ("wrong", wrong)));
            return Success;
        }

        /// <summary>Gefundene Spiele (Epic, Steam), nur lesend.</summary>
        public int Games()
        {
            var library = new GameLibrary(new WindowsGameFileSystem(), WindowsGameFileSystem.EpicManifestFolder, WindowsGameFileSystem.SteamRoot());
            foreach (var game in library.Scan())
            {
                Console.WriteLine($"{game.Source,-6} {game.Name} | Anti-Cheat: {game.AntiCheat} | EXE: {game.ExePath ?? Text.L["cli.choose"]}");
            }

            return Success;
        }

        /// <summary>Anti-Cheat-Kompatibilitaetspruefung (GAME-020), nur lesend.</summary>
        public int AntiCheatStatus()
        {
            var facts = new WindowsSecurityFacts().Read();
            foreach (var check in AntiCheatCheck.Evaluate(facts))
            {
                Console.WriteLine($"{check.Id,-12} {check.State,-9} {check.ValueKey}");
            }

            return Success;
        }

        public int List()
        {
            var module = _args.Module;
            var rows = _catalog.Tweaks.Where(t => module is null || t.Id.StartsWith(module + "-", StringComparison.OrdinalIgnoreCase)).Select(t =>
            {
                var status = _service.GetStatus(t.Id);
                return new
                {
                    t.Id,
                    Name = _catalog.Text(Culture, t.NameKey),
                    Risk = t.Risk.ToString(),
                    Evidence = t.Evidence.ToString(),
                    Scope = t.Scope.ToString(),
                    State = status.State.ToString(),
                    status.Reason,
                    t.HiddenByDefault,
                };
            }).ToList();

            if (_args.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(rows, CoreJson.Options));
                return Success;
            }

            foreach (var row in rows)
            {
                var hidden = row.HiddenByDefault ? Text.L["cli.hidden"] : string.Empty;
                Console.WriteLine($"{row.Id,-9} {row.State,-12} {row.Risk,-8} {row.Evidence,-9} {row.Scope,-8} {row.Name}{hidden}");
                if (row.Reason is not null)
                {
                    Console.WriteLine($"          {row.Reason}");
                }
            }

            return Success;
        }

        public int Status()
        {
            if (!_lookup.TryGet(_args.Ids[0], out var tweak))
            {
                Console.Error.WriteLine(Text.L.Format("cli.unknownid", ("id", _args.Ids[0])));
                return InvalidArguments;
            }

            var status = _service.GetStatus(tweak.Id);
            Console.WriteLine($"{tweak.Id}: {status.State}{(status.TrackedInLedger ? ", " + Text.L["cli.byapp"] : string.Empty)}");
            if (status.Reason is not null)
            {
                Console.WriteLine(status.Reason);
            }

            return Success;
        }

        public int Apply()
        {
            if (SupportPolicy.Classify(_environment.Build) == BuildSupport.Unsupported)
            {
                Console.Error.WriteLine(Text.L["cli.buildunsupported"]);
                return BuildUnsupported;
            }

            var tweaks = new List<TweakDefinition>();
            foreach (var id in _args.Ids)
            {
                if (_lookup.TryGet(id, out var t))
                {
                    tweaks.Add(t);
                }
            }

            var risky = tweaks.Where(t => t.Risk == RiskLevel.Expert || t.Evidence == EvidenceLevel.Disputed).ToList();
            if (risky.Count > 0 && !_args.AllowExpert)
            {
                if (_args.Silent)
                {
                    Console.Error.WriteLine(Text.L.Format("cli.silentrefused", ("ids", string.Join(", ", risky.Select(t => t.Id)))));
                    return InvalidArguments;
                }

                Console.WriteLine(Text.L.Format("cli.risky", ("ids", string.Join(", ", risky.Select(t => t.Id)))));
                Console.Write(Text.L.Format("cli.confirmword.prompt", ("word", Text.L["cli.confirmword"])));
                if (Console.ReadLine()?.Trim() != Text.L["cli.confirmword"])
                {
                    Console.WriteLine(Text.L["cli.cancelled"]);
                    return GeneralError;
                }
            }

            if (!_args.DryRun && tweaks.Any(t => t.Scope != TweakScope.User))
            {
                Console.WriteLine(Text.L["cli.uac"]);
            }

            var result = _service.Apply(_args.Ids, new ApplyOptions
            {
                DryRun = _args.DryRun,
                ConfirmWithoutRestorePoint = ConfirmWithoutRestorePoint,
                Progress = new ConsoleProgress(),
            });

            PrintPlan(result.Plan);
            return Report(result);
        }

        /// <summary>
        /// Profil anwenden (Abschnitt 10.3): gleicher Plan wie in der App. Versteckte Tweaks (Experte, Umstritten) nur mit --allow-expert,
        /// zurueckgesetzt werden nur eigene Aenderungen, "Nur Desktop" entfaellt auf Geraeten mit Akku.
        /// </summary>
        public int ApplyProfile()
        {
            var profile = LoadProfile();
            if (profile is null)
            {
                return InvalidArguments;
            }

            var battery = BatteryInfo.HasBattery();
            var applies = new List<string>();
            var reverts = new List<string>();
            foreach (var entry in profile.Tweaks)
            {
                if (!_lookup.TryGet(entry.Id, out var tweak))
                {
                    continue;
                }

                string? skip = null;
                var status = _service.GetStatus(entry.Id);
                if (entry.Condition == ProfileCondition.DesktopOnly && battery)
                {
                    skip = Text.L["cli.skip.desktoponly"];
                }
                else if (tweak.HiddenByDefault && !_args.AllowExpert)
                {
                    skip = Text.L["cli.skip.expert"];
                }
                else if (status.State == TweakState.Unavailable)
                {
                    skip = status.Reason ?? Text.L["cli.skip.unavailable"];
                }
                else if (entry.Target == ProfileTarget.Applied && status.State != TweakState.Applied)
                {
                    applies.Add(entry.Id);
                }
                else if (entry.Target == ProfileTarget.NotApplied && status.State == TweakState.Applied)
                {
                    if (status.TrackedInLedger)
                    {
                        reverts.Add(entry.Id);
                    }
                    else
                    {
                        skip = Text.L["cli.skip.notours"];
                    }
                }

                if (skip is not null)
                {
                    Console.WriteLine(Text.L.Format("cli.skipped", ("id", entry.Id), ("reason", skip)));
                }
            }

            Console.WriteLine(Text.L.Format("cli.profile.plan", ("apply", applies.Count), ("revert", reverts.Count)));
            if (_args.DryRun)
            {
                Console.WriteLine(Text.L["cli.dryrun"]);
                foreach (var id in reverts)
                {
                    Console.WriteLine(Text.L.Format("cli.wouldreset", ("id", id)));
                }
            }

            var exit = Success;
            if (applies.Count > 0)
            {
                _args.SetIds(applies);
                exit = Apply();
            }

            if (!_args.DryRun && exit == Success && reverts.Count > 0)
            {
                exit = ReportRevert(reverts.Select(id => _service.Revert(id, ResolveConflict)).ToList());
            }

            return exit;
        }

        /// <summary>Export eines Profils als PowerShell-Skript oder .reg-Datei, jeweils mit Gegenstueck zum Zuruecksetzen (Abschnitt 10.4).</summary>
        public int Export()
        {
            var profile = LoadProfile();
            if (profile is null)
            {
                return InvalidArguments;
            }

            var ids = profile.Tweaks.Where(e => e.Target == ProfileTarget.Applied).Select(e => e.Id).ToList();
            var export = _service.ExportScripts(ids, id => _lookup.TryGet(id, out var t) ? _catalog.Text(Culture, t.NameKey) : id, profile.Key ?? profile.Name);
            var output = _args.Out!;
            var revertPath = Path.Combine(Path.GetDirectoryName(output) ?? ".", Path.GetFileNameWithoutExtension(output) + "-revert" + Path.GetExtension(output));
            if (_args.ExportFormat == "reg")
            {
                if (export.ApplyReg is null)
                {
                    Console.Error.WriteLine(Text.L["cli.export.noreg"]);
                    return GeneralError;
                }

                File.WriteAllText(output, export.ApplyReg, Encoding.Unicode);
                File.WriteAllText(revertPath, export.RevertReg!, Encoding.Unicode);
            }
            else
            {
                File.WriteAllText(output, export.ApplyPs1, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                File.WriteAllText(revertPath, export.RevertPs1, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }

            Console.WriteLine(Text.L.Format("cli.exported", ("n", export.Exported.Count), ("file", output), ("revert", revertPath)));
            foreach (var (id, reason) in export.Skipped)
            {
                Console.WriteLine(Text.L.Format("cli.notexported", ("id", id), ("reason", reason)));
            }

            return Success;
        }

        /// <summary>Vorgefertigtes Profil (safe, gaming, office, laptop, privacy), eine .tpprofile-Datei oder ein eigenes Profil nach Namen.</summary>
        private TweakProfile? LoadProfile()
        {
            var name = _args.ProfileName!;
            try
            {
                if (ProfileLoader.BuiltInKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return ProfileLoader.LoadBuiltIn(_lookup).Single(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
                }

                if (name.EndsWith(ProfileCodec.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(name))
                {
                    using var stream = File.OpenRead(name);
                    var buffer = new byte[Math.Min(stream.Length, ProfileCodec.MaxBytes + 1L)];
                    stream.ReadExactly(buffer);
                    return ProfileCodec.Parse(buffer, _lookup);
                }

                var own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "profiles");
                if (Directory.Exists(own))
                {
                    foreach (var file in Directory.EnumerateFiles(own, "*.json"))
                    {
                        try
                        {
                            var profile = ProfileCodec.Parse(File.ReadAllBytes(file), _lookup);
                            if (string.Equals(profile.Name, name, StringComparison.CurrentCultureIgnoreCase))
                            {
                                return profile;
                            }
                        }
                        catch (TweakPilotException)
                        {
                        }
                    }
                }
            }
            catch (TweakPilotException ex)
            {
                Console.Error.WriteLine(Text.L.Format("cli.profile.refused", ("message", ex.Message)));
                return null;
            }

            Console.Error.WriteLine(Text.L.Format("cli.profile.notfound", ("name", name), ("builtin", string.Join(", ", ProfileLoader.BuiltInKeys))));
            return null;
        }

        public int Revert()
        {
            if (!_lookup.TryGet(_args.Ids[0], out var tweak))
            {
                Console.Error.WriteLine(Text.L.Format("cli.unknownid", ("id", _args.Ids[0])));
                return InvalidArguments;
            }

            return ReportRevert([_service.Revert(tweak.Id, ResolveConflict)]);
        }

        public int RevertAll()
        {
            return ReportRevert(_service.RevertAll(ResolveConflict));
        }

        public int Recover()
        {
            var exit = Success;
            foreach (var (scope, batch) in _service.GetInterruptedBatches())
            {
                Console.WriteLine(Text.L.Format("cli.recover.batch", ("batch", batch.BatchId), ("ids", string.Join(", ", batch.Tweaks.Select(t => t.Id)))));
                foreach (var error in _service.RecoverInterrupted(scope, batch.BatchId))
                {
                    Console.Error.WriteLine($"  {error}");
                    exit = GeneralError;
                }
            }

            Console.WriteLine(Text.L[exit == Success ? "cli.recover.done" : "cli.recover.failed"]);
            return exit;
        }

        public void Dispose() => _service.Dispose();

        private bool ConfirmWithoutRestorePoint(RestorePointResult result)
        {
            Console.WriteLine(Text.L.Format("cli.restorepoint", ("message", result.Message)));
            if (_args.NoRestorePoint)
            {
                Console.WriteLine(Text.L["cli.norestorepoint"]);
                return true;
            }

            if (_args.Silent)
            {
                Console.Error.WriteLine(Text.L["cli.norestorepoint.required"]);
                return false;
            }

            Console.Write(Text.L["cli.norestorepoint.ask"]);
            return Console.ReadLine()?.Trim().ToUpperInvariant() is "J" or "Y";
        }

        private ConflictDecision ResolveConflict(RevertConflict conflict)
        {
            Console.WriteLine(Text.L.Format("cli.conflict", ("id", conflict.TweakId)));
            Console.WriteLine("  " + Text.L.Format("cli.conflict.target", ("value", conflict.View.Target)));
            Console.WriteLine("  " + Text.L.Format("cli.conflict.current", ("value", conflict.View.Current)));
            Console.WriteLine("  " + Text.L.Format("cli.conflict.original", ("value", conflict.View.Original)));
            if (_args.Silent)
            {
                Console.WriteLine("  " + Text.L["cli.conflict.silent"]);
                return ConflictDecision.Cancel;
            }

            Console.Write("  " + Text.L["cli.conflict.ask"]);
            var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
            return answer == "O" ? ConflictDecision.RestoreOriginal
                : answer == Text.L["cli.conflict.keepkey"] ? ConflictDecision.KeepCurrent
                : ConflictDecision.Cancel;
        }

        private static void PrintPlan(BatchPlan plan)
        {
            if (plan.Items.Count > 0)
            {
                Console.WriteLine(Text.L["cli.preview"]);
                foreach (var item in plan.Items)
                {
                    var name = item.ValueName is null ? string.Empty : $" : {item.ValueName}";
                    Console.WriteLine($"  {item.TweakId} {item.ActionType} {item.Target}{name}");
                    Console.WriteLine("      " + Text.L.Format("cli.preview.current", ("value", item.Current)));
                    Console.WriteLine("      " + Text.L.Format("cli.preview.new", ("value", item.New)));
                }
            }

            foreach (var excluded in plan.Excluded)
            {
                Console.WriteLine($"  {excluded.TweakId}: {excluded.Outcome} - {excluded.Reason}");
            }
        }

        private static int Report(BatchResult result)
        {
            switch (result.Outcome)
            {
                case BatchOutcome.DryRun:
                    Console.WriteLine(Text.L["cli.preview.done"]);
                    return Success;
                case BatchOutcome.NothingToDo:
                    Console.WriteLine(Text.L["cli.nothing"]);
                    return result.Plan.Excluded.Any(e => e.Outcome == PreflightOutcome.AdminRequired) ? AdminMissing : Success;
                case BatchOutcome.Aborted:
                    Console.WriteLine(Text.L["cli.cancelled"]);
                    return GeneralError;
                case BatchOutcome.BlockedByInterruptedBatch:
                    Console.Error.WriteLine($"{result.Error?.Message} (tweakpilot-cli --recover)");
                    return GeneralError;
                case BatchOutcome.BackupFailed:
                    Console.Error.WriteLine($"{result.Error?.Message}");
                    return BackupFailed;
                case BatchOutcome.Committed:
                    Console.WriteLine(Text.L.Format("cli.applied", ("ids", string.Join(", ", result.AppliedTweaks))));
                    Console.WriteLine(Text.L.Format("cli.backup", ("path", result.BackupSetPath ?? "-")));
                    if (result.RebootRequired)
                    {
                        Console.WriteLine(Text.L["cli.restart"]);
                    }

                    return Success;
                case BatchOutcome.RolledBack:
                    Console.Error.WriteLine(Text.L.Format("cli.failed", ("id", result.FailedTweakId ?? "-"), ("message", result.Error?.Message ?? "-")));
                    Console.Error.WriteLine(Text.L["cli.rolledback"]);
                    return result.Error?.Kind switch
                    {
                        ErrorKind.PermissionDenied => AdminMissing,
                        ErrorKind.ValueMismatchAfterWrite => VerificationFailed,
                        _ => GeneralError,
                    };
                default:
                    Console.Error.WriteLine(Text.L.Format("cli.critical", ("id", result.FailedTweakId ?? "-"), ("message", result.Error?.Message ?? "-")));
                    Console.Error.WriteLine(Text.L["cli.rollbackfailed"]);
                    foreach (var error in result.RollbackErrors)
                    {
                        Console.Error.WriteLine($"  {error}");
                    }

                    Console.Error.WriteLine(Text.L.Format("cli.manual", ("path", result.BackupSetPath ?? "-")));
                    return GeneralError;
            }
        }

        private static int ReportRevert(IReadOnlyList<RevertResult> results)
        {
            var exit = Success;
            foreach (var result in results)
            {
                Console.WriteLine($"{result.TweakId}: {result.Outcome}{(result.Error is null ? string.Empty : " - " + result.Error.Message)}");
                if (result.Outcome == RevertOutcome.Failed)
                {
                    exit = result.Error?.Kind == ErrorKind.PermissionDenied ? AdminMissing : GeneralError;
                }
                else if (result.Outcome == RevertOutcome.Cancelled && exit == Success)
                {
                    exit = GeneralError;
                }
            }

            if (results.Count == 0)
            {
                Console.WriteLine(Text.L["cli.noneapplied"]);
            }

            return exit;
        }
    }
}

/// <summary>Synchrone Fortschrittsausgabe (Progress&lt;T&gt; wuerde ueber den Threadpool verzoegert schreiben).</summary>
internal sealed class ConsoleProgress : IProgress<BatchProgress>
{
    public void Report(BatchProgress value) => Console.WriteLine($"[{value.Index}/{value.Total}] {value.TweakId}");
}

/// <summary>CLI texts in the language the user chose (V3): --lang, then the saved app setting, then English.</summary>
internal static class Text
{
    public static TweakPilot.Localization.Localizer L { get; set; } = new(TweakPilot.Localization.Localizer.English);

    public static string ChooseCulture(string[] args)
    {
        var index = Array.IndexOf(args, "--lang");
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1] == "de" ? TweakPilot.Localization.Localizer.German : TweakPilot.Localization.Localizer.English;
        }

        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "settings.json");
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("language", out var language) && language.GetString() == TweakPilot.Localization.Localizer.German)
                {
                    return TweakPilot.Localization.Localizer.German;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: English.
        }

        return TweakPilot.Localization.Localizer.English;
    }
}

internal sealed class Arguments
{
    public const string Usage =
        "Usage: tweakpilot-cli --list [--module X] [--json] | --autostart [--json] | --games | --anticheat | --clean-scan | --apps | --onedrive-check | --volumes | --sysinfo | --large-scan | --ping <ip> | --status <id> | --apply <id,...> [--dry-run] [--silent] [--allow-expert] [--no-restore-point]"
        + " | --profile <name|file> [--dry-run] [--silent] [--allow-expert] | --export ps1|reg --profile <name|file> --out <file>"
        + " | --revert <id> | --revert-all | --recover | --version  [--lang en|de] [--state-root <folder>]";

    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "--list", "--autostart", "--games", "--anticheat", "--clean-scan", "--large-scan", "--apps", "--onedrive-check", "--volumes", "--sysinfo", "--ping", "--status", "--apply", "--revert", "--revert-all", "--recover", "--version", "--export",
    };

    public string Command { get; private set; } = string.Empty;

    public IReadOnlyList<string> Ids { get; private set; } = [];

    public bool DryRun { get; private set; }

    public bool Silent { get; private set; }

    public bool AllowExpert { get; private set; }

    public bool NoRestorePoint { get; private set; }

    public bool Json { get; private set; }

    /// <summary>Null: language from the saved settings, otherwise English (V3).</summary>
    public string? Language { get; private set; }

    /// <summary>Nur fuer Tests: eigener Zustandsordner statt %LOCALAPPDATA% bzw. %ProgramData%.</summary>
    public string? StateRoot { get; private set; }

    public string? Module { get; private set; }

    public string? ProfileName { get; private set; }

    public string? ExportFormat { get; private set; }

    public string? Out { get; private set; }

    /// <summary>Profil-Anwendung nutzt denselben Ablauf wie --apply.</summary>
    public void SetIds(IReadOnlyList<string> ids) => Ids = ids;

    public static Arguments Parse(string[] args)
    {
        var result = new Arguments();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(Text.L.Format("cli.args.value", ("arg", arg)));

            if (Commands.Contains(arg))
            {
                if (result.Command.Length > 0)
                {
                    throw new ArgumentException(Text.L["cli.args.onecommand"]);
                }

                result.Command = arg;
                if (arg is "--status" or "--apply" or "--revert" or "--ping")
                {
                    result.Ids = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                if (arg == "--export")
                {
                    result.ExportFormat = Next() is "ps1" or "reg" ? args[i] : throw new ArgumentException(Text.L["cli.args.format"]);
                }

                continue;
            }

            switch (arg)
            {
                case "--dry-run": result.DryRun = true; break;
                case "--silent": result.Silent = true; break;
                case "--allow-expert": result.AllowExpert = true; break;
                case "--no-restore-point": result.NoRestorePoint = true; break;
                case "--json": result.Json = true; break;
                case "--lang":
                    result.Language = Next() is "de" or "en" ? args[i] : throw new ArgumentException(Text.L["cli.args.lang"]);
                    break;
                case "--state-root": result.StateRoot = Path.GetFullPath(Next()); break;
                case "--module":
                    result.Module = Next() is "PRIV" or "PERF" or "GAME" or "UI" or "DEB" or "NET" or "DISK" or "UPD" ? args[i] : throw new ArgumentException(Text.L["cli.args.module.value"]);
                    break;
                case "--profile": result.ProfileName = Next(); break;
                case "--out": result.Out = Path.GetFullPath(Next()); break;
                default: throw new ArgumentException(Text.L.Format("cli.args.unknown", ("arg", arg)));
            }
        }

        // --profile ist allein ein Befehl (Profil anwenden), zusammen mit --export die Quelle des Exports.
        if (result.Command.Length == 0 && result.ProfileName is not null)
        {
            result.Command = "--profile";
        }

        if (result.Command.Length == 0)
        {
            throw new ArgumentException(Text.L["cli.args.nocommand"]);
        }

        if (result.Command == "--export" && (result.ProfileName is null || result.Out is null))
        {
            throw new ArgumentException(Text.L["cli.args.export"]);
        }

        if (result.ProfileName is not null && result.Command is not ("--profile" or "--export"))
        {
            throw new ArgumentException(Text.L["cli.args.profile"]);
        }

        if (result.Module is not null && result.Command != "--list")
        {
            throw new ArgumentException(Text.L["cli.args.module"]);
        }

        if (result.Command is "--status" or "--apply" or "--revert" && result.Ids.Count == 0)
        {
            throw new ArgumentException(Text.L.Format("cli.args.ids", ("command", result.Command)));
        }

        return result;
    }
}
