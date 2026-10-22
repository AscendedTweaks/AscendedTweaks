namespace AscendedTweaks.Core;

/// <summary>Aufbau einer Zeichenkette, die mehrere Einstellungen enthaelt.</summary>
public enum ListEntryFormat
{
    /// <summary>"Schluessel=Wert;" hintereinander, zum Beispiel DirectX UserGpuPreferences ("GpuPreference=2;SwapEffectUpgradeEnable=1;").</summary>
    KeyValueSemicolon,

    /// <summary>Kompatibilitaets-Flags "~ FLAG1 FLAG2" unter AppCompatFlags\Layers; ein Eintrag ist ein Flag ohne Wert.</summary>
    CompatLayers,
}

/// <summary>
/// Setzt genau einen Eintrag in einer Zeichenkette mit mehreren Einstellungen und laesst die uebrigen unangetastet
/// ("zusammenfuehren statt ueberschreiben", Abschnitt 6.3). Zuruecksetzen stellt nur diesen Eintrag wieder her.
/// </summary>
public sealed class RegistryListEntryAction : TweakAction
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required ListEntryFormat Format { get; init; }

    public required string Key { get; init; }

    /// <summary>Wert des Eintrags; bei <see cref="ListEntryFormat.CompatLayers"/> leer (das Flag ist gesetzt oder nicht).</summary>
    public string Value { get; init; } = string.Empty;
}

public sealed class RegistryListEntryEqualsCondition : DetectCondition
{
    public required RegHive Hive { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required ListEntryFormat Format { get; init; }

    public required string Key { get; init; }

    public string Value { get; init; } = string.Empty;
}

/// <summary>Lesen und Schreiben einzelner Eintraege; Reihenfolge und fremde Eintraege bleiben erhalten.</summary>
public static class RegistryList
{
    /// <summary>Wert des Eintrags oder null, wenn er fehlt. Bei Flags ist der Wert "" fuer "gesetzt".</summary>
    public static string? Get(string? text, ListEntryFormat format, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (format == ListEntryFormat.CompatLayers)
        {
            return Tokens(text).Flags.Contains(key, StringComparer.OrdinalIgnoreCase) ? string.Empty : null;
        }

        foreach (var (k, v) in Pairs(text))
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return null;
    }

    /// <summary>Setzt (value != null) oder entfernt (value == null) einen Eintrag. Leeres Ergebnis ergibt "".</summary>
    public static string Set(string? text, ListEntryFormat format, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (format == ListEntryFormat.CompatLayers)
        {
            var (prefix, flags) = Tokens(text ?? string.Empty);
            flags.RemoveAll(f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
            if (value is not null)
            {
                flags.Add(key);
            }

            if (flags.Count == 0)
            {
                return string.Empty;
            }

            if (prefix.Count == 0)
            {
                prefix.Add("~");
            }

            return string.Join(' ', prefix.Concat(flags));
        }

        var pairs = Pairs(text ?? string.Empty).ToList();
        var index = pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (index >= 0)
            {
                pairs.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            pairs[index] = (pairs[index].Key, value);
        }
        else
        {
            pairs.Add((key, value));
        }

        return string.Concat(pairs.Select(p => $"{p.Key}={p.Value};"));
    }

    private static IEnumerable<(string Key, string Value)> Pairs(string text)
    {
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            yield return equals < 0 ? (part, string.Empty) : (part[..equals].Trim(), part[(equals + 1)..].Trim());
        }
    }

    /// <summary>Fuehrende Sonderzeichen ("~", "$") bleiben als Praefix erhalten, der Rest sind Flags.</summary>
    private static (List<string> Prefix, List<string> Flags) Tokens(string text)
    {
        var prefix = new List<string>();
        var flags = new List<string>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (flags.Count == 0 && token.All(c => !char.IsLetterOrDigit(c)))
            {
                prefix.Add(token);
            }
            else
            {
                flags.Add(token);
            }
        }

        return (prefix, flags);
    }
}
