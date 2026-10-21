using System.Text.Json.Serialization;
using TweakPilot.Core;

namespace TweakPilot.Engine;

/// <summary>Gesicherter Registry-Wert: Rohdaten als Base64 plus lesbare Darstellung (Abschnitt 7.2).</summary>
public sealed record StoredRegistryData
{
    public required RegValueKind Kind { get; init; }

    public required string Base64 { get; init; }

    public required string Display { get; init; }

    public static StoredRegistryData? From(RegistryData? data)
    {
        return data is null
            ? null
            : new StoredRegistryData { Kind = data.Kind, Base64 = Convert.ToBase64String(data.ToRaw()), Display = data.Display() };
    }

    public RegistryData ToData() => RegistryData.FromRaw(Kind, Convert.FromBase64String(Base64));

    public static RegistryData? ToData(StoredRegistryData? stored) => stored?.ToData();

    public static string DisplayOf(StoredRegistryData? stored) => stored?.Display ?? EngineText.Missing;
}

/// <summary>
/// Zustand einer Aktion vor dem Anwenden (Before-Felder) und nach dem Anwenden (Written-Felder).
/// Restore arbeitet nur mit den Before-Feldern und ist idempotent.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "captureType")]
[JsonDerivedType(typeof(RegistryValueCapture), "registryValue")]
[JsonDerivedType(typeof(RegistryKeyCapture), "registryKey")]
[JsonDerivedType(typeof(RegistryTreeCapture), "registryTree")]
[JsonDerivedType(typeof(ServiceCapture), "service")]
[JsonDerivedType(typeof(PowerPlanCapture), "powerPlan")]
[JsonDerivedType(typeof(PowerSettingCapture), "powerSetting")]
[JsonDerivedType(typeof(CommandCapture), "command")]
[JsonDerivedType(typeof(ScheduledTaskCapture), "scheduledTask")]
[JsonDerivedType(typeof(SystemParameterCapture), "systemParameter")]
[JsonDerivedType(typeof(RegistryListEntryCapture), "registryListEntry")]
public abstract record ActionCapture
{
    /// <summary>True, sobald die Aktion ausgefuehrt wurde und die Written-Felder gueltig sind.</summary>
    public bool Applied { get; init; }
}

public sealed record RegistryValueCapture : ActionCapture
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public StoredRegistryData? Before { get; init; }

    /// <summary>Schluessel, die vor dem Anwenden fehlten (vom kuerzesten zum laengsten Pfad).</summary>
    public IReadOnlyList<string> MissingKeys { get; init; } = [];

    /// <summary>Geschriebener Wert; null bei Applied bedeutet: Wert wurde geloescht.</summary>
    public StoredRegistryData? Written { get; init; }
}

public sealed record RegistryKeyCapture : ActionCapture
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public IReadOnlyList<string> MissingKeys { get; init; } = [];

    public StoredRegistryData? DefaultBefore { get; init; }

    public StoredRegistryData? DefaultWritten { get; init; }
}

public sealed record RegistryTreeNode
{
    public required string Name { get; init; }

    public IReadOnlyList<RegistryTreeValue> Values { get; init; } = [];

    public IReadOnlyList<RegistryTreeNode> SubKeys { get; init; } = [];
}

public sealed record RegistryTreeValue
{
    public required string Name { get; init; }

    public required StoredRegistryData Data { get; init; }
}

public sealed record RegistryTreeCapture : ActionCapture
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    /// <summary>Kompletter Teilbaum vor dem Loeschen; null, wenn der Schluessel nicht existierte.</summary>
    public RegistryTreeNode? Tree { get; init; }
}

public sealed record ServiceCapture : ActionCapture
{
    public required string Service { get; init; }

    public required ServiceStartType BeforeStartType { get; init; }

    public required bool BeforeDelayed { get; init; }

    public required bool BeforeRunning { get; init; }

    public ServiceStartType WrittenStartType { get; init; }

    public bool WrittenDelayed { get; init; }
}

public sealed record PowerPlanCapture : ActionCapture
{
    public required Guid BeforeActive { get; init; }

    public required Guid Target { get; init; }

    /// <summary>Von der App per Duplikat angelegter Plan, der beim Zuruecksetzen wieder entfernt wird.</summary>
    public Guid? CreatedScheme { get; init; }
}

/// <summary>Unter-Einstellung eines Energieplans; Scheme ist der beim Anwenden aktive Plan.</summary>
public sealed record PowerSettingCapture : ActionCapture
{
    public required Guid Scheme { get; init; }

    public required Guid Subgroup { get; init; }

    public required Guid Setting { get; init; }

    public required uint Before { get; init; }

    public uint Written { get; init; }
}

public sealed record CommandCapture : ActionCapture
{
    public required string Command { get; init; }

    public required string BeforeValue { get; init; }

    public string? WrittenValue { get; init; }
}

public sealed record ScheduledTaskCapture : ActionCapture
{
    public required string TaskPath { get; init; }

    public required bool BeforeEnabled { get; init; }

    public bool? WrittenEnabled { get; init; }
}

public sealed record SystemParameterCapture : ActionCapture
{
    public required string Parameter { get; init; }

    public required uint BeforeValue { get; init; }

    public uint? WrittenValue { get; init; }
}

public sealed record RegistryListEntryCapture : ActionCapture
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required ListEntryFormat Format { get; init; }

    public required string Key { get; init; }

    /// <summary>Ganze Zeichenkette vor dem Anwenden (fuer Anzeige und .reg-Export).</summary>
    public StoredRegistryData? Before { get; init; }

    /// <summary>Wert des Eintrags vorher; null bedeutet: Eintrag fehlte.</summary>
    public string? BeforeEntry { get; init; }

    public IReadOnlyList<string> MissingKeys { get; init; } = [];

    public string? WrittenEntry { get; init; }
}
