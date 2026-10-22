using System.Text.Json.Serialization;

namespace AscendedTweaks.Core;

/// <summary>Basis aller Aktionen (Abschnitt 5.2). Der JSON-Diskriminator ist das Feld "type".</summary>
public abstract class TweakAction
{
}

public sealed class RegistrySetValueAction : TweakAction
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required RegValueKind Kind { get; init; }

    public required RegLiteral Value { get; init; }
}

public sealed class RegistryDeleteValueAction : TweakAction
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }
}

public sealed class RegistryCreateKeyAction : TweakAction
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    /// <summary>Optionaler Standardwert (REG_SZ) des angelegten Schluessels.</summary>
    public string? DefaultValue { get; init; }
}

public sealed class RegistryDeleteKeyAction : TweakAction
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }
}

public sealed class ServiceSetStartTypeAction : TweakAction
{
    public required string Service { get; init; }

    public required ServiceStartSetting StartType { get; init; }

    public bool StopNow { get; init; }

    /// <summary>Starttyp einer frischen Windows-Installation (Anzeige im Dienste-Manager).</summary>
    public ServiceStartSetting? WindowsDefault { get; init; }
}

public sealed class PowerPlanSetAction : TweakAction
{
    [JsonPropertyName("guid")]
    public required Guid SchemeGuid { get; init; }

    /// <summary>Fehlt der Plan, wird er aus diesem Plan mit der Ziel-GUID dupliziert.</summary>
    public Guid? DuplicateFrom { get; init; }
}

/// <summary>
/// Unter-Einstellung des aktiven Energieplans (Netzbetrieb, V2 Abschnitt 5.2). Geschrieben wird mit PowerWriteACValueIndex
/// in den beim Anwenden aktiven Plan; die Sicherung merkt sich diesen Plan, damit das Zuruecksetzen denselben Plan trifft.
/// </summary>
public sealed class PowerSettingSetAction : TweakAction
{
    public required Guid Subgroup { get; init; }

    public required Guid Setting { get; init; }

    public required uint Value { get; init; }
}

/// <summary>Befehl aus der fest eingebetteten Whitelist mit typisiertem Wert.</summary>
public sealed class CommandAction : TweakAction
{
    public required string Command { get; init; }

    public required string Value { get; init; }
}

public static class ActionTypeNames
{
    public static readonly IReadOnlyDictionary<string, Type> Implemented = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["RegistrySetValue"] = typeof(RegistrySetValueAction),
        ["RegistryDeleteValue"] = typeof(RegistryDeleteValueAction),
        ["RegistryCreateKey"] = typeof(RegistryCreateKeyAction),
        ["RegistryDeleteKey"] = typeof(RegistryDeleteKeyAction),
        ["ServiceSetStartType"] = typeof(ServiceSetStartTypeAction),
        ["PowerPlanSet"] = typeof(PowerPlanSetAction),
        ["PowerSettingSet"] = typeof(PowerSettingSetAction),
        ["Command"] = typeof(CommandAction),
        ["ScheduledTaskSetEnabled"] = typeof(ScheduledTaskSetEnabledAction),
        ["SystemParametersInfo"] = typeof(SystemParametersInfoAction),
        ["RegistryListEntry"] = typeof(RegistryListEntryAction),
    };

    /// <summary>In Abschnitt 5.2 vorgesehen, aber erst in spaeteren Phasen implementiert.</summary>
    public static readonly IReadOnlySet<string> Planned = new HashSet<string>(StringComparer.Ordinal)
    {
        "AppxRemove",
        "FileClean",
    };

    public static string NameOf(TweakAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Implemented.First(kv => kv.Value == action.GetType()).Key;
    }
}
