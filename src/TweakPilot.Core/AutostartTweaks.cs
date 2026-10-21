
namespace TweakPilot.Core;

/// <summary>Orte, deren Autostart-Eintraege Windows ueber "StartupApproved" freigibt (wie der Task-Manager).</summary>
public enum AutostartLocation
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserStartupFolder,
    CommonStartupFolder,
}

/// <summary>
/// PERF-026 (Abschnitt 6.2): Autostart-Eintrag deaktivieren, generisch pro Eintrag. Der Tweak wird aus der ID erzeugt
/// ("PERF-026/&lt;Ort&gt;/&lt;Name&gt;"), damit Ledger, Sicherung und "Alles rueckgaengig" auch nach einem Neustart
/// ohne gespeicherte Zusatzdaten funktionieren. Es wird nur der Freigabewert geschrieben, der Eintrag selbst bleibt.
/// </summary>
public static class AutostartTweaks
{
    public const string BaseId = "PERF-026";

    public const int MaxNameLength = 260;

    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string MachineApproved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>Deaktiviert: Byte 0 = 03, danach ein Zeitstempel. Die App schreibt Nullen, damit die Erkennung eindeutig ist.</summary>
    public static readonly byte[] DisabledValue = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static string Code(AutostartLocation location) => location switch
    {
        AutostartLocation.UserRun => "HKCU-RUN",
        AutostartLocation.MachineRun => "HKLM-RUN",
        AutostartLocation.MachineRun32 => "HKLM-RUN32",
        AutostartLocation.UserStartupFolder => "USER-STARTUP",
        _ => "COMMON-STARTUP",
    };

    public static (RegHive Hive, string Path) ApprovalKey(AutostartLocation location) => location switch
    {
        AutostartLocation.UserRun => (RegHive.HKCU, Approved + @"\Run"),
        AutostartLocation.MachineRun => (RegHive.HKLM, MachineApproved + @"\Run"),
        AutostartLocation.MachineRun32 => (RegHive.HKLM, MachineApproved + @"\Run32"),
        AutostartLocation.UserStartupFolder => (RegHive.HKCU, Approved + @"\StartupFolder"),
        _ => (RegHive.HKLM, MachineApproved + @"\StartupFolder"),
    };

    public static TweakScope ScopeOf(AutostartLocation location) =>
        location is AutostartLocation.UserRun or AutostartLocation.UserStartupFolder ? TweakScope.User : TweakScope.Machine;

    public static bool IsValidName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length is > 0 and <= MaxNameLength && !name.Any(char.IsControl);
    }

    public static string IdFor(AutostartLocation location, string name)
    {
        if (!IsValidName(name))
        {
            throw new TweakPilotException(ErrorKind.CatalogInvalid, "Invalid name of a startup entry.");
        }

        return $"{BaseId}/{Code(location)}/{name}";
    }

    public static bool IsAutostartId(string id) => id.StartsWith(BaseId + "/", StringComparison.Ordinal);

    public static bool TryParse(string id, out AutostartLocation location, out string name)
    {
        location = default;
        name = string.Empty;
        if (!IsAutostartId(id))
        {
            return false;
        }

        var rest = id[(BaseId.Length + 1)..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        var code = rest[..slash];
        name = rest[(slash + 1)..];
        foreach (var candidate in Enum.GetValues<AutostartLocation>())
        {
            if (Code(candidate) == code)
            {
                location = candidate;
                return IsValidName(name);
            }
        }

        return false;
    }

    public static bool TryCreate(string id, out TweakDefinition tweak)
    {
        tweak = null!;
        if (!TryParse(id, out var location, out var name))
        {
            return false;
        }

        var (hive, path) = ApprovalKey(location);
        var value = new RegLiteral { Text = Convert.ToBase64String(DisabledValue) };
        var scope = ScopeOf(location);
        tweak = new TweakDefinition
        {
            Id = id,
            Category = "Performance.Autostart",
            Impact = ImpactLevel.Small,
            ImpactNoteKey = $"tweak.{BaseId}.impact",
            PlainTitleKey = $"tweak.{BaseId}.plain.title",
            PlainDescKey = $"tweak.{BaseId}.plain.desc",
            NameKey = $"tweak.{BaseId}.name",
            DescriptionKey = $"tweak.{BaseId}.desc",
            SideEffectsKey = $"tweak.{BaseId}.side",
            Risk = RiskLevel.Safe,
            Evidence = EvidenceLevel.Proven,
            Builds = [new BuildRange { Min = 19045 }],
            Editions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
            Scope = scope,
            RequiresAdmin = scope != TweakScope.User,
            RequiresReboot = false,
            Detect = new RegistryValueEqualsCondition { Hive = hive, Path = path, Name = name, Value = value },
            Apply = [new RegistrySetValueAction { Hive = hive, Path = path, Name = name, Kind = RegValueKind.Binary, Value = value }],
            Revert = RevertSpec.RestoreFromBackup,
            Sources =
            [
                "Same mechanism as Task Manager (Startup apps tab): approval value under Explorer\\StartupApproved, byte 0 = 02 enabled, 03 disabled. Not documented by Microsoft; the entry and the program stay unchanged, undone through the backed-up original value.",
            ],
        };
        return true;
    }
}

/// <summary>Eingebetteter Katalog plus die aus der ID erzeugten Tweaks (PERF-026 Autostart, GAME-007/008/016 pro Spiel).</summary>
public sealed class CompositeCatalog : ITweakCatalog
{
    private readonly ITweakCatalog _inner;

    public CompositeCatalog(ITweakCatalog inner) => _inner = inner;

    public IReadOnlyList<TweakDefinition> Tweaks => _inner.Tweaks;

    public bool TryGet(string id, out TweakDefinition tweak)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _inner.TryGet(id, out tweak) || AutostartTweaks.TryCreate(id, out tweak) || GameTweaks.TryCreate(id, out tweak) || CustomDnsTweaks.TryCreate(id, out tweak) || UpdateTweaks.TryCreate(id, out tweak);
    }
}
