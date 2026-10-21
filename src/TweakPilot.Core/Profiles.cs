using System.Text;
using System.Text.Json;

namespace TweakPilot.Core;

/// <summary>Zielzustand eines Tweaks im Profil.</summary>
public enum ProfileTarget
{
    Applied,
    NotApplied,
}

/// <summary>Bedingung fuer einen Eintrag; DesktopOnly wird auf Geraeten mit Akku uebersprungen (Gaming: PERF-001).</summary>
public enum ProfileCondition
{
    None,
    DesktopOnly,
}

public sealed record ProfileEntry
{
    public required string Id { get; init; }

    public ProfileTarget Target { get; init; } = ProfileTarget.Applied;

    public ProfileCondition Condition { get; init; } = ProfileCondition.None;
}

/// <summary>Profil (Abschnitt 6.10): benannte Liste von Tweak-IDs mit Zielzustand. Vorgefertigte Profile haben einen Schluessel fuer die Texte.</summary>
public sealed record TweakProfile
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; init; } = CurrentSchema;

    /// <summary>Nur vorgefertigte Profile; Name und Beschreibung kommen dann aus den Texten ("profile.&lt;key&gt;.name").</summary>
    public string? Key { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public required IReadOnlyList<ProfileEntry> Tweaks { get; init; }

    /// <summary>V2: Symbol der Preset-Karte (Name eines Fluent-Symbols, nur vorgefertigte Presets).</summary>
    public string? Symbol { get; init; }

    /// <summary>V2: Bereinigungs-Bereiche (CLEAN-...), nur in vorgefertigten Presets; ein Import darf nie Dateien loeschen lassen.</summary>
    public IReadOnlyList<string> Cleanup { get; init; } = [];
}

/// <summary>Lesen und Schreiben von Profilen. Importierte Dateien sind nicht vertrauenswuerdig (Abschnitt 6.10, Bedrohungsmodell B).</summary>
public static class ProfileCodec
{
    public const string Extension = ".tpprofile";
    public const int MaxBytes = 1024 * 1024;
    public const int MaxEntries = 500;
    public const int MaxNameLength = 60;
    public const int MaxDescriptionLength = 500;

    /// <summary>
    /// Prueft und liest ein Profil. Erlaubt sind nur bekannte Felder, Katalog-IDs und eigene DNS-Server (NET-004);
    /// alle anderen erzeugten IDs enthalten Pfade oder Programmnamen und sind im Import gesperrt.
    /// </summary>
    public static TweakProfile Parse(ReadOnlySpan<byte> data, ITweakCatalog catalog, bool builtIn = false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (data.Length == 0 || data.Length > MaxBytes)
        {
            throw new TweakPilotException(ErrorKind.General, "The profile file is empty or larger than 1 MB.");
        }

        TweakProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<TweakProfile>(data, CoreJson.Options);
        }
        catch (JsonException ex)
        {
            throw new TweakPilotException(ErrorKind.General, "The profile file is not a valid profile: " + ex.Message);
        }

        if (profile is null || profile.Tweaks is null)
        {
            throw new TweakPilotException(ErrorKind.General, "The profile file is not a valid profile.");
        }

        Validate(profile, catalog, builtIn);
        return profile;
    }

    public static void Validate(TweakProfile profile, ITweakCatalog catalog, bool builtIn = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(catalog);
        if (profile.SchemaVersion != TweakProfile.CurrentSchema)
        {
            throw new TweakPilotException(ErrorKind.General, $"Unknown profile version {profile.SchemaVersion}.");
        }

        if (builtIn ? string.IsNullOrEmpty(profile.Key) : profile.Key is not null || !IsValidName(profile.Name))
        {
            throw new TweakPilotException(ErrorKind.General, "The profile name is missing, too long or contains control characters.");
        }

        if (profile.Description is { } description && (description.Length > MaxDescriptionLength || description.Any(char.IsControl)))
        {
            throw new TweakPilotException(ErrorKind.General, "The description is too long or contains control characters.");
        }

        if (profile.Cleanup.Count > 0 && !builtIn)
        {
            throw new TweakPilotException(ErrorKind.General, "Cleanup is only allowed in built-in presets.");
        }

        if (profile.Cleanup.Any(id => !CleanupCategories.TryGet(id, out _)) || profile.Cleanup.Distinct(StringComparer.Ordinal).Count() != profile.Cleanup.Count)
        {
            throw new TweakPilotException(ErrorKind.General, "Unknown or duplicate cleanup area.");
        }

        if (profile.Tweaks.Count + profile.Cleanup.Count == 0 || profile.Tweaks.Count > MaxEntries)
        {
            throw new TweakPilotException(ErrorKind.General, $"Ein Profil braucht 1 bis {MaxEntries} Tweaks.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in profile.Tweaks)
        {
            if (entry?.Id is null || !IsAllowedId(entry.Id, catalog))
            {
                throw new TweakPilotException(ErrorKind.General, $"Unknown or not allowed tweak ID \"{Shorten(entry?.Id)}\".");
            }

            if (!seen.Add(entry.Id))
            {
                throw new TweakPilotException(ErrorKind.General, $"Tweak ID {entry.Id} appears twice in the profile.");
            }

            if (!Enum.IsDefined(entry.Target) || !Enum.IsDefined(entry.Condition))
            {
                throw new TweakPilotException(ErrorKind.General, $"Invalid target state for {entry.Id}.");
            }
        }
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength && !name.Any(char.IsControl) && name.Trim() == name;

    public static bool IsAllowedId(string id, ITweakCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Tweaks.Any(t => t.Id == id) || CustomDnsTweaks.TryCreate(id, out _);
    }

    public static byte[] Serialize(TweakProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.SerializeToUtf8Bytes(profile, CoreJson.Options);
    }

    /// <summary>Gueltiger Dateiname aus dem Profilnamen (fuer den Export).</summary>
    public static string FileNameFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        return (result.Length == 0 ? "Profil" : result) + Extension;
    }

    private static string Shorten(string? id) => id is null ? string.Empty : id.Length > 40 ? id[..40] + "..." : id;
}
