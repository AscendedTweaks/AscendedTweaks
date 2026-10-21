using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TweakPilot.Localization;

/// <summary>
/// Texte der Oberflaeche aus eingebetteten Sprachdateien (Abschnitt 8.5). Nie hartkodiert.
/// Bindungen ueber den Indexer aktualisieren sich beim Sprachwechsel automatisch.
/// </summary>
public sealed partial class Localizer : INotifyPropertyChanged
{
    public const string German = "de-DE";
    public const string English = "en-US";

    public static readonly IReadOnlyList<string> SupportedCultures = [German, English];

    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Bundles = new(LoadAll);

    /// <summary>English is the default language (V3); German is an optional translation.</summary>
    public Localizer(string culture = English)
    {
        Culture = SupportedCultures.Contains(culture) ? culture : English;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Instanz fuer XAML-Bindungen.</summary>
    public static Localizer Current { get; set; } = new();

    public string Culture { get; private set; }

    public string this[string key] => Lookup(key);

    public static IReadOnlyDictionary<string, string> Bundle(string culture) => Bundles.Value[culture];

    /// <summary>Platzhalter der Form {name} in einem Text.</summary>
    public static IReadOnlySet<string> Placeholders(string text)
    {
        return PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    public void SetCulture(string culture)
    {
        if (!SupportedCultures.Contains(culture) || culture == Culture)
        {
            return;
        }

        Culture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Text mit ersetzten Platzhaltern, zum Beispiel Format("changebar.pending", ("n", 3)).</summary>
    public string Format(string key, params (string Name, object Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var text = Lookup(key);
        foreach (var (name, value) in values)
        {
            text = text.Replace("{" + name + "}", Convert.ToString(value, System.Globalization.CultureInfo.GetCultureInfo(Culture)), StringComparison.Ordinal);
        }

        return text;
    }

    private string Lookup(string key)
    {
        if (Bundles.Value.TryGetValue(Culture, out var bundle) && bundle.TryGetValue(key, out var text))
        {
            return text;
        }

        return Bundles.Value[English].TryGetValue(key, out var fallback) ? fallback : $"[{key}]";
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var assembly = typeof(Localizer).Assembly;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var culture in SupportedCultures)
        {
            using var stream = assembly.GetManifestResourceStream($"strings/{culture}.json")
                ?? throw new InvalidOperationException($"Language file {culture} is missing.");
            result[culture] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Language file {culture} is empty.");
        }

        return result;
    }

    [GeneratedRegex(@"\{([a-zA-Z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
