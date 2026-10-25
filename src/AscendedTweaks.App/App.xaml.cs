using System.IO;
using System.Windows;
using System.Windows.Threading;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.App.Views;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Debloat;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.Logging;
using AscendedTweaks.Engine.State;
using AscendedTweaks.Engine.Windows;
using AscendedTweaks.Ipc;
using AscendedTweaks.Localization;
using Wpf.Ui.Controls;

namespace AscendedTweaks.App;

/// <summary>
/// Startpunkt der Oberflaeche. Laeuft mit Standardrechten (asInvoker); Admin-Schritte uebernimmt der Helfer.
/// Komposition ohne Reflection-Scanning, Seiten werden erst beim ersten Aufruf erzeugt.
/// </summary>
public partial class App : Application
{
    private JsonLinesLog? _log;

    private TweakService? _service;

    /// <summary>Quelle der Berechtigungs-Uebersicht; im Snapshot-Modus Beispieldaten statt der echten App-Liste.</summary>
    internal static Func<IReadOnlyList<PermissionCapability>>? PermissionSource { get; set; }

    /// <summary>Quelle des Autostart-Managers; im Snapshot-Modus Beispieldaten.</summary>
    internal static Func<IReadOnlyList<AutostartEntry>>? AutostartSource { get; set; }

    /// <summary>Quelle der Prozessuebersicht; im Snapshot-Modus Beispieldaten statt der laufenden Programme.</summary>
    internal static IProcessSampler? ProcessSampler { get; set; }

    /// <summary>Spiele, Profile und Sicherheitsstatus; im Snapshot-Modus Beispieldaten.</summary>
    internal static Func<IReadOnlyList<DetectedGame>>? GameScanSource { get; set; }

    internal static GameProfileStore? GameProfiles { get; set; }

    internal static ISecurityFactsSource? SecuritySource { get; set; }

    internal static ICleanupService? CleanupSource { get; set; }

    internal static IAppManager? AppSource { get; set; }

    internal static Func<OneDriveStatus>? OneDriveCheckSource { get; set; }

    internal static INetworkTools? NetworkToolsSource { get; set; }

    internal static IUpdateStatusSource? UpdateStatusSource { get; set; }

    internal static ISystemInfoSource? SystemInfoSource { get; set; }

    internal static ILogFolder? LogFolderSource { get; set; }

    /// <summary>Schalter in den Einstellungen geaendert: Navigation und Karten folgen.</summary>
    private static Action<bool>? ExpertModeChanged { get; set; }

    /// <summary>
    /// Bundled documents: in the package under "Docs" with readable names, otherwise in the repository "docs" folder;
    /// opened in Notepad (no shell call through a file association).
    /// </summary>
    private static void OpenDocument(string name)
    {
        var packaged = name switch
        {
            "SOURCES.md" => "Sources.md",
            "THIRD-PARTY-NOTICES.md" => "Licenses.md",
            "PRIVACY.md" => "Privacy.md",
            "CHANGELOG.md" => "Changelog.md",
            _ => name,
        };
        var path = new[] { Path.Combine(AppContext.BaseDirectory, "Docs", packaged), Path.Combine(AppContext.BaseDirectory, "docs", name) }
            .FirstOrDefault(File.Exists);
        if (path is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"), $"\"{path}\"") { UseShellExecute = false });
        }
    }

    internal static IUpdateService? UpdateServiceSource { get; set; }

    internal static IProfileMetrics? ProfileMetricsSource { get; set; }

    internal static string? ProfileDirectory { get; set; }

    internal static string? PresetOwnershipPath { get; set; }

    internal static bool? HasBatterySource { get; set; }

    /// <summary>Hardware-Fakten der Gaming-Checks; im Snapshot-Modus Beispieldaten.</summary>
    internal static AscendedTweaks.Engine.Windows.IHardwareFacts? HardwareSource { get; set; }

    /// <summary>"Immer behalten"-Liste der grossen Dateien; im Snapshot-Modus eine temporaere Datei.</summary>
    internal static string? KeepListPath { get; set; }

    /// <summary>Datei der Karte "Messung"; im Snapshot-Modus eine temporaere Datei.</summary>
    internal static string? MeasurementPath { get; set; }

    /// <summary>Springt aus einem Gaming-Check zur passenden Seite des aktuellen Modus; gesetzt, sobald das Fenster steht.</summary>
    internal static Action<GamingJump>? GamingNavigator { get; set; }

    /// <summary>Applies a tweak from a gaming check ("Fix now"); set together with the navigator.</summary>
    internal static TweakWorkspace? GamingWorkspace { get; set; }

    /// <summary>
    /// Design preview: a second copy that only shows the design. Sample data everywhere, state, settings and logs in %TEMP%,
    /// no installation question, no helper; applying and resetting only show a notice. Debug builds only (sample data).
    /// </summary>
    internal static bool DesignPreview { get; private set; }

    internal const string DesignPreviewMarker = ".ascendedtweaks-design-preview";

    /// <summary>
    /// Palette of a design preview copy: the marker may name it in a line "tokens=Tokens.PreviewX.xaml". Only preview token
    /// files of the app are accepted; anything else falls back to Tokens.Preview.xaml.
    /// </summary>
    internal static string PreviewTokens(string marker)
    {
        try
        {
            var line = File.Exists(marker) ? File.ReadAllLines(marker).FirstOrDefault(l => l.StartsWith("tokens=", StringComparison.Ordinal)) : null;
            var name = line?["tokens=".Length..].Trim();
            return name is not null && System.Text.RegularExpressions.Regex.IsMatch(name, @"^Tokens\.Preview[A-Za-z]*\.xaml$") ? name : "Tokens.Preview.xaml";
        }
        catch (IOException)
        {
            return "Tokens.Preview.xaml";
        }
    }

    private static GamingChecksViewModel GamingChecks(Localizer localizer)
    {
        var commands = new ProcessCommandRunner();
        var reader = new GamingFactsReader(
            SystemInfoSource ?? new WindowsSystemInfo(commands, Environment.SystemDirectory),
            HardwareSource ?? new AscendedTweaks.Engine.Windows.WindowsHardwareFacts(),
            () => new WindowsPowerPlanAccess().GetActive(),
            () => HasBatterySource ?? BatteryInfo.HasBattery(),
            AutostartSource ?? new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(commands, Environment.SystemDirectory), new WindowsSignatureVerifier()).Read,
            ProcessSampler ?? new WindowsProcessSampler(),
            SecuritySource ?? new WindowsSecurityFacts(),
            TimeProvider.System);
        return new GamingChecksViewModel(reader.Read, localizer, OpenUri)
        {
            Navigate = jump => GamingNavigator?.Invoke(jump),
            Fix = id => GamingWorkspace?.ApplyIdsAsync([id]) ?? Task.CompletedTask,
            NameOf = id => GamingWorkspace?.NameOf(id) ?? id,
        };
    }

    internal static IDiskTools? DiskToolsSource { get; set; }

    private static IAppManager AppManagerFor(TweakWorkspace workspace) => AppSource ?? new AppManager(
        new AscendedTweaks.Appx.WindowsAppxAccess(),
        workspace.Service.RemoveApps,
        new RemovedAppsStore(Path.Combine(StateStore.DefaultUserRoot, "removed-apps.json")),
        OpenUri);

    internal static IReadOnlyList<NavItem> CreateNavItems(
        Localizer localizer,
        SettingsStore store,
        AppSettings settings,
        TweakWorkspace workspace,
        IDialogService dialogs)
    {
        var items = new List<NavItem>
        {
            new("nav.dashboard", SymbolRegular.Home24, () =>
            {
                var commands = new ProcessCommandRunner();
                var dashboard = new DashboardViewModel(
                    workspace,
                    SystemInfoSource ?? new WindowsSystemInfo(commands, Environment.SystemDirectory),
                    SecuritySource ?? new WindowsSecurityFacts(),
                    AutostartSource ?? new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(commands, Environment.SystemDirectory), new WindowsSignatureVerifier()).Read);
                FirstPageLoad ??= dashboard.LoadAsync();
                return dashboard;
            }),
            new("nav.performance", SymbolRegular.TopSpeed24, () =>
            {
                var commands = new ProcessCommandRunner();
                var systemDirectory = Environment.SystemDirectory;
                return new PerformancePageViewModel(
                    workspace,
                    () => new AutostartViewModel(
                        AutostartSource ?? new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(commands, systemDirectory), new WindowsSignatureVerifier()).Read,
                        workspace,
                        ProcessSampler ?? new WindowsProcessSampler()),
                    () => new CatalogServicesViewModel(workspace, new WindowsServiceAccess()),
                    () => new CatalogTasksViewModel(workspace, new WindowsScheduledTaskAccess(commands, systemDirectory)),
                    () => new ProcessesViewModel(new ProcessOverview(ProcessSampler ?? new WindowsProcessSampler()), localizer));
            }),
            new("nav.gaming", SymbolRegular.Games24, () => new GamingPageViewModel(
                workspace,
                () => new GameProfilesViewModel(
                    GameScanSource ?? new GameLibrary(new WindowsGameFileSystem(), WindowsGameFileSystem.EpicManifestFolder, WindowsGameFileSystem.SteamRoot()).Scan,
                    GameProfiles ?? new GameProfileStore(GameProfileStore.DefaultPath),
                    workspace,
                    new GameLauncher(),
                    new WindowsFilePicker()),
                () => new AntiCheatViewModel(SecuritySource ?? new WindowsSecurityFacts(), localizer),
                () => GamingChecks(localizer),
                jump => GamingNavigator?.Invoke(jump))),
            new("nav.privacy", SymbolRegular.ShieldLock24, () =>
            {
                var source = PermissionSource ?? new PermissionOverview(new WindowsRegistryAccess(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Read;
                var permissions = new PermissionOverviewViewModel(source, localizer, OpenUri);
                _ = permissions.LoadAsync();
                return new TweakListPageViewModel("nav.privacy", workspace, permissions, "PRIV");
            }),
            new("nav.cleanup", SymbolRegular.Broom24, () => new CleanupPageViewModel(CleanupSource ?? new TweakServiceCleanup(workspace.Service), dialogs, localizer, workspace.IsReadOnly)),
            new("nav.debloat", SymbolRegular.Apps24, () => new DebloatPageViewModel(
                workspace,
                () => new AppsViewModel(AppManagerFor(workspace), dialogs, localizer, Environment.OSVersion.Version.Build, workspace.IsReadOnly, OpenUri),
                () => new OneDriveViewModel(
                    OneDriveCheckSource ?? new OneDriveCheck(new WindowsOneDriveEnvironment(new WindowsSignatureVerifier())).Check,
                    () => DesignPreview && OneDriveCheckSource is { } sample
                        ? sample()
                        : new OneDriveUninstaller(new OneDriveCheck(new WindowsOneDriveEnvironment(new WindowsSignatureVerifier())), new ProcessCommandRunner()).Uninstall(),
                    dialogs,
                    localizer,
                    workspace.IsReadOnly))),
            new("nav.network", SymbolRegular.Globe24, () => new SectionPageViewModel("nav.network",
            [
                (new SectionItem("tweaks", "perf.section.tweaks"), () => new TweakListPageViewModel("nav.network", workspace, "NET")),
                (new SectionItem("dns", "net.section.dns"), () => new CustomDnsViewModel(workspace, dialogs)),
                (new SectionItem("tools", "net.section.tools"), () =>
                {
                    var tools = new NetworkToolsViewModel(NetworkToolsSource ?? new NetworkTools(workspace.Service, new WindowsNetworkAccess(new ProcessCommandRunner(), Environment.SystemDirectory), new ProcessCommandRunner(), OpenUri), dialogs, localizer);
                    _ = tools.LoadAsync();
                    return tools;
                }),
            ])),
            new("nav.disk", SymbolRegular.Storage24, () => new SectionPageViewModel("nav.disk",
            [
                (new SectionItem("tweaks", "perf.section.tweaks"), () => new TweakListPageViewModel("nav.disk", workspace, "DISK")),
                (new SectionItem("drives", "disk.section.drives"), () =>
                {
                    var drives = new DrivesViewModel(DiskToolsSource ?? new DiskTools(workspace.Service), dialogs, localizer, workspace.IsReadOnly);
                    _ = drives.LoadAsync();
                    return drives;
                }),
                (new SectionItem("large", "disk.section.large"), () => new LargeFilesViewModel(DiskToolsSource ?? new DiskTools(workspace.Service), localizer, dialogs, new KeepListStore(KeepListPath ?? KeepListStore.DefaultPath), OpenUri, expert: true)),
            ])),
            new("nav.updates", SymbolRegular.ArrowSync24, () => new SectionPageViewModel("nav.updates",
            [
                (new SectionItem("overview", "updates.section.overview"), () =>
                {
                    var overview = new UpdatesOverviewViewModel(workspace, UpdateStatusSource ?? new WindowsUpdateStatusSource(Environment.OSVersion.Version.Build), OpenUri);
                    _ = overview.LoadAsync();
                    return overview;
                }),
                (new SectionItem("tweaks", "updates.section.policies"), () => new TweakListPageViewModel("nav.updates", workspace, "UPD")),
            ])),
            new("nav.profiles", SymbolRegular.DocumentBulletList24, () =>
            {
                var catalog = workspace.Catalog;
                var commands = new ProcessCommandRunner();
                return new ProfilesPageViewModel(
                    workspace,
                    catalog,
                    AscendedTweaks.Catalog.ProfileLoader.LoadBuiltIn(catalog),
                    new ProfileStore(ProfileDirectory ?? ProfileStore.DefaultDirectory, catalog),
                    new WindowsProfileFiles(),
                    ProfileMetricsSource ?? new WindowsProfileMetrics(AutostartSource ?? new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(commands, Environment.SystemDirectory), new WindowsSignatureVerifier()).Read),
                    dialogs,
                    HasBatterySource ?? BatteryInfo.HasBattery());
            }),
            new("nav.restore", SymbolRegular.History24, () =>
            {
                var page = new RestorePageViewModel(workspace, dialogs, AppManagerFor(workspace));
                _ = page.LoadAsync();
                return page;
            }),
            new("nav.settings", SymbolRegular.Settings24, () =>
            {
                var preferences = new SettingsViewModel(localizer, store, settings, expert => ExpertModeChanged?.Invoke(expert))
                {
                    KeepList = new KeepListViewModel(new KeepListStore(KeepListPath ?? KeepListStore.DefaultPath)),
                };
                SettingsPages.Add(preferences);
                var version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "0.0.0";
                preferences.Maintenance = new SettingsMaintenanceViewModel(
                    LogFolderSource ?? new UserLogFolder(AscendedTweaks.Engine.Logging.JsonLinesLog.DefaultUserDirectory),
                    UpdateServiceSource ?? new AppUpdateService(version),
                    dialogs,
                    localizer,
                    preferences.ResetToDefaults)
                {
                    Version = version,
                    OpenDocument = OpenDocument,
                };
                return preferences;
            }),
        };
#if DEBUG
        items.Add(new("nav.catalog", SymbolRegular.DesignIdeas24, () => new DesignCatalogViewModel(localizer)));
#endif
        return items;
    }

    /// <summary>Alle erzeugten Einstellungs-Seiten, damit der Modus-Schalter im Kopf und der Schalter dort gleich stehen.</summary>
    private static readonly List<SettingsViewModel> SettingsPages = [];

    /// <summary>V2: Einfach-Modus mit 5 Seiten (Abschnitt 2.2). Detailseiten bleiben im Experten-Modus erhalten.</summary>
    internal static (IReadOnlyList<NavItem> Main, IReadOnlyList<NavItem> Footer) CreateSimpleNavItems(
        Localizer localizer,
        TweakWorkspace workspace,
        IDialogService dialogs,
        IReadOnlyList<NavItem> expertItems)
    {
        Func<IReadOnlyList<AutostartEntry>> Autostart()
        {
            var commands = new ProcessCommandRunner();
            return AutostartSource ?? new AutostartInventory(new WindowsRegistryAccess(), new WindowsAutostartSources(commands, Environment.SystemDirectory), new WindowsSignatureVerifier()).Read;
        }

        var main = new List<NavItem>
        {
            new("nav.simple.start", SymbolRegular.Home24, () =>
            {
                var catalog = workspace.Catalog;
                var start = new SimpleStartViewModel(
                    workspace,
                    SystemInfoSource ?? new WindowsSystemInfo(new ProcessCommandRunner(), Environment.SystemDirectory),
                    Autostart(),
                    AscendedTweaks.Catalog.ProfileLoader.LoadBuiltIn(catalog),
                    HasBatterySource ?? BatteryInfo.HasBattery(),
                    dialogs,
                    new PresetOwnershipStore(PresetOwnershipPath ?? PresetOwnershipStore.DefaultPath),
                    new ProfileStore(ProfileDirectory ?? ProfileStore.DefaultDirectory, catalog),
                    CleanupSource ?? new TweakServiceCleanup(workspace.Service),
                    new MeasurementViewModel(
                        ProfileMetricsSource ?? new WindowsProfileMetrics(Autostart(), () => (SystemInfoSource ?? new WindowsSystemInfo(new ProcessCommandRunner(), Environment.SystemDirectory)).Read().Boot),
                        new MeasurementStore(MeasurementPath ?? MeasurementStore.DefaultPath),
                        MeasurementViewModel.LastBootFromUptime,
                        localizer,
                        TimeProvider.System));
                start.OpenAutostart = () => GamingNavigator?.Invoke(GamingJump.Autostart);
                FirstPageLoad ??= start.LoadAsync();
                return start;
            }),
            new("nav.simple.faster", SymbolRegular.TopSpeed24, () =>
            {
                var tools = new NetworkToolsViewModel(NetworkToolsSource ?? new NetworkTools(workspace.Service, new WindowsNetworkAccess(new ProcessCommandRunner(), Environment.SystemDirectory), new ProcessCommandRunner(), OpenUri), dialogs, localizer);
                return new SimpleTweakPageViewModel(
                    "nav.simple.faster",
                    workspace,
                    c => c.Tweak.Module is "PERF" or "DISK" or "BROW" || c.Tweak.Category == "Network.Dns",
                    "simple.faster.help",
                    [new SimpleAutostartCard(new AutostartViewModel(Autostart(), workspace, ProcessSampler ?? new WindowsProcessSampler())), BackgroundCard(dialogs, localizer), new SimplePingCard(tools)]);
            }),
            new("nav.simple.gaming", SymbolRegular.Games24, () => new SimpleTweakPageViewModel("nav.simple.gaming", workspace, c => c.Tweak.Module is "GAME" or "POWR" or "DEFN" || c.Tweak.Category == "Network.Adapter", "simple.gaming.help", [Loaded(GamingChecks(localizer)), BackgroundCard(dialogs, localizer)])),
            new("nav.simple.cleanup", SymbolRegular.Broom24, () => new SectionPageViewModel("nav.simple.cleanup",
            [
                (new SectionItem("cleanup", "simple.cleanup.section.junk"), () =>
                {
                    // Einfach-Modus: Der Scan startet beim Oeffnen (nur lesen), damit Aufraeumen in drei Klicks geht.
                    var junk = new CleanupPageViewModel(CleanupSource ?? new TweakServiceCleanup(workspace.Service), dialogs, localizer, workspace.IsReadOnly);
                    _ = junk.ScanAsync();
                    return new SimpleCleanupView(junk);
                }),
                (new SectionItem("large", "simple.cleanup.section.large"), () => new LargeFilesViewModel(DiskToolsSource ?? new DiskTools(workspace.Service), localizer, dialogs, new KeepListStore(KeepListPath ?? KeepListStore.DefaultPath), OpenUri)),
                (new SectionItem("apps", "simple.cleanup.section.apps"), () => new AppsViewModel(AppManagerFor(workspace), dialogs, localizer, Environment.OSVersion.Version.Build, workspace.IsReadOnly, OpenUri)),
            ])),
            new("nav.simple.privacy", SymbolRegular.ShieldLock24, () =>
            {
                var updates = new UpdatesOverviewViewModel(workspace, UpdateStatusSource ?? new WindowsUpdateStatusSource(Environment.OSVersion.Version.Build), OpenUri);
                _ = updates.LoadAsync();
                return new SimpleTweakPageViewModel(
                    "nav.simple.privacy",
                    workspace,
                    c => c.Tweak.Module is "PRIV" or "DEB" or "UI",
                    "simple.privacy.help",
                    [new SimpleUpdatesCard(updates)]);
            }),
        };
        var footer = new List<NavItem>
        {
            new("nav.simple.back", SymbolRegular.ArrowUndo24, () =>
            {
                var back = new SimpleBackViewModel(workspace, dialogs);
                _ = back.LoadAsync();
                return back;
            }),
            new("nav.settings", SymbolRegular.Settings24, expertItems.First(i => i.Key == "nav.settings").Create),
        };
        return (main, footer);
    }

    /// <summary>Loading of the page shown first; the main window waits for it behind the loading window.</summary>
    private static Task? FirstPageLoad { get; set; }

    /// <summary>Programs of the user's session, for "Clean up background" (tests and snapshots replace the source).</summary>
    internal static IRunningPrograms? RunningProgramsSource { get; set; }

    private static SimpleBackgroundCard BackgroundCard(IDialogService dialogs, Localizer localizer)
    {
        var cleanup = new BackgroundCleanupViewModel(
            RunningProgramsSource ?? new WindowsRunningPrograms(),
            dialogs,
            localizer,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.ProcessId);
        _ = cleanup.LoadAsync();
        return new SimpleBackgroundCard(cleanup);
    }

    private static GamingChecksViewModel Loaded(GamingChecksViewModel checks)
    {
        _ = checks.LoadAsync();
        return checks;
    }

    /// <summary>Sprungziel je Modus: Einfach "Aufraeumen"/"Schneller" (Autostart-Karte), Experte "Bereinigung"/"Leistung" mit Autostart-Bereich.</summary>
    private static void NavigateTo(ShellViewModel shell, GamingJump jump)
    {
        var key = (jump, shell.IsExpertMode) switch
        {
            (GamingJump.Cleanup, false) => "nav.simple.cleanup",
            (GamingJump.Cleanup, true) => "nav.cleanup",
            (GamingJump.Autostart, false) => "nav.simple.faster",
            (GamingJump.Autostart, true) => "nav.performance",
            _ => null,
        };
        if (key is null || shell.Items.FirstOrDefault(i => i.Key == key) is not { } item)
        {
            return;
        }

        shell.SelectedItem = item;
        if (jump == GamingJump.Autostart && shell.CurrentPage is PerformancePageViewModel performance)
        {
            performance.SelectedSection = performance.Sections.First(s => s.Key == "autostart");
        }
    }

    /// <summary>Numbers and dates follow the app language (32.0 GB in English, 32,0 GB in German), not the Windows region.</summary>
    private static void UseFormatsOf(string culture)
    {
        var info = System.Globalization.CultureInfo.GetCultureInfo(culture);
        System.Globalization.CultureInfo.CurrentCulture = info;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = info;
    }

    private static void OpenUri(string uri) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        var marker = Path.Combine(AppContext.BaseDirectory, DesignPreviewMarker);
        DesignPreview = e.Args.Contains("--design-preview") || File.Exists(marker);
#endif
        var previewRoot = Path.Combine(Path.GetTempPath(), "AscendedTweaks.DesignPreview");
        _log = new JsonLinesLog(DesignPreview ? Path.Combine(previewRoot, "Logs") : JsonLinesLog.DefaultUserDirectory, "app", TimeProvider.System);
        DispatcherUnhandledException += OnUnhandledException;

        var store = new SettingsStore(DesignPreview ? Path.Combine(previewRoot, "settings.json") : SettingsStore.DefaultPath);
        var settings = store.Load();
        if (DesignPreview)
        {
            // Same language as the real app, but its own settings: nothing of the real app is touched.
            settings.Language = new SettingsStore(SettingsStore.DefaultPath).Load().Language;
            settings.FirstStepsCompleted = true;
            settings.PortableChosen = true;
            ThemeService.TokenFile = PreviewTokens(Path.Combine(AppContext.BaseDirectory, DesignPreviewMarker));
        }

#if DEBUG
        // Screenshots in a chosen language without touching the saved setting: --lang en|de.
        var langArgument = Array.IndexOf(e.Args, "--lang");
        if (langArgument >= 0 && langArgument + 1 < e.Args.Length)
        {
            settings.Language = e.Args[langArgument + 1] == "de" ? Localizer.German : Localizer.English;
        }
#endif

        Localizer.Current = new Localizer(settings.Language);
        UseFormatsOf(Localizer.Current.Culture);
        Localizer.Current.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(Localizer.Culture))
            {
                UseFormatsOf(Localizer.Current.Culture);
            }
        };
        var theme = new ThemeService();
        theme.Apply();
        theme.WatchHighContrast();

        if (!e.Args.Contains("--snapshot") && !DesignPreview)
        {
            // Waehrend der Installationsfrage darf das Schliessen des kleinen Fensters die App nicht beenden.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "0.0.0";
            if (InstallFlow.Handle(e.Args, settings, store, version.Split('+')[0]))
            {
                Shutdown();
                return;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        // Short loading window until the first page is ready, instead of a window that fills in slowly.
        var splash = e.Args.Contains("--snapshot") ? null : StartupSplash.Show(ProductInfo.Name, Localizer.Current["splash.loading"]);

        var environment = new WindowsSystemEnvironment();
        var embedded = CatalogLoader.LoadEmbedded();
        var catalog = new CompositeCatalog(embedded);
        _service = new TweakService(
            catalog,
            EngineFactory.CreateForWindows(catalog, DesignPreview ? Path.Combine(previewRoot, "State", "User") : StateStore.DefaultUserRoot, environment, _log),
            EngineFactory.CreateForWindows(catalog, DesignPreview ? Path.Combine(previewRoot, "State", "Machine") : StateStore.DefaultMachineRoot, environment, _log),
            EngineFactory.CreateForWindows(catalog, DesignPreview ? Path.Combine(previewRoot, "State", "Policy") : StateStore.UserPolicyRoot(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value), environment, _log),
            () => new HelperClient(new ElevatedHelperLauncher()),
            TimeProvider.System);
        _service.AttachCleanup(AscendedTweaks.Engine.Cleanup.CleanupFactory.User(environment), AscendedTweaks.Engine.Cleanup.CleanupFactory.MachineEstimator(environment));
        var dialogs = new OverlayDialogService();
        var readOnly = SupportPolicy.Classify(environment.Build) == BuildSupport.Unsupported;
        var workspace = new TweakWorkspace(embedded, _service, Localizer.Current, settings, dialogs, new WpfUiInvoker(), new ExplorerRestarter(), readOnly);
        workspace.OneClick = true;
        workspace.DesignPreview = DesignPreview;
#if DEBUG
        if (e.Args.Contains("--snapshot") || DesignPreview)
        {
            PermissionSource = SnapshotRenderer.SamplePermissions;
            AutostartSource = SnapshotRenderer.SampleAutostart;
            ProcessSampler = new SnapshotRenderer.SampleProcesses();
            RunningProgramsSource = new SnapshotRenderer.SamplePrograms();
            GameScanSource = SnapshotRenderer.SampleGames;
            GameProfiles = new GameProfileStore(Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.games." + Guid.NewGuid().ToString("N") + ".json"));
            SecuritySource = new SnapshotRenderer.SampleSecurity();
            CleanupSource = new SnapshotRenderer.SampleCleanup();
            AppSource = new SnapshotRenderer.SampleApps();
            NetworkToolsSource = new SnapshotRenderer.SampleNetwork();
            DiskToolsSource = new SnapshotRenderer.SampleDisks();
            UpdateStatusSource = new SnapshotRenderer.SampleUpdates();
            SystemInfoSource = new SnapshotRenderer.SampleSystem();
            LogFolderSource = new SnapshotRenderer.SampleLogs();
            UpdateServiceSource = new SnapshotRenderer.SampleUpdate();
            ProfileMetricsSource = new SnapshotRenderer.SampleMetrics();
            ProfileDirectory = Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.profiles." + Guid.NewGuid().ToString("N"));
            PresetOwnershipPath = Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.presets." + Guid.NewGuid().ToString("N") + ".json");
            HasBatterySource = false;
            HardwareSource = new SnapshotRenderer.SampleHardware();
            KeepListPath = Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.keep." + Guid.NewGuid().ToString("N") + ".json");
            MeasurementPath = Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.measure." + Guid.NewGuid().ToString("N") + ".json");
            OneDriveCheckSource = () => new OneDriveStatus(true, @"C:\Users\Example\AppData\Local\Microsoft\OneDrive\25.1\OneDriveSetup.exe", [OneDriveBlock.KnownFolderMove], ["Desktop", "Personal"], 0);
        }
#endif
        var expertItems = CreateNavItems(Localizer.Current, store, settings, workspace, dialogs);
        var (simpleItems, footerItems) = CreateSimpleNavItems(Localizer.Current, workspace, dialogs, expertItems);
        var shell = new ShellViewModel(Localizer.Current, environment.Build, expertItems, workspace, dialogs, simpleItems, footerItems, settings.ExpertMode);
        GamingNavigator = jump => NavigateTo(shell, jump);
        GamingWorkspace = workspace;

        // Ein Modus fuer alles: Kopf-Schalter und Einstellung stehen immer gleich, versteckte Tweaks folgen dem Modus.
        shell.ModeChanged = expert =>
        {
            settings.ExpertMode = expert;
            store.Save(settings);
            workspace.SetExpertMode(expert);
            foreach (var page in SettingsPages)
            {
                page.SyncExpertMode(expert);
            }
        };
        ExpertModeChanged = expert =>
        {
            workspace.SetExpertMode(expert);
            shell.ApplyMode(expert);
        };
        var window = new ShellWindow { DataContext = shell };
        MainWindow = window;

#if DEBUG
        var snapshot = Array.IndexOf(e.Args, "--snapshot");
        if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
        {
            await workspace.RefreshAsync().ConfigureAwait(true);
            var sample = new FirstStepsViewModel(workspace, new AppSettings(), new SettingsStore(Path.Combine(Path.GetTempPath(), "AscendedTweaks.snapshot.settings.json")), new SettingsViewModel(Localizer.Current, store, settings));
            SnapshotRenderer.Run(window, shell, theme, e.Args[snapshot + 1], sample);
            Shutdown();
            return;
        }
#endif

        try
        {
            await Task.WhenAny(Task.WhenAll(workspace.RefreshAsync(), FirstPageLoad ?? Task.CompletedTask), Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(true);
        }
        finally
        {
            window.Show();
            window.Activate();
            splash?.Close();
        }

        if (DesignPreview)
        {
            await dialogs.ShowAsync(new ResultDialogViewModel(Localizer.Current["preview.title"], Localizer.Current["preview.body"], [], false)).ConfigureAwait(true);
            return;
        }

        if (!settings.FirstStepsCompleted)
        {
            var preferences = new SettingsViewModel(Localizer.Current, store, settings);
            await dialogs.ShowAsync(new FirstStepsViewModel(workspace, settings, store, preferences)).ConfigureAwait(true);
        }

        // Absturzsicherheit (Abschnitt 5.4, Punkt 8): offene Aenderung beim Start zuruecknehmen anbieten.
        if (_service.GetInterruptedBatches().Count > 0
            && await dialogs.ShowAsync(new ConfirmDialogViewModel(
                Localizer.Current["restore.interrupted"],
                Localizer.Current["restore.interrupted.body"],
                Localizer.Current["restore.recover"],
                Localizer.Current["dialog.later"])).ConfigureAwait(true))
        {
            foreach (var (scope, batch) in _service.GetInterruptedBatches())
            {
                await Task.Run(() => _service.RecoverInterrupted(scope, batch.BatchId)).ConfigureAwait(true);
            }

            await workspace.RefreshAsync().ConfigureAwait(true);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Beendet einen laufenden Helfer sofort (er endet sonst nach 120 s Leerlauf oder mit diesem Prozess).
        _service?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Globale Fehlerbehandlung (Abschnitt 10.2): kein Stacktrace in der Oberflaeche, nichts wird gesendet.
        _log?.Write(new LogRecord { Event = "UnhandledException", Level = Core.LogLevel.Error, Error = e.Exception.ToString() });
        System.Windows.MessageBox.Show(
            Localizer.Current.Format("error.general", ("path", _log?.CurrentFile ?? string.Empty)),
            ProductInfo.Name,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
