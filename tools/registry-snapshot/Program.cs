using System.Text;
using System.Text.RegularExpressions;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Windows;

// Exportiert Registry-Baeume als normalisierte, sortierte Textdatei und vergleicht zwei Snapshots.
//   registry-snapshot snapshot --out <datei> <HKCU\Pfad> [<HKLM\Pfad> ...]
//   registry-snapshot compare <a> <b> [--ignore <datei mit regulaeren Ausdruecken>]
// Exit: 0 gleich bzw. erfolgreich, 1 Unterschiede, 2 Aufruffehler.
if (args.Length == 0)
{
    return Usage();
}

return args[0] switch
{
    "snapshot" => Snapshot(args[1..]),
    "compare" => Compare(args[1..]),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("registry-snapshot snapshot --out <datei> <HKCU\\Pfad>... | compare <a> <b> [--ignore <datei>]");
    return 2;
}

static int Snapshot(string[] args)
{
    var outIndex = Array.IndexOf(args, "--out");
    if (outIndex < 0 || outIndex + 1 >= args.Length)
    {
        return Usage();
    }

    var output = args[outIndex + 1];
    var roots = args.Where((_, i) => i != outIndex && i != outIndex + 1).ToList();
    var registry = new WindowsRegistryAccess();
    var lines = new List<string>();
    foreach (var root in roots)
    {
        var separator = root.IndexOf('\\', StringComparison.Ordinal);
        var hive = Enum.Parse<RegHive>(separator < 0 ? root : root[..separator], ignoreCase: true);
        var path = separator < 0 ? string.Empty : root[(separator + 1)..];
        Walk(registry, hive, path, lines);
    }

    lines.Sort(StringComparer.Ordinal);
    File.WriteAllLines(output, lines, new UTF8Encoding(false));
    Console.WriteLine($"{lines.Count} Zeilen nach {output} geschrieben.");
    return 0;
}

static void Walk(WindowsRegistryAccess registry, RegHive hive, string path, List<string> lines)
{
    var display = $"{hive}\\{path}".ToUpperInvariant();
    try
    {
        if (!registry.KeyExists(hive, path))
        {
            lines.Add($"M|{display}");
            return;
        }

        lines.Add($"K|{display}");
        foreach (var name in registry.GetValueNames(hive, path))
        {
            var data = registry.GetValue(hive, path, name);
            var encoded = data is null ? "-" : $"{data.Kind}|{Convert.ToBase64String(data.ToRaw())}";
            lines.Add($"V|{display}|{name.ToUpperInvariant()}|{encoded}");
        }

        foreach (var sub in registry.GetSubKeyNames(hive, path))
        {
            Walk(registry, hive, path.Length == 0 ? sub : $"{path}\\{sub}", lines);
        }
    }
    catch (AscendedTweaksException ex)
    {
        lines.Add($"E|{display}|{ex.Kind}");
    }
}

static int Compare(string[] args)
{
    if (args.Length < 2)
    {
        return Usage();
    }

    var ignore = new List<Regex>();
    var ignoreIndex = Array.IndexOf(args, "--ignore");
    if (ignoreIndex >= 0 && ignoreIndex + 1 < args.Length)
    {
        ignore.AddRange(File.ReadAllLines(args[ignoreIndex + 1])
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => new Regex(l, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))));
    }

    bool Keep(string line) => !ignore.Any(r => r.IsMatch(line));
    var a = File.ReadAllLines(args[0]).Where(Keep).ToHashSet(StringComparer.Ordinal);
    var b = File.ReadAllLines(args[1]).Where(Keep).ToHashSet(StringComparer.Ordinal);
    var removed = a.Except(b).Order(StringComparer.Ordinal).ToList();
    var added = b.Except(a).Order(StringComparer.Ordinal).ToList();
    foreach (var line in removed)
    {
        Console.WriteLine($"- {line}");
    }

    foreach (var line in added)
    {
        Console.WriteLine($"+ {line}");
    }

    Console.WriteLine(removed.Count + added.Count == 0 ? "Snapshots gleich." : $"{removed.Count} entfernt, {added.Count} hinzugefuegt.");
    return removed.Count + added.Count == 0 ? 0 : 1;
}
