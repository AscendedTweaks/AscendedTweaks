using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.App.Services;

/// <summary>Spielprofil (Abschnitt 6.3): EXE plus gewuenschte Einstellungen. Angewendet wird nur ueber die Engine (Vorschau, Sicherung).</summary>
public sealed class GameProfile
{
    public required string Name { get; set; }

    public required string ExePath { get; set; }

    public GameSource Source { get; set; } = GameSource.Manual;

    /// <summary>Start ueber den Launcher (Epic, Steam); sonst wird die EXE direkt gestartet.</summary>
    public string? LaunchUri { get; set; }

    public AntiCheat AntiCheat { get; set; }

    public bool GpuHighPerformance { get; set; }

    public bool DisableFullscreenOptimizations { get; set; }

    public bool StartHighPriority { get; set; }

    /// <summary>GAME-016 (Experte); bei Spielen mit Anti-Cheat nie erlaubt.</summary>
    public bool PermanentPriority { get; set; }

    public bool HasAntiCheat => AntiCheat != AntiCheat.None;

    /// <summary>Tweak-IDs, die dieses Profil angewendet haben moechte.</summary>
    public IReadOnlyList<string> DesiredTweakIds()
    {
        var ids = new List<string>();
        if (!GameTweaks.IsValidExePath(ExePath))
        {
            return ids;
        }

        if (GpuHighPerformance)
        {
            ids.Add(GameTweaks.GpuId(2, ExePath));
        }

        if (DisableFullscreenOptimizations)
        {
            ids.Add(GameTweaks.FullscreenId(ExePath));
        }

        if (PermanentPriority && !HasAntiCheat && GameTweaks.IsValidExeName(Path.GetFileName(ExePath)))
        {
            ids.Add(GameTweaks.PriorityId(Path.GetFileName(ExePath)));
        }

        return ids;
    }

    /// <summary>Alle Tweak-IDs, die zu diesem Profil gehoeren koennen (zum Zuruecksetzen).</summary>
    public IReadOnlyList<string> AllTweakIds()
    {
        if (!GameTweaks.IsValidExePath(ExePath))
        {
            return [];
        }

        var ids = new List<string> { GameTweaks.GpuId(1, ExePath), GameTweaks.GpuId(2, ExePath), GameTweaks.FullscreenId(ExePath) };
        if (GameTweaks.IsValidExeName(Path.GetFileName(ExePath)))
        {
            ids.Add(GameTweaks.PriorityId(Path.GetFileName(ExePath)));
        }

        return ids;
    }
}

/// <summary>Profile pro Benutzer in %LOCALAPPDATA%\AscendedTweaks\games.json; beschaedigte Dateien ergeben eine leere Liste.</summary>
public sealed class GameProfileStore
{
    private static readonly JsonSerializerOptions Options = new(CoreJson.Options) { WriteIndented = true };

    public GameProfileStore(string path) => FilePath = path;

    public static string DefaultPath =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "games.json");

    public string FilePath { get; }

    public List<GameProfile> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<GameProfile>>(File.ReadAllText(FilePath), Options) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<GameProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles.ToList(), Options));
    }
}

public enum LaunchOutcome
{
    Started,
    StartedWithPriority,
    PriorityDenied,
    GameNotFound,
    Failed,
}

public sealed record LaunchResult(LaunchOutcome Outcome, string? Detail = null);

public interface IGameLauncher
{
    /// <summary>GAME-015: startet das Spiel und setzt bei Bedarf die Prioritaet "Hoch" fuer diese eine Sitzung.</summary>
    Task<LaunchResult> StartAsync(GameProfile profile, CancellationToken cancellation);
}

/// <summary>
/// Startet ueber den Launcher oder direkt. Die Prioritaet wird nur fuer den laufenden Prozess gesetzt (kein Dauer-Eingriff).
/// Verweigert Windows oder ein Anti-Cheat den Zugriff, wird das gemeldet und nicht umgangen.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Startet echte Prozesse.")]
public sealed class GameLauncher : IGameLauncher
{
    private static readonly TimeSpan WaitForGame = TimeSpan.FromMinutes(2);

    public async Task<LaunchResult> StartAsync(GameProfile profile, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            if (profile.LaunchUri is { } uri)
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
            }
            else
            {
                if (!File.Exists(profile.ExePath))
                {
                    return new LaunchResult(LaunchOutcome.GameNotFound);
                }

                using var direct = Process.Start(new ProcessStartInfo(profile.ExePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(profile.ExePath)! });
                if (profile.StartHighPriority && direct is not null)
                {
                    return SetHigh(direct);
                }
            }

            if (!profile.StartHighPriority)
            {
                return new LaunchResult(LaunchOutcome.Started);
            }

            var name = Path.GetFileNameWithoutExtension(profile.ExePath);
            var deadline = DateTime.UtcNow + WaitForGame;
            while (DateTime.UtcNow < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                var running = Process.GetProcessesByName(name);
                try
                {
                    if (running.Length > 0)
                    {
                        return SetHigh(running[0]);
                    }
                }
                finally
                {
                    foreach (var process in running)
                    {
                        process.Dispose();
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellation).ConfigureAwait(false);
            }

            return new LaunchResult(LaunchOutcome.GameNotFound);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
    }

    private static LaunchResult SetHigh(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.High;
            return new LaunchResult(LaunchOutcome.StartedWithPriority);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return new LaunchResult(LaunchOutcome.PriorityDenied, ex.Message);
        }
    }
}

public interface IFilePicker
{
    /// <summary>Waehlt eine .exe; null bei Abbruch.</summary>
    string? PickExe(string? initialFolder);
}

[ExcludeFromCodeCoverage(Justification = "Systemdialog.")]
public sealed class WindowsFilePicker : IFilePicker
{
    public string? PickExe(string? initialFolder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs (*.exe)|*.exe",
            InitialDirectory = initialFolder is not null && Directory.Exists(initialFolder) ? initialFolder : string.Empty,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

/// <summary>Bereinigung fuer die Oberflaeche; die echte Umsetzung ist der TweakService (Benutzer lokal, Systemordner im Helfer).</summary>
public interface ICleanupService
{
    CleanupScan ScanCleanup(string categoryId);

    CleanupResult RunCleanup(string categoryId, CleanupMode mode);
}

public sealed class TweakServiceCleanup(AscendedTweaks.Ipc.TweakService service) : ICleanupService
{
    public CleanupScan ScanCleanup(string categoryId) => service.ScanCleanup(categoryId);

    public CleanupResult RunCleanup(string categoryId, CleanupMode mode) => service.RunCleanup(categoryId, mode);
}

/// <summary>Apps: Lesen im Benutzerkontext, Entfernen im Helfer, Liste und Wiederherstellung im Benutzerkontext.</summary>
public sealed class AppManager : AscendedTweaks.App.ViewModels.IAppManager
{
    private readonly AscendedTweaks.Engine.Debloat.IAppxAccess _appx;
    private readonly Func<IReadOnlyList<string>, bool, IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp>> _remove;
    private readonly AscendedTweaks.Engine.Debloat.RemovedAppsStore _store;
    private readonly Action<string> _openUri;

    public AppManager(
        AscendedTweaks.Engine.Debloat.IAppxAccess appx,
        Func<IReadOnlyList<string>, bool, IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp>> remove,
        AscendedTweaks.Engine.Debloat.RemovedAppsStore store,
        Action<string> openUri)
    {
        _appx = appx;
        _remove = remove;
        _store = store;
        _openUri = openUri;
    }

    public IReadOnlyList<AscendedTweaks.Engine.Debloat.AppxPackageInfo> ListInstalled() => _appx.ListForCurrentUser();

    public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> Remove(IReadOnlyList<string> packageNames, bool deprovision)
    {
        var removed = _remove(packageNames, deprovision);
        var list = _store.Load();
        list.AddRange(removed.Where(r => r.Ok));
        _store.Save(list);
        return removed;
    }

    public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemovedApps() => _store.Load();

    public AscendedTweaks.Engine.Debloat.RestoreOutcome Restore(AscendedTweaks.Engine.Debloat.RemovedApp app)
    {
        var outcome = new AscendedTweaks.Engine.Debloat.AppRestorer(_appx, _openUri, File.Exists).Restore(app);
        var list = _store.Load();
        foreach (var entry in list.Where(e => e.FullName == app.FullName))
        {
            entry.Restored = outcome == AscendedTweaks.Engine.Debloat.RestoreOutcome.Registered;
        }

        _store.Save(list);
        return outcome;
    }
}

/// <summary>Netzwerk-Werkzeuge: Ping und DNS-Cache im Benutzerkontext, Netzwerk-Reset im Helfer.</summary>
public sealed class NetworkTools : AscendedTweaks.App.ViewModels.INetworkTools
{
    private readonly AscendedTweaks.Ipc.TweakService _service;
    private readonly AscendedTweaks.Core.INetworkAccess _network;
    private readonly AscendedTweaks.Core.ICommandRunner _runner;
    private readonly Action<string> _openUri;

    public NetworkTools(AscendedTweaks.Ipc.TweakService service, AscendedTweaks.Core.INetworkAccess network, AscendedTweaks.Core.ICommandRunner runner, Action<string> openUri)
    {
        _service = service;
        _network = network;
        _runner = runner;
        _openUri = openUri;
    }

    public Task<AscendedTweaks.Engine.Tools.PingStats> PingAsync(string target, IProgress<int>? progress, CancellationToken cancellation) =>
        new AscendedTweaks.Engine.Tools.PingTest(new AscendedTweaks.Engine.Windows.WindowsPinger()).RunAsync(target, progress, cancellation);

    public bool FlushDnsCache() => _runner.Run(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), ["/flushdns"], TimeSpan.FromSeconds(30)).ExitCode == 0;

    public AscendedTweaks.Engine.Tools.DohStatus Doh() =>
        AscendedTweaks.Engine.Tools.DohCheck.Evaluate(AscendedTweaks.Engine.Windows.WindowsDoh.WellKnownServers(), _network.PhysicalAdapters().SelectMany(_network.GetDnsServers).ToList());

    public AscendedTweaks.Engine.Tools.MaintenanceResult NetworkReset() => _service.RunMaintenance(AscendedTweaks.Engine.Tools.MaintenanceAction.NetworkReset, null);

    public void OpenDnsSettings() => _openUri("ms-settings:network-advancedsettings");
}

/// <summary>Datentraeger-Werkzeuge: Laufwerke und grosse Dateien im Benutzerkontext, ReTrim, Defrag und SMART im Helfer.</summary>
public sealed class DiskTools : AscendedTweaks.App.ViewModels.IDiskTools
{
    private readonly AscendedTweaks.Ipc.TweakService _service;

    public DiskTools(AscendedTweaks.Ipc.TweakService service) => _service = service;

    public IReadOnlyList<AscendedTweaks.Engine.Tools.VolumeInfo> Volumes() => new AscendedTweaks.Engine.Windows.WindowsVolumeInventory().FixedVolumes();

    public AscendedTweaks.Engine.Tools.MaintenanceResult Run(AscendedTweaks.Engine.Tools.MaintenanceAction action, char drive) => _service.RunMaintenance(action, drive);

    public IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> Health() => _service.ReadDiskHealth();

    public AscendedTweaks.Engine.Tools.LargeFileScan ScanLargeFiles(long threshold, IReadOnlyList<string> keep, IProgress<int> progress, CancellationToken cancellation)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots = new AscendedTweaks.Engine.Windows.WindowsVolumeInventory().FixedVolumes().Select(v => $"{v.Drive}:\\").ToList();
        var scanner = new AscendedTweaks.Engine.Tools.LargeFileScanner(new AscendedTweaks.Engine.Windows.WindowsCleanupFileSystem(), new AscendedTweaks.Engine.Windows.WindowsFileHasher(), AscendedTweaks.Engine.Windows.WindowsLargeFileContext.Create(keep), [windows]);
        return scanner.Scan(roots, threshold, cancellation, progress);
    }

    public AscendedTweaks.Engine.Tools.RecycleOutcome RecycleLargeFiles(IReadOnlyList<AscendedTweaks.Engine.Tools.LargeFileItem> items, IReadOnlyList<string> gameRoots) =>
        new AscendedTweaks.Engine.Tools.LargeFileRecycler(
            new AscendedTweaks.Engine.Windows.WindowsCleanupFileSystem(),
            new AscendedTweaks.Engine.Windows.WindowsRecycler(),
            new AscendedTweaks.Engine.Windows.WindowsFileInUseProbe(),
            AscendedTweaks.Engine.Windows.WindowsLargeFileContext.ProtectedRoots(),
            gameRoots).Recycle(items);


    /// <summary>"Im Explorer zeigen" markiert die Datei; geloescht wird aus dieser Liste nie.</summary>
    public void ShowInExplorer(string path)
    {
        if (AscendedTweaks.Engine.Cleanup.PathGuard.Canonical(path) is not { } safe)
        {
            return;
        }

        // explorer.exe erwartet /select,"Pfad" mit Anfuehrungszeichen NUR um den Pfad; ArgumentList wuerde bei
        // Leerzeichen das ganze Argument quoten, dann oeffnet Explorer nur "Dokumente". Pfad ist durch PathGuard
        // frei von Anfuehrungszeichen.
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var arguments = File.Exists(safe) ? $"/select,\"{safe}\"" : Path.GetDirectoryName(safe) is { } dir && Directory.Exists(dir) ? $"\"{dir}\"" : null;
        if (arguments is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorer, arguments) { UseShellExecute = false })?.Dispose();
        }
    }
}
