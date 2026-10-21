namespace TweakPilot.Core;

/// <summary>Basisordner einer Bereinigungs-Wurzel. Benutzerordner werden nur im Benutzerprozess aufgeloest, nie im Helfer.</summary>
public enum CleanupBase
{
    UserTemp,
    LocalAppData,
    LocalAppDataLow,
    Windows,
    ProgramData,
    SystemDrive,

    /// <summary>%APPDATA% (Roaming), zum Beispiel Discord.</summary>
    RoamingAppData,
}

public enum CleanupMode
{
    /// <summary>In den Papierkorb (wo moeglich, mit Rueckgaengig-Option des Explorers).</summary>
    RecycleBin,

    /// <summary>Endgueltig loeschen.</summary>
    Permanent,
}

/// <summary>Sonderablauf statt Dateiloeschung durch die App.</summary>
public enum CleanupHandler
{
    Files,

    /// <summary>CLEAN-003: Dienste wuauserv und bits kurz stoppen, danach exakt wiederherstellen.</summary>
    WindowsUpdateDownload,

    /// <summary>CLEAN-005: offizieller Datentraegerbereinigungs-Handler "Vorherige Windows-Installation(en)".</summary>
    PreviousInstallations,

    /// <summary>CLEAN-006: SHEmptyRecycleBin.</summary>
    RecycleBin,

    /// <summary>CLEAN-014: DISM /StartComponentCleanup.</summary>
    ComponentStore,
}

/// <summary>
/// Eine Wurzel: Basisordner plus relativer Pfad. Ein Segment "*" steht fuer jedes direkte Unterverzeichnis
/// (zum Beispiel Browser-Profile); Abzweigungen (Reparse Points) werden dabei nie verfolgt.
/// </summary>
public sealed record CleanupRoot(CleanupBase Base, string RelativePath, string Pattern = "*", bool Recursive = true, bool SingleFile = false);

public sealed record CleanupCategory
{
    public required string Id { get; init; }

    public required TweakScope Scope { get; init; }

    public required RiskLevel Risk { get; init; }

    public CleanupHandler Handler { get; init; } = CleanupHandler.Files;

    public IReadOnlyList<CleanupRoot> Roots { get; init; } = [];

    /// <summary>Nur Dateien, die mindestens so lange nicht geaendert wurden.</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.Zero;

    /// <summary>Papierkorb moeglich. Bei Systemordnern im Helfer: false, dort steht deutlich "endgueltig".</summary>
    public bool SupportsRecycleBin { get; init; }

    /// <summary>Prozessnamen, die geschlossen sein muessen (Browser).</summary>
    public IReadOnlyList<string> RequiresClosed { get; init; } = [];

    /// <summary>Leere Unterordner (nicht die Wurzel) nach dem Loeschen entfernen.</summary>
    public bool RemoveEmptyFolders { get; init; }

    /// <summary>Nach dem Scan vorausgewaehlt. Nie fuer den Papierkorb und nie fuer Kategorien mit Risiko "Vorsicht".</summary>
    public bool SelectedByDefault => Risk == RiskLevel.Safe && Handler != CleanupHandler.RecycleBin;
}

/// <summary>Fest eingebettete Kategorien CLEAN-001 bis CLEAN-014 (Abschnitt 6.5) und Launcher-Caches CLEAN-015 bis CLEAN-020 (V2 Abschnitt 5.2).</summary>
public static class CleanupCategories
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private static readonly string[] BrowserCaches = ["Cache", "Code Cache", "GPUCache"];

    public static readonly IReadOnlyList<CleanupCategory> All =
    [
        new() { Id = "CLEAN-001", Scope = TweakScope.User, Risk = RiskLevel.Safe, Roots = [new(CleanupBase.UserTemp, string.Empty)], MinimumAge = OneDay, SupportsRecycleBin = true, RemoveEmptyFolders = true },
        new() { Id = "CLEAN-002", Scope = TweakScope.Machine, Risk = RiskLevel.Safe, Roots = [new(CleanupBase.Windows, "Temp")], MinimumAge = OneDay, RemoveEmptyFolders = true },
        new() { Id = "CLEAN-003", Scope = TweakScope.Machine, Risk = RiskLevel.Safe, Handler = CleanupHandler.WindowsUpdateDownload, Roots = [new(CleanupBase.Windows, @"SoftwareDistribution\Download")], RemoveEmptyFolders = true },
        new() { Id = "CLEAN-004", Scope = TweakScope.Machine, Risk = RiskLevel.Safe, Roots = [new(CleanupBase.Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")], RemoveEmptyFolders = true },
        new() { Id = "CLEAN-005", Scope = TweakScope.Machine, Risk = RiskLevel.Caution, Handler = CleanupHandler.PreviousInstallations, Roots = [new(CleanupBase.SystemDrive, "Windows.old")] },
        new() { Id = "CLEAN-006", Scope = TweakScope.User, Risk = RiskLevel.Safe, Handler = CleanupHandler.RecycleBin },
        new() { Id = "CLEAN-007", Scope = TweakScope.User, Risk = RiskLevel.Safe, Roots = [new(CleanupBase.LocalAppData, @"Microsoft\Windows\Explorer", "thumbcache_*.db", Recursive: false)], SupportsRecycleBin = true },
        new()
        {
            Id = "CLEAN-008", Scope = TweakScope.Machine, Risk = RiskLevel.Safe, RemoveEmptyFolders = true,
            Roots = [new(CleanupBase.ProgramData, @"Microsoft\Windows\WER\ReportArchive"), new(CleanupBase.ProgramData, @"Microsoft\Windows\WER\ReportQueue")],
        },
        new()
        {
            Id = "CLEAN-009", Scope = TweakScope.Machine, Risk = RiskLevel.Caution, RemoveEmptyFolders = true,
            Roots = [new(CleanupBase.Windows, "Minidump", "*.dmp"), new(CleanupBase.Windows, "MEMORY.DMP", SingleFile: true), new(CleanupBase.Windows, "LiveKernelReports", "*.dmp")],
        },
        Browser("CLEAN-010", @"Google\Chrome\User Data", "chrome"),
        Browser("CLEAN-011", @"Microsoft\Edge\User Data", "msedge"),
        new()
        {
            Id = "CLEAN-012", Scope = TweakScope.User, Risk = RiskLevel.Safe, SupportsRecycleBin = true, RequiresClosed = ["firefox"], RemoveEmptyFolders = true,
            Roots = [new(CleanupBase.LocalAppData, @"Mozilla\Firefox\Profiles\*\cache2")],
        },
        new()
        {
            Id = "CLEAN-013", Scope = TweakScope.User, Risk = RiskLevel.Caution, SupportsRecycleBin = true, RemoveEmptyFolders = true,
            Roots =
            [
                new(CleanupBase.LocalAppData, "D3DSCache"),
                new(CleanupBase.LocalAppData, @"NVIDIA\DXCache"),
                new(CleanupBase.LocalAppData, @"NVIDIA\GLCache"),
                new(CleanupBase.LocalAppData, @"AMD\DxCache"),
                new(CleanupBase.LocalAppDataLow, @"Intel\ShaderCache"),
            ],
        },
        new() { Id = "CLEAN-014", Scope = TweakScope.Machine, Risk = RiskLevel.Caution, Handler = CleanupHandler.ComponentStore },

        // Launcher-Caches: nur die Cache-Unterordner, nie Local Storage, Cookies, Einstellungen, Spielstaende oder Installationen.
        Launcher("CLEAN-015", CleanupBase.RoamingAppData, "discord", "discord"),
        new()
        {
            Id = "CLEAN-016", Scope = TweakScope.User, Risk = RiskLevel.Caution, SupportsRecycleBin = true, RequiresClosed = ["spotify"], RemoveEmptyFolders = true,
            Roots = [new(CleanupBase.LocalAppData, @"Spotify\Storage")],
        },
        Launcher("CLEAN-017", CleanupBase.LocalAppData, @"EpicGamesLauncher\Saved\webcache_*", "epicgameslauncher"),
        Launcher("CLEAN-018", CleanupBase.LocalAppData, @"Steam\htmlcache\Default", "steam", "steamwebhelper"),
        Launcher("CLEAN-019", CleanupBase.RoamingAppData, @"Microsoft\Teams", "teams"),
        new()
        {
            Id = "CLEAN-020", Scope = TweakScope.User, Risk = RiskLevel.Caution, SupportsRecycleBin = true, MinimumAge = OneDay,
            Roots = [new(CleanupBase.LocalAppData, "CrashDumps", "*.dmp", Recursive: false)],
        },
    ];

    public static bool TryGet(string id, out CleanupCategory category)
    {
        category = All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal))!;
        return category is not null;
    }

    private static CleanupCategory Launcher(string id, CleanupBase root, string folder, params string[] processes) => new()
    {
        Id = id,
        Scope = TweakScope.User,
        Risk = RiskLevel.Safe,
        SupportsRecycleBin = true,
        RequiresClosed = processes,
        RemoveEmptyFolders = true,
        Roots = BrowserCaches.Select(c => new CleanupRoot(root, $@"{folder}\{c}")).ToList(),
    };

    private static CleanupCategory Browser(string id, string userData, string process) => new()
    {
        Id = id,
        Scope = TweakScope.User,
        Risk = RiskLevel.Safe,
        SupportsRecycleBin = true,
        RequiresClosed = [process],
        RemoveEmptyFolders = true,
        Roots = BrowserCaches.Select(c => new CleanupRoot(CleanupBase.LocalAppData, $@"{userData}\*\{c}")).ToList(),
    };
}

/// <summary>Ergebnis eines Scans; nur lesend ermittelt.</summary>
public sealed record CleanupScan
{
    public required string CategoryId { get; init; }

    public long Bytes { get; init; }

    public int Files { get; init; }

    /// <summary>Groesse unbekannt (zum Beispiel Komponentenspeicher: ermittelt nur DISM selbst).</summary>
    public bool SizeUnknown { get; init; }

    public int SkippedReparsePoints { get; init; }

    /// <summary>Grund, warum die Kategorie gerade nicht bereinigt werden kann (zum Beispiel Browser geoeffnet).</summary>
    public string? Blocked { get; init; }

    /// <summary>Systemordner ohne Administratorrechte gelesen: unzugaengliche Teile fehlen in der Summe.</summary>
    public bool Estimated { get; init; }

    public bool NothingToDo => !SizeUnknown && Files == 0 && Bytes == 0;
}

public sealed record CleanupResult
{
    public required string CategoryId { get; init; }

    public required CleanupMode Mode { get; init; }

    public int DeletedFiles { get; init; }

    public long DeletedBytes { get; init; }

    public int SkippedInUse { get; init; }

    public int SkippedReparsePoints { get; init; }

    public int Failed { get; init; }

    public string? Blocked { get; init; }

    public string? Message { get; init; }
}
