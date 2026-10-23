using System.Globalization;
using System.Text.RegularExpressions;

namespace AscendedTweaks.Tools.ContrastCheck;

/// <summary>
/// Prueft alle Text-auf-Flaeche-Paare des dunklen Designs (das einzige Design) gegen WCAG 2.x (4,5:1 fuer Text, 3:1 fuer Symbole und Bedienelemente).
/// Getoente Flaechen (Auswahl, Hover, gefuellte Badges) werden als Alpha-Mischung berechnet.
/// Aufruf: contrast-check [Ordner mit Tokens.Dark.xaml]. Exit 0 = alle Paare bestanden, 1 = mindestens ein Paar zu schwach.
/// </summary>
internal static partial class Program
{
    private const double Text = 4.5;
    private const double Graphic = 3.0;

    private static readonly string[] Surfaces = ["Background", "Surface", "Elevated"];
    private static readonly string[] TextTokens = ["TextPrimary", "TextSecondary", "Accent", "Success", "Warning", "Danger"];
    private static readonly string[] Tones = ["Success", "Warning", "Danger", "Accent"];

    private static int Main(string[] args)
    {
        var directory = args.Length > 0 ? args[0] : FindThemes();
        if (directory is null)
        {
            Console.Error.WriteLine("Ordner mit Tokens.Dark.xaml nicht gefunden.");
            return 2;
        }

        var failures = 0;
        foreach (var theme in new[] { "Dark" })
        {
            var tokens = ReadTokens(Path.Combine(directory, $"Tokens.{theme}.xaml"));
            foreach (var check in Checks(tokens))
            {
                var ratio = Ratio(check.Foreground, check.Background);
                var ok = ratio >= check.Minimum;
                failures += ok ? 0 : 1;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(ok ? "OK  " : "FAIL")} {theme,-5} {check.Name,-48} {ratio,5:0.00}:1 (min {check.Minimum:0.0})"));
            }
        }

        Console.WriteLine(failures == 0 ? "Kontrastpruefung bestanden." : $"Kontrastpruefung: {failures} Paare zu schwach.");
        return failures == 0 ? 0 : 1;
    }

    internal sealed record Check(string Name, (double R, double G, double B) Foreground, (double R, double G, double B) Background, double Minimum);

    internal static IEnumerable<Check> Checks(IReadOnlyDictionary<string, (double R, double G, double B)> t)
    {
        // The darker frame (title bar and navigation) carries text too.
        foreach (var surface in t.ContainsKey("Sidebar") ? Surfaces.Append("Sidebar") : Surfaces)
        {
            foreach (var text in TextTokens)
            {
                yield return new Check($"{text} auf {surface}", t[text], t[surface], Text);
            }
        }

        yield return new Check("OnAccent auf Accent (Schalter, Badges)", t["OnAccent"], t["Accent"], Text);
        if (t.TryGetValue("AccentFill", out var fill))
        {
            yield return new Check("OnAccentFill auf AccentFill (Primaerschaltflaeche)", t["OnAccentFill"], fill, Text);
        }

        if (t.TryGetValue("AccentTint", out var tint))
        {
            yield return new Check("TextPrimary auf AccentTint (Auswahl)", t["TextPrimary"], tint, Text);
            yield return new Check("Accent auf AccentTint (Symbolkachel)", t["Accent"], tint, Graphic);
        }

        yield return new Check("OnAccent auf Accent (Schalterknopf)", t["OnAccent"], t["Accent"], Graphic);
        yield return new Check("TextSecondary auf Surface (Schalter aus)", t["TextSecondary"], t["Surface"], Graphic);

        foreach (var surface in Surfaces)
        {
            var selected = Blend(t["Accent"], 0.14, t[surface]);
            yield return new Check($"TextPrimary auf Auswahl ueber {surface}", t["TextPrimary"], selected, Text);
            var hover = Blend(t["TextSecondary"], 0.10, t[surface]);
            yield return new Check($"TextPrimary auf Hover ueber {surface}", t["TextPrimary"], hover, Text);
            foreach (var tone in Tones)
            {
                var filled = Blend(t[tone], 0.15, t[surface]);
                yield return new Check($"TextPrimary auf Badge {tone} ueber {surface}", t["TextPrimary"], filled, Text);
                yield return new Check($"Symbol {tone} auf Badge {tone} ueber {surface}", t[tone], filled, Graphic);
            }
        }
    }

    internal static IReadOnlyDictionary<string, (double R, double G, double B)> ReadTokens(string file)
    {
        var result = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
        foreach (Match match in ColorPattern().Matches(File.ReadAllText(file)))
        {
            result[match.Groups["key"].Value] = Parse(match.Groups["hex"].Value);
        }

        return result;
    }

    internal static (double R, double G, double B) Parse(string hex)
    {
        return (
            int.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
    }

    internal static (double R, double G, double B) Blend((double R, double G, double B) top, double alpha, (double R, double G, double B) bottom)
    {
        return (
            (top.R * alpha) + (bottom.R * (1 - alpha)),
            (top.G * alpha) + (bottom.G * (1 - alpha)),
            (top.B * alpha) + (bottom.B * (1 - alpha)));
    }

    internal static double Ratio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((double R, double G, double B) c)
    {
        static double Channel(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static string? FindThemes()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AscendedTweaks.App", "Themes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [GeneratedRegex(@"<Color x:Key=""Color\.(?<key>\w+)"">#(?<hex>[0-9A-Fa-f]{6})</Color>", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
