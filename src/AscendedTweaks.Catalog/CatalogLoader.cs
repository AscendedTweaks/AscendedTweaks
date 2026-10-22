using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AscendedTweaks.Core;
using Json.Schema;

namespace AscendedTweaks.Catalog;

/// <summary>Laedt Tweaks aus JSON, prueft sie gegen das Schema und den Linter.</summary>
public static class CatalogLoader
{
    private const string ResourcePrefix = "catalog/";

    /// <summary>Laedt den eingebetteten Katalog. Ist er ungueltig, wird nichts geladen.</summary>
    public static TweakCatalog LoadEmbedded()
    {
        var assembly = typeof(CatalogLoader).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).ToList();
        var schema = ReadResource(assembly, ResourcePrefix + "tweak.schema.json");
        var tweaks = names
            .Where(n => n.StartsWith(ResourcePrefix + "tweaks/", StringComparison.Ordinal))
            .Select(n => new CatalogSourceFile(n, ReadResource(assembly, n)))
            .ToList();
        var texts = names
            .Where(n => n.StartsWith(ResourcePrefix + "i18n/", StringComparison.Ordinal))
            .ToDictionary(n => Path.GetFileNameWithoutExtension(n), n => ReadResource(assembly, n), StringComparer.Ordinal);

        var result = Load(tweaks, texts, schema);
        if (!result.IsValid)
        {
            throw new AscendedTweaksException(
                ErrorKind.CatalogInvalid,
                "Embedded catalog is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, result.Issues));
        }

        return result.Catalog!;
    }

    /// <summary>Laedt den Katalog aus dem Quellordner des Projekts AscendedTweaks.Catalog (fuer tools/catalog-lint).</summary>
    public static CatalogLoadResult LoadFromDirectory(string catalogProjectDirectory)
    {
        // Vollstaendiger Pfad, damit der Vergleich mit dem i18n-Ordner auch bei "/" als Trenner stimmt.
        catalogProjectDirectory = Path.GetFullPath(catalogProjectDirectory);
        var schema = File.ReadAllText(Path.Combine(catalogProjectDirectory, "tweak.schema.json"));
        var tweaksRoot = Path.Combine(catalogProjectDirectory, "tweaks");
        var i18nRoot = Path.Combine(tweaksRoot, "i18n");
        var tweaks = Directory.EnumerateFiles(tweaksRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetDirectoryName(f), i18nRoot, StringComparison.OrdinalIgnoreCase))
            .Select(f => new CatalogSourceFile(Path.GetRelativePath(catalogProjectDirectory, f), File.ReadAllText(f)))
            .ToList();
        var texts = Directory.EnumerateFiles(i18nRoot, "*.json")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), File.ReadAllText, StringComparer.Ordinal);
        return Load(tweaks, texts, schema);
    }

    public static CatalogLoadResult Load(
        IEnumerable<CatalogSourceFile> tweakFiles,
        IReadOnlyDictionary<string, string> textFiles,
        string schemaJson)
    {
        ArgumentNullException.ThrowIfNull(tweakFiles);
        ArgumentNullException.ThrowIfNull(textFiles);
        var issues = new List<LintIssue>();
        var schema = JsonSchema.FromText(schemaJson);
        var tweaks = new List<TweakDefinition>();

        foreach (var file in tweakFiles)
        {
            var tweak = LoadTweak(file, schema, issues);
            if (tweak is not null)
            {
                tweaks.Add(tweak);
            }
        }

        var texts = new Dictionary<string, TextBundle>(StringComparer.Ordinal);
        foreach (var (culture, json) in textFiles)
        {
            try
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                texts[culture] = new TextBundle(culture, map);
            }
            catch (JsonException ex)
            {
                issues.Add(new LintIssue(null, "i18n", $"Sprachdatei {culture}: {ex.Message}"));
            }
        }

        issues.AddRange(CatalogLinter.Lint(tweaks, texts));
        return new CatalogLoadResult(new TweakCatalog(tweaks, texts), issues);
    }

    private static TweakDefinition? LoadTweak(CatalogSourceFile file, JsonSchema schema, List<LintIssue> issues)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(file.Json);
        }
        catch (JsonException ex)
        {
            issues.Add(new LintIssue(null, "json", $"{file.Name}: {ex.Message}"));
            return null;
        }

        var evaluation = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid)
        {
            var messages = evaluation.Details
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
                .Distinct(StringComparer.Ordinal)
                .Take(20);
            foreach (var message in messages)
            {
                issues.Add(new LintIssue(null, "schema", $"{file.Name}: {message}"));
            }

            return null;
        }

        TweakDefinition tweak;
        try
        {
            tweak = node.Deserialize<TweakDefinition>(CoreJson.Options)
                ?? throw new JsonException("Leeres Dokument.");
        }
        catch (JsonException ex)
        {
            issues.Add(new LintIssue(null, "json", $"{file.Name}: {ex.Message}"));
            return null;
        }

        var fileId = Path.GetFileNameWithoutExtension(file.Name.Replace('\\', '/'));
        if (!string.Equals(fileId, tweak.Id, StringComparison.Ordinal))
        {
            issues.Add(new LintIssue(tweak.Id, "file-name", $"File name {file.Name} does not match the ID."));
        }

        return tweak;
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
