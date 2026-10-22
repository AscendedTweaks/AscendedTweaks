using System.Text.Json.Serialization;

namespace AscendedTweaks.Core;

/// <summary>Beschreibung der Zustandserkennung. Der JSON-Diskriminator ist das Feld "type".</summary>
public abstract class DetectCondition
{
}

public sealed class RegistryValueEqualsCondition : DetectCondition
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required RegLiteral Value { get; init; }

    /// <summary>Zustand, wenn Schluessel oder Wert fehlt.</summary>
    public TweakState DefaultWhenMissing { get; init; } = TweakState.NotApplied;
}

public sealed class RegistryValueAbsentCondition : DetectCondition
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }
}

public sealed class RegistryKeyExistsCondition : DetectCondition
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }
}

public sealed class RegistryKeyAbsentCondition : DetectCondition
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }
}

public sealed class ServiceStartTypeEqualsCondition : DetectCondition
{
    public required string Service { get; init; }

    public required ServiceStartSetting Value { get; init; }
}

public sealed class PowerPlanActiveCondition : DetectCondition
{
    [JsonPropertyName("guid")]
    public required Guid SchemeGuid { get; init; }

    /// <summary>Zustand, wenn der Plan nicht existiert (Standard: nicht verfuegbar).</summary>
    public TweakState MissingIs { get; init; } = TweakState.Unavailable;
}

/// <summary>Wert einer Unter-Einstellung im aktiven Energieplan (Netzbetrieb).</summary>
public sealed class PowerSettingEqualsCondition : DetectCondition
{
    public required Guid Subgroup { get; init; }

    public required Guid Setting { get; init; }

    public required uint Value { get; init; }
}

public sealed class CommandQueryEqualsCondition : DetectCondition
{
    public required string Command { get; init; }

    public required string Value { get; init; }
}

public sealed class AllOfCondition : DetectCondition
{
    public required IReadOnlyList<DetectCondition> Conditions { get; init; }

    /// <summary>Alle Blattbedingungen, rekursiv aufgeloest.</summary>
    public IEnumerable<DetectCondition> Leaves()
    {
        foreach (var c in Conditions)
        {
            if (c is AllOfCondition nested)
            {
                foreach (var leaf in nested.Leaves())
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return c;
            }
        }
    }
}

public static class DetectTypeNames
{
    public static readonly IReadOnlyDictionary<string, Type> All = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["RegistryValueEquals"] = typeof(RegistryValueEqualsCondition),
        ["RegistryValueAbsent"] = typeof(RegistryValueAbsentCondition),
        ["RegistryKeyExists"] = typeof(RegistryKeyExistsCondition),
        ["RegistryKeyAbsent"] = typeof(RegistryKeyAbsentCondition),
        ["ServiceStartTypeEquals"] = typeof(ServiceStartTypeEqualsCondition),
        ["PowerPlanActive"] = typeof(PowerPlanActiveCondition),
        ["PowerSettingEquals"] = typeof(PowerSettingEqualsCondition),
        ["CommandQueryEquals"] = typeof(CommandQueryEqualsCondition),
        ["ScheduledTaskEnabledEquals"] = typeof(ScheduledTaskEnabledEqualsCondition),
        ["SystemParameterEquals"] = typeof(SystemParameterEqualsCondition),
        ["RegistryListEntryEquals"] = typeof(RegistryListEntryEqualsCondition),
        ["AllOf"] = typeof(AllOfCondition),
    };

    public static string NameOf(DetectCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return All.First(kv => kv.Value == condition.GetType()).Key;
    }

    public static IEnumerable<DetectCondition> LeavesOf(DetectCondition condition)
    {
        return condition is AllOfCondition all ? all.Leaves() : [condition];
    }
}
