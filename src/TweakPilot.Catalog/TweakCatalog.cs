using TweakPilot.Core;

namespace TweakPilot.Catalog;

public sealed class TextBundle
{
    public TextBundle(string culture, IReadOnlyDictionary<string, string> texts)
    {
        Culture = culture;
        Texts = texts;
    }

    public string Culture { get; }

    public IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>Fehlende Schluessel werden sichtbar markiert statt still leer zu bleiben.</summary>
    public string Get(string key) => Texts.TryGetValue(key, out var value) ? value : $"[{key}]";
}

public sealed class TweakCatalog : ITweakCatalog
{
    private readonly Dictionary<string, TweakDefinition> _byId;

    public TweakCatalog(IReadOnlyList<TweakDefinition> allTweaks, IReadOnlyDictionary<string, TextBundle> texts)
    {
        ArgumentNullException.ThrowIfNull(allTweaks);
        AllIncludingPlacebo = allTweaks;
        Tweaks = allTweaks.Where(t => t.Evidence != EvidenceLevel.Placebo).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        // Doppelte IDs meldet der Linter; hier gewinnt der erste Eintrag, damit das Laden nicht abbricht.
        _byId = Tweaks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Texts = texts;
    }

    /// <summary>Auslieferbare Tweaks. Placebo-Eintraege sind nie enthalten.</summary>
    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public IReadOnlyList<TweakDefinition> AllIncludingPlacebo { get; }

    public IReadOnlyDictionary<string, TextBundle> Texts { get; }

    public bool TryGet(string id, out TweakDefinition tweak) => _byId.TryGetValue(id, out tweak!);

    public string Text(string culture, string key)
    {
        return Texts.TryGetValue(culture, out var bundle) ? bundle.Get(key) : $"[{key}]";
    }
}

public sealed record LintIssue(string? TweakId, string Rule, string Message)
{
    public override string ToString() => $"{TweakId ?? "-"} [{Rule}] {Message}";
}

public sealed record CatalogSourceFile(string Name, string Json);

public sealed class CatalogLoadResult
{
    public CatalogLoadResult(TweakCatalog? catalog, IReadOnlyList<LintIssue> issues)
    {
        Catalog = catalog;
        Issues = issues;
    }

    public TweakCatalog? Catalog { get; }

    public IReadOnlyList<LintIssue> Issues { get; }

    public bool IsValid => Catalog is not null && Issues.Count == 0;
}
