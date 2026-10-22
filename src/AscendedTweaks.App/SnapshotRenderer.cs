#if DEBUG
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.App.Views;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.App;

/// <summary>
/// Nur Entwicklungs-Builds: rendert Seiten in Hell und Dunkel, bei 100 und 200 Prozent sowie schmal (eingeklappte Navigation)
/// als PNG. Aufruf: AscendedTweaks.App.exe --snapshot &lt;Ordner&gt;. 200 Prozent entspricht 1920 x 1080 Pixeln abzueglich Taskleiste.
/// </summary>
internal static class SnapshotRenderer
{
    private static readonly (string Name, double Width, double Height, double Dpi)[] Sizes =
    [
        ("100", 1280, 800, 96),
        ("200", 960, 516, 192),
        ("narrow", 900, 700, 96),
    ];

    /// <summary>Beispieldaten fuer Bildschirmfotos, damit keine echte App-Liste in die Doku geraet.</summary>
    public static IReadOnlyList<PermissionCapability> SamplePermissions() =>
    [
        new("webcam", ConsentState.Allow, ConsentState.Allow, ConsentState.Allow,
        [
            new("Microsoft.WindowsCamera", null, false, ConsentState.Allow, new DateTimeOffset(2026, 10, 12, 18, 30, 0, TimeSpan.Zero)),
            new("videocall.exe", @"C:\Program Files\Example\videocall.exe", true, ConsentState.Inherited, null),
        ]),
        new("microphone", ConsentState.Allow, ConsentState.Allow, ConsentState.Deny,
        [
            new("Microsoft.WindowsSoundRecorder", null, false, ConsentState.Prompt, null),
        ]),
        new("location", ConsentState.Deny, ConsentState.Allow, ConsentState.Inherited, []),
    ];

    public static IReadOnlyList<AutostartEntry> SampleAutostart() =>
    [
        Sample("Beispiel-Chat", AutostartSource.Run, AutostartLocation.UserRun, @"C:\Program Files\Example\chat.exe", "Example Inc.", AutostartRating.Optional, "autostart.reason.known", true),
        Sample("Audio control", AutostartSource.Run, AutostartLocation.MachineRun, @"C:\Program Files\Audio\control.exe", "Realtek Semiconductor Corp.", AutostartRating.Keep, "autostart.reason.driver", true),
        Sample("SecurityHealth", AutostartSource.Run, AutostartLocation.MachineRun, @"C:\Windows\System32\SecurityHealthSystray.exe", null, AutostartRating.Keep, "autostart.reason.windows", true),
        Sample("Updater.lnk", AutostartSource.StartupFolder, AutostartLocation.UserStartupFolder, @"C:\Program Files\Example\update.exe", "Example Inc.", AutostartRating.Optional, "autostart.reason.updater", false),
        Sample("Unknown", AutostartSource.Run, AutostartLocation.UserRun, @"C:\Temp\tool.exe", null, AutostartRating.Unknown, "autostart.reason.unsigned", true),
    ];

    public static IReadOnlyList<DetectedGame> SampleGames() =>
    [
        new("Fortnite", GameSource.Epic, @"C:\Program Files\Epic Games\Fortnite", @"C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true", AntiCheat.EasyAntiCheat),
        new("Beispielspiel", GameSource.Steam, @"C:\Steam\steamapps\common\Example", null, "steam://rungameid/1", AntiCheat.None),
    ];

    public sealed class SampleCleanup : ICleanupService
    {
        public CleanupScan ScanCleanup(string categoryId) => categoryId switch
        {
            "CLEAN-001" => new CleanupScan { CategoryId = categoryId, Files = 120, Bytes = 85_000_000 },
            "CLEAN-003" => new CleanupScan { CategoryId = categoryId, Files = 40, Bytes = 1_200_000_000, Estimated = true },
            "CLEAN-006" => new CleanupScan { CategoryId = categoryId, Files = 12, Bytes = 300_000_000 },
            "CLEAN-010" => new CleanupScan { CategoryId = categoryId, Blocked = "browser-open:chrome" },
            "CLEAN-013" => new CleanupScan { CategoryId = categoryId, Files = 250, Bytes = 640_000_000 },
            "CLEAN-014" => new CleanupScan { CategoryId = categoryId, SizeUnknown = true, Estimated = true },
            _ => new CleanupScan { CategoryId = categoryId },
        };

        public CleanupResult RunCleanup(string categoryId, CleanupMode mode) => new() { CategoryId = categoryId, Mode = mode };
    }

    public sealed class SampleApps : IAppManager
    {
        public IReadOnlyList<AscendedTweaks.Engine.Debloat.AppxPackageInfo> ListInstalled() =>
        [
            Pkg("Microsoft.BingNews", true), Pkg("Microsoft.BingWeather", true), Pkg("Microsoft.GamingApp", true), Pkg("Clipchamp.Clipchamp", true),
            Pkg("Example.Tool", false, "Example Inc."), Pkg("Microsoft.WindowsStore", true),
        ];

        public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> Remove(IReadOnlyList<string> packageNames, bool deprovision) => [];

        public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemovedApps() => [];

        public AscendedTweaks.Engine.Debloat.RestoreOutcome Restore(AscendedTweaks.Engine.Debloat.RemovedApp app) => AscendedTweaks.Engine.Debloat.RestoreOutcome.Registered;

        private static AscendedTweaks.Engine.Debloat.AppxPackageInfo Pkg(string name, bool microsoft, string publisher = "Microsoft Corporation") =>
            new(name, name + "_1_x64__x", name + "_x", publisher, microsoft, false, false, false, null);
    }

    public sealed class SampleNetwork : INetworkTools
    {
        public Task<AscendedTweaks.Engine.Tools.PingStats> PingAsync(string target, IProgress<int>? progress, CancellationToken cancellation) =>
            Task.FromResult(new AscendedTweaks.Engine.Tools.PingStats(target, 50, 50, 18, 21, 30, 1.8));

        public bool FlushDnsCache() => true;

        public AscendedTweaks.Engine.Tools.DohStatus Doh() => new(true, ["1.1.1.1"], ["1.1.1.1"]);

        public AscendedTweaks.Engine.Tools.MaintenanceResult NetworkReset() => new(true, true, null);

        public void OpenDnsSettings()
        {
        }
    }

    public sealed class SampleDisks : IDiskTools
    {
        public IReadOnlyList<AscendedTweaks.Engine.Tools.VolumeInfo> Volumes() =>
        [
            new('C', AscendedTweaks.Engine.Tools.MediaKind.Ssd, 1_000_000_000_000, 650_000_000_000, "Beispiel-SSD 1 TB"),
            new('D', AscendedTweaks.Engine.Tools.MediaKind.Hdd, 2_000_000_000_000, 900_000_000_000, "Beispiel-HDD 2 TB"),
        ];

        public AscendedTweaks.Engine.Tools.MaintenanceResult Run(AscendedTweaks.Engine.Tools.MaintenanceAction action, char drive) => new(true, false, null);

        public IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> Health() => [];

        public AscendedTweaks.Engine.Tools.LargeFileScan ScanLargeFiles(long threshold, IReadOnlyList<string> keep, IProgress<int> progress, CancellationToken cancellation)
        {
            var now = DateTimeOffset.UtcNow;
            const long gb = 1L << 30;
            return new(
            [
                new(@"C:\Users\Example\Downloads\Win11_25H2.iso", 6 * gb, now.AddDays(-112), AscendedTweaks.Engine.Tools.LargeFileClass.A, "large.why.installer", [("days", 112)]),
                new(@"C:\Users\Example\Downloads\Game-Setup.exe", 2 * gb, now.AddDays(-45), AscendedTweaks.Engine.Tools.LargeFileClass.A, "large.why.installer", [("days", 45)]),
                new(@"C:\Users\Example\Desktop\Recording 2024.mp4", 4 * gb, now.AddDays(-400), AscendedTweaks.Engine.Tools.LargeFileClass.B, "large.why.oldvideo", [("days", 400)]),
                new(@"D:\Kopien\Vacation.mp4", 3 * gb, now.AddDays(-300), AscendedTweaks.Engine.Tools.LargeFileClass.B, "large.why.duplicate", [("original", "Vacation.mp4")], DuplicateGroup: "A1B2C3D4E5F6"),
                new(@"C:\Users\Example\Videos\Vacation.mp4", 3 * gb, now.AddDays(-500), AscendedTweaks.Engine.Tools.LargeFileClass.C, "large.why.library", [], DuplicateGroup: "A1B2C3D4E5F6", IsOriginal: true),
                new(@"D:\Games\Fortnite\FortniteGame\Content\Paks\pakchunk0-WindowsClient.ucas", 20 * gb, now.AddDays(-5), AscendedTweaks.Engine.Tools.LargeFileClass.D, "large.why.game", [("game", "Fortnite")]),
            ],
            [new(@"D:\Games\Fortnite", "Fortnite", 98 * gb), new(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive", "Counter-Strike Global Offensive", 34 * gb)],
            12_840,
            false,
            false,
            [@"D:\Games\Fortnite"]);
        }

        public AscendedTweaks.Engine.Tools.RecycleOutcome RecycleLargeFiles(IReadOnlyList<AscendedTweaks.Engine.Tools.LargeFileItem> items, IReadOnlyList<string> gameRoots) => new([], []);

        public void ShowInExplorer(string path)
        {
        }
    }

    public sealed class SampleSystem : ISystemInfoSource
    {
        public SystemInfo Read() => new(
            new CpuInfo("Beispiel-CPU 8 Kerne", 8, 16),
            [new GpuInfo("NVIDIA", "Beispiel-Grafikkarte", "32.0.15.0000", new DateOnly(2026, 8, 1), 12L << 30)],
            new RamInfo(32L << 30, 12L << 30, 37, "DDR5"),
            [new DiskInfo("Beispiel-NVMe 1 TB", DiskKind.Nvme, 1_000_204_886_016, 605_000_000_000), new DiskInfo("Beispiel-HDD 2 TB", DiskKind.Hdd, 2_000_398_934_016, 900_000_000_000)],
            new WindowsInfo("Windows 11 Pro", "25H2", "26200.1000", new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)),
            new BootInfo(new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero), null, true));

        public HealthFacts ReadHealth() => new(false, "Ausbalanciert", true, true, ["Microsoft Defender Antivirus"], 60, 37);
    }

    public sealed class SampleLogs : ILogFolder
    {
        public (int Files, long Bytes) Measure() => (3, 412_000);

        public int Delete() => 0;

        public void Open()
        {
        }
    }

    public sealed class SampleUpdate : IUpdateService
    {
        public Task<AscendedTweaks.Engine.Update.UpdateCheckResult> CheckAsync() =>
            Task.FromResult(new AscendedTweaks.Engine.Update.UpdateCheckResult(AscendedTweaks.Engine.Update.UpdateCheckOutcome.NotConfigured, null, null));

        public Task<AscendedTweaks.Engine.Update.UpdateDownload> DownloadAsync(AscendedTweaks.Engine.Update.UpdateManifest manifest) =>
            Task.FromResult(new AscendedTweaks.Engine.Update.UpdateDownload(false, null, null));

        public void StartInstaller(string path)
        {
        }
    }

    public sealed class SampleUpdates : IUpdateStatusSource
    {
        public UpdateStatus Read() => new(null, 11, "25H2");
    }

    public sealed class SampleMetrics : IProfileMetrics
    {
        public ProfileMetrics Capture() => new(9, 6_400_000_000, 605_000_000_000);
    }

    public sealed class SampleHardware : AscendedTweaks.Engine.Windows.IHardwareFacts
    {
        public DisplayFacts? ReadDisplay() => new(60, 144);

        public IReadOnlyList<MemoryModule>? ReadMemoryModules() => [new(6000, 4800), new(6000, 4800)];
    }

    public sealed class SampleSecurity : ISecurityFactsSource
    {
        public SecurityFacts Read() => new(true, 2, true, true, false, false);
    }

    /// <summary>Feste Prozessliste; die zweite Messung hat mehr CPU-Zeit, damit beide Listen gefuellt sind.</summary>
    public sealed class SampleProcesses : IProcessSampler
    {
        private int _calls;

        public IReadOnlyList<ProcessSample> Sample()
        {
            var factor = _calls++;
            return
            [
                new(1, "Game", TimeSpan.FromMilliseconds(900 * factor), 3_200_000_000),
                new(2, "Browser", TimeSpan.FromMilliseconds(200 * factor), 900_000_000),
                new(3, "Browser", TimeSpan.FromMilliseconds(100 * factor), 600_000_000),
                new(4, "Chat", TimeSpan.FromMilliseconds(40 * factor), 400_000_000),
                new(5, "explorer", TimeSpan.FromMilliseconds(20 * factor), 180_000_000),
                new(6, "Virenschutz", TimeSpan.FromMilliseconds(60 * factor), 250_000_000),
            ];
        }
    }

    private static AutostartEntry Sample(string name, AutostartSource source, AutostartLocation location, string path, string? publisher, AutostartRating rating, string reason, bool enabled) => new()
    {
        Name = name,
        Source = source,
        Location = location,
        MachineWide = location is AutostartLocation.MachineRun or AutostartLocation.MachineRun32 or AutostartLocation.CommonStartupFolder,
        Command = path,
        FilePath = path,
        Enabled = enabled,
        Signature = new SignatureInfo(publisher is null ? SignatureStatus.Unsigned : SignatureStatus.Valid, publisher),
        Rating = rating,
        ReasonKey = reason,
        TweakId = AutostartTweaks.IdFor(location, name),
    };

    private static readonly string[] Pages = ["nav.dashboard", "nav.performance", "nav.gaming", "nav.cleanup", "nav.debloat", "nav.network", "nav.disk", "nav.updates", "nav.profiles", "nav.privacy", "nav.restore", "nav.settings", "nav.catalog"];

    /// <summary>
    /// Jedes sichtbare Bedienelement braucht einen Namen fuer Screenreader (AutomationProperties.Name oder Inhaltstext).
    /// Listeneintraege duerfen nicht den Typnamen eines ViewModels vorlesen.
    /// </summary>
    private static void Audit(DependencyObject root, string context)
    {
        var page = context.Split('-', 3) is { Length: 3 } parts ? parts[2] : context;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
            {
                stack.Push(VisualTreeHelper.GetChild(current, i));
            }

            // Nur Elemente, die per Tastatur erreichbar sind; Teile von Bildlaufleisten und Auswahllisten sind es nicht.
            if (current is not UIElement { IsVisible: true, Focusable: true } element
                || current is not (System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.ComboBox or System.Windows.Controls.TextBox or System.Windows.Controls.ListBoxItem or System.Windows.Controls.Slider))
            {
                continue;
            }

            _a11yChecked++;
            var name = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(element)?.GetName();
            var label = (current as FrameworkElement)?.DataContext?.GetType().Name ?? "-";
            if (string.IsNullOrWhiteSpace(name))
            {
                A11yFindings.Add($"{page}: {current.GetType().Name} without a name (data context {label})");
            }
            else if (name.StartsWith("AscendedTweaks.", StringComparison.Ordinal) || name.Contains(" { ", StringComparison.Ordinal))
            {
                A11yFindings.Add($"{page}: {current.GetType().Name} reads out the type name: {name[..Math.Min(name.Length, 80)]}");
            }
        }
    }

    /// <summary>V2: alle Seiten des Einfach-Modus (hell, dunkel, 100 und 200 Prozent, schmal), dazu Suche und "Mehr anzeigen".</summary>
    private static void RenderSimpleMode(Window window, ShellViewModel shell, IThemeService theme, string directory)
    {
        Directory.CreateDirectory(directory);
        shell.ApplyMode(false);
        void Pump()
        {
            for (var i = 0; i < 20; i++)
            {
                window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                System.Threading.Thread.Sleep(25);
            }
        }

        foreach (var choice in new[] { ThemeChoice.Dark, ThemeChoice.Light })
        {
            theme.Apply(choice);
            foreach (var (name, width, height, dpi) in Sizes)
            {
                window.Width = width;
                window.Height = height;
                foreach (var item in shell.Items.Concat(shell.FooterItems).ToList())
                {
                    if (shell.Items.Contains(item))
                    {
                        shell.SelectedItem = item;
                    }
                    else
                    {
                        shell.SelectedFooterItem = item;
                    }

                    Pump();
                    Render(window, dpi, Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{choice}-{name}-{item.Key.Replace("nav.simple.", string.Empty, StringComparison.Ordinal).Replace("nav.", string.Empty, StringComparison.Ordinal)}.png")));
                }
            }
        }

        // Preset-Plaene als Text, damit Zaehler im Bild nachvollziehbar sind (welche Zeile warum uebersprungen wird).
        shell.SelectedItem = shell.Items.First(i => i.Key == "nav.simple.start");
        Pump();
        if (shell.CurrentPage is SimpleStartViewModel start)
        {
            File.WriteAllLines(Path.Combine(directory, "preset-plans.txt"), start.Presets.SelectMany(p =>
                new[] { $"{p.OwnerKey}: {string.Join(", ", p.Levels.Select(l => l.Label))}" }
                    .Concat(p.Plan.Select(r => $"  {r.Id} {r.Action} {r.Text}"))));
        }

        // Suche und "Mehr anzeigen" auf "Schneller".
        window.Width = 1280;
        window.Height = 800;
        shell.SelectedItem = shell.Items.First(i => i.Key == "nav.simple.faster");
        if (shell.CurrentPage is SimpleTweakPageViewModel faster)
        {
            faster.Query = "Start";
            Pump();
            Render(window, 96, Path.Combine(directory, "Light-100-faster-search.png"));
            faster.Query = string.Empty;
        }

        shell.ApplyMode(true);
    }

    private static IEnumerable<System.Windows.Controls.ScrollViewer> FindScrollViewers(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer viewer)
            {
                yield return viewer;
            }

            foreach (var nested in FindScrollViewers(child))
            {
                yield return nested;
            }
        }
    }

    private static void WriteA11yReport(string directory)
    {
        var lines = new List<string> { $"Checked controls (with repeats across themes and sizes): {_a11yChecked}", $"Findings: {A11yFindings.Count}" };
        lines.AddRange(A11yFindings);
        File.WriteAllLines(Path.Combine(directory, "a11y-report.txt"), lines);
    }

    public static void Run(ShellWindow window, ShellViewModel shell, IThemeService theme, string directory, FirstStepsViewModel? firstSteps = null)
    {
        Directory.CreateDirectory(directory);
        ShellWindow.AnimationsEnabled = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = 0;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();

        // Die bisherigen Aufnahmen zeigen den Experten-Modus (alle Seiten).
        shell.ApplyMode(true);
        shell.Workspace?.SetExpertMode(false);

        foreach (var choice in new[] { ThemeChoice.Dark, ThemeChoice.Light })
        {
            theme.Apply(choice);
            foreach (var (name, width, height, dpi) in Sizes)
            {
                window.Width = width;
                window.Height = height;
                foreach (var key in Pages)
                {
                    shell.SelectedItem = shell.Items.First(i => i.Key == key);
                    Render(window, dpi, Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{choice}-{name}-{key[4..]}.png")));
                }
            }

            // Detailpanel, Vorschau-Dialog und First steps (Schritt 2).
            window.Width = 1280;
            window.Height = 800;
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.privacy");
            var workspace = shell.Workspace!;
            workspace.OpenDetails(workspace.Card("PRIV-013"));
            Render(window, 96, Path.Combine(directory, $"{choice}-details.png"));
            workspace.CloseDetailsCommand.Execute(null);

            var plan = workspace.Service.Preview(["PRIV-013"]);
            var preview = new PreviewDialogViewModel(
                plan.Items.Select(i => new PreviewRow(workspace.NameOf(i.TweakId), $"{i.Target} : {i.ValueName}", i.Current, i.New)).ToList(),
                [],
                [],
                plan.RequiresReboot,
                false,
                false);
            shell.Dialogs.Show(preview);
            Render(window, 96, Path.Combine(directory, $"{choice}-dialog-preview.png"));
            shell.Dialogs.Dismiss(preview);

            var expert = new ExpertWarningViewModel(AscendedTweaks.Localization.Localizer.Current, [workspace.NameOf("PRIV-013")]) { Stage = 3, Understood = true };
            shell.Dialogs.Show(expert);
            Render(window, 96, Path.Combine(directory, $"{choice}-dialog-expert.png"));
            shell.Dialogs.Dismiss(expert);

            if (firstSteps is not null)
            {
                firstSteps.Step = 2;
                shell.Dialogs.Show(firstSteps);
                Render(window, 96, Path.Combine(directory, $"{choice}-first-steps.png"));
                shell.Dialogs.Dismiss(firstSteps);
            }

            // Bereiche der Leistungsseite.
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.performance");
            if (shell.CurrentPage is PerformancePageViewModel performance)
            {
                foreach (var section in performance.Sections.Skip(1))
                {
                    performance.SelectedSection = section;
                    for (var i = 0; i < 40 && IsLoading(performance.CurrentSection); i++)
                    {
                        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                        System.Threading.Thread.Sleep(100);
                    }

                    Render(window, 96, Path.Combine(directory, $"{choice}-performance-{section.Key}.png"));
                }

                performance.SelectedSection = performance.Sections[0];
            }

            // Bereiche der Netzwerk- und Datentraegerseite.
            foreach (var key in new[] { "nav.network", "nav.disk", "nav.updates" })
            {
                shell.SelectedItem = shell.Items.First(i => i.Key == key);
                if (shell.CurrentPage is SectionPageViewModel sectionPage)
                {
                    foreach (var section in sectionPage.Sections.Skip(1))
                    {
                        sectionPage.SelectedSection = section;
                        for (var i = 0; i < 10; i++)
                        {
                            window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                            System.Threading.Thread.Sleep(50);
                        }

                        Render(window, 96, Path.Combine(directory, $"{choice}-{key[4..]}-{section.Key}.png"));
                    }

                    sectionPage.SelectedSection = sectionPage.Sections[0];
                }
            }

            // Profilseite mit fertiger Vorschau (die Planung laeuft im Hintergrund).
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.profiles");
            if (shell.CurrentPage is ProfilesPageViewModel profilesPage)
            {
                for (var i = 0; i < 100 && profilesPage.Plan.Count == 0; i++)
                {
                    window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                    System.Threading.Thread.Sleep(50);
                }

                Render(window, 96, Path.Combine(directory, $"{choice}-profiles-plan.png"));
            }

            // Bereiche der Debloat-Seite.
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.debloat");
            if (shell.CurrentPage is DebloatPageViewModel debloat)
            {
                foreach (var section in debloat.Sections.Skip(1))
                {
                    debloat.SelectedSection = section;
                    for (var i = 0; i < 40 && debloat.CurrentSection is OneDriveViewModel { IsBusy: true }; i++)
                    {
                        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                        System.Threading.Thread.Sleep(100);
                    }

                    Render(window, 96, Path.Combine(directory, $"{choice}-debloat-{section.Key}.png"));
                }

                debloat.SelectedSection = debloat.Sections[0];
            }

            // Bereinigung nach dem Scan.
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.cleanup");
            if (shell.CurrentPage is CleanupPageViewModel cleanup)
            {
                var scan = cleanup.ScanAsync();
                for (var i = 0; i < 200 && !scan.IsCompleted; i++)
                {
                    window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                    System.Threading.Thread.Sleep(50);
                }

                Render(window, 96, Path.Combine(directory, $"{choice}-cleanup-scanned.png"));
            }

            // Grosse Dateien nach der Suche (Beispieldaten), mit geschuetzten Bereichen.
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.disk");
            if (shell.CurrentPage is SectionPageViewModel disk && disk.Sections.FirstOrDefault(s => s.Key == "large") is { } largeSection)
            {
                disk.SelectedSection = largeSection;
                if (disk.CurrentSection is LargeFilesViewModel large)
                {
                    var scan = large.ScanCommand.ExecuteAsync(null);
                    for (var i = 0; i < 200 && !scan.IsCompleted; i++)
                    {
                        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                        System.Threading.Thread.Sleep(50);
                    }

                    large.ShowProtected = true;
                    window.Height = 2000;
                    window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
                    Render(window, 96, Path.Combine(directory, $"{choice}-disk-large-scanned.png"));
                    window.Height = 800;
                }
            }

            // Bereiche der Gaming-Seite.
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.gaming");
            if (shell.CurrentPage is GamingPageViewModel gaming)
            {
                foreach (var section in gaming.Sections.Skip(1))
                {
                    gaming.SelectedSection = section;
                    for (var i = 0; i < 40 && gaming.CurrentSection is GameProfilesViewModel { IsLoading: true } or AntiCheatViewModel { IsLoading: true }; i++)
                    {
                        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
                        System.Threading.Thread.Sleep(100);
                    }

                    Render(window, 96, Path.Combine(directory, $"{choice}-gaming-{section.Key}.png"));
                }

                gaming.SelectedSection = gaming.Sections[0];
            }

            // Gestaltungskatalog vollstaendig (hohes Fenster), um alle Zustaende zu sehen.
            window.Width = 1280;
            window.Height = 2400;
            shell.SelectedItem = shell.Items.First(i => i.Key == "nav.catalog");
            Render(window, 96, Path.Combine(directory, $"{choice}-full-catalog.png"));
        }

        // Einstellungen ans Ende gescrollt: Wartung und "Ueber".
        shell.SelectedItem = shell.Items.First(i => i.Key == "nav.settings");
        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        ((FrameworkElement)window.Content).UpdateLayout();
        var scroller = FindScrollViewers(window).Where(v => v.ScrollableHeight > 0).OrderByDescending(v => v.ScrollableHeight).FirstOrDefault();
        scroller?.ScrollToEnd();
        Render(window, 96, Path.Combine(directory, "Light-settings-about.png"));
        scroller?.ScrollToHome();

        // Hochkontrast mit den aktuellen Systemfarben (Abschnitt 8.6).
        ThemeService.HighContrastOverride = true;
        theme.Apply(ThemeChoice.Dark);
        foreach (var key in new[] { "nav.dashboard", "nav.privacy" })
        {
            shell.SelectedItem = shell.Items.First(i => i.Key == key);
            Render(window, 96, Path.Combine(directory, $"HighContrast-100-{key[4..]}.png"));
        }

        ThemeService.HighContrastOverride = null;
        theme.Apply(ThemeChoice.Dark);

        RenderSimpleMode(window, shell, theme, Path.Combine(directory, "v2"));

        WriteA11yReport(directory);
        window.Close();
    }

    private static bool IsLoading(object? section) => section switch
    {
        AutostartViewModel a => a.IsLoading,
        ProcessesViewModel p => p.IsMeasuring,
        _ => false,
    };

    /// <summary>Befunde der Barrierefreiheits-Pruefung (Abschnitt 8.6), gesammelt ueber alle Seiten, Themen und Groessen.</summary>
    private static readonly SortedSet<string> A11yFindings = new(StringComparer.Ordinal);

    private static int _a11yChecked;

    private static void Render(Window window, double dpi, string file)
    {
        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        Audit(root, Path.GetFileNameWithoutExtension(file));
        var scale = dpi / 96;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth * scale),
            (int)Math.Ceiling(root.ActualHeight * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
#endif
