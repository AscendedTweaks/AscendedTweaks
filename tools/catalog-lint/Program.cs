using AscendedTweaks.Catalog;

// Prueft alle Tweak-Dateien im Quellordner gegen Schema und Linter-Regeln. Exit 0 = sauber, 1 = Befunde.
var directory = args.Length > 0 ? args[0] : FindCatalogDirectory();
if (directory is null || !Directory.Exists(directory))
{
    Console.Error.WriteLine("Katalogordner nicht gefunden. Aufruf: catalog-lint <Pfad zu src/AscendedTweaks.Catalog>");
    return 2;
}

var result = CatalogLoader.LoadFromDirectory(directory);
foreach (var issue in result.Issues)
{
    Console.Error.WriteLine(issue);
}

var count = result.Catalog?.AllIncludingPlacebo.Count ?? 0;
Console.WriteLine(result.IsValid
    ? $"Katalog sauber: {count} Tweaks geprueft."
    : $"Katalog ungueltig: {result.Issues.Count} Befunde bei {count} Tweaks.");
return result.IsValid ? 0 : 1;

static string? FindCatalogDirectory()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "src", "AscendedTweaks.Catalog");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}
