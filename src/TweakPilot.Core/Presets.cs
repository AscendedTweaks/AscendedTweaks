namespace TweakPilot.Core;

/// <summary>Stufe eines Presets (V2, Abschnitt 4.2).</summary>
public enum PresetLevel
{
    Gentle,
    Standard,
    Strong,
}

/// <summary>
/// Feste Stufen-Regel, nicht pro Preset hart kodiert, damit neue Tweaks automatisch einsortiert werden:
/// Sanft = Sicher und Spuerbar; Standard = Sanft plus Sicher/Klein plus Vorsicht/Spuerbar; Stark = Standard plus Vorsicht/Klein.
/// Nie Experte, nie Unklar, nie Sicherheitsfunktionen. Energie-Tweaks, die auf Notebooks Akku kosten (Bedingung "nur Desktop"),
/// nur in "Stark" und nur auf Geraeten ohne Akku.
/// </summary>
public static class PresetLevels
{
    /// <summary>Kategorien mit Sicherheitsfunktionen; kein Preset beruehrt sie.</summary>
    public static readonly IReadOnlySet<string> SecurityCategories = new HashSet<string>(StringComparer.Ordinal) { "Gaming.Security" };

    public static bool Includes(PresetLevel level, RiskLevel risk, ImpactLevel impact)
    {
        if (risk == RiskLevel.Expert || impact == ImpactLevel.Unclear)
        {
            return false;
        }

        return level switch
        {
            PresetLevel.Gentle => risk == RiskLevel.Safe && impact == ImpactLevel.Noticeable,
            PresetLevel.Standard => risk == RiskLevel.Safe || impact == ImpactLevel.Noticeable,
            _ => true,
        };
    }

    /// <summary>Ein Preset-Eintrag gehoert zur Stufe. Zuruecksetzen eigener Aenderungen (Ziel "nicht angewendet") ist in jeder Stufe erlaubt.</summary>
    public static bool Includes(PresetLevel level, TweakDefinition tweak, ProfileEntry entry, bool hasBattery)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Target == ProfileTarget.NotApplied)
        {
            return true;
        }

        if (SecurityCategories.Contains(tweak.Category) || tweak.HiddenByDefault)
        {
            return false;
        }

        if (entry.Condition == ProfileCondition.DesktopOnly && (level != PresetLevel.Strong || hasBattery))
        {
            return false;
        }

        return Includes(level, tweak.Risk, tweak.Impact ?? ImpactLevel.Unclear);
    }

    /// <summary>
    /// Bereinigung im Preset "Aufraeumen": Sanft = sichere Bereiche des Benutzers ohne Papierkorb (keine Administratorrechte);
    /// Standard = alle sicheren Bereiche ohne Papierkorb; Stark = zusaetzlich Papierkorb und Bereiche mit Vorsicht.
    /// </summary>
    public static bool IncludesCleanup(PresetLevel level, CleanupCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return level switch
        {
            PresetLevel.Gentle => category.Risk == RiskLevel.Safe && category.Scope == TweakScope.User && category.Handler != CleanupHandler.RecycleBin,
            PresetLevel.Standard => category.Risk == RiskLevel.Safe && category.Handler != CleanupHandler.RecycleBin,
            _ => category.Risk is RiskLevel.Safe or RiskLevel.Caution,
        };
    }
}

/// <summary>
/// Welche Presets einen Tweak wollen (V2, Abschnitt 4.3). "Preset rueckgaengig" setzt nur Tweaks zurueck, die kein anderes Preset
/// mehr braucht: Preset A anwenden, Preset B anwenden, A rueckgaengig: was B gesetzt hat oder ebenfalls will, bleibt.
/// </summary>
public sealed class PresetOwnership
{
    private readonly Dictionary<string, List<string>> _owners;

    public PresetOwnership(IReadOnlyDictionary<string, IReadOnlyList<string>>? owners = null)
    {
        _owners = owners?.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot() =>
        _owners.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToList(), StringComparer.Ordinal);

    public IReadOnlyList<string> OwnersOf(string tweakId) => _owners.TryGetValue(tweakId, out var list) ? list : [];

    /// <summary>Das Preset will diese Tweaks (angewendet oder schon im Zielzustand). Der spaetere Eintrag steht am Ende.</summary>
    public void Claim(string preset, IEnumerable<string> tweakIds)
    {
        foreach (var id in tweakIds)
        {
            if (!_owners.TryGetValue(id, out var list))
            {
                _owners[id] = list = [];
            }

            list.Remove(preset);
            list.Add(preset);
        }
    }

    /// <summary>Das Preset hat diese Tweaks zurueckgesetzt (Ziel "nicht angewendet"): Andere Presets verlieren den Anspruch.</summary>
    public void Drop(IEnumerable<string> tweakIds)
    {
        foreach (var id in tweakIds)
        {
            _owners.Remove(id);
        }
    }

    /// <summary>Gibt die Tweaks frei; zurueckgesetzt werden nur die, die danach niemand mehr will.</summary>
    public IReadOnlyList<string> Release(string preset)
    {
        var free = new List<string>();
        foreach (var (id, list) in _owners)
        {
            if (list.Remove(preset) && list.Count == 0)
            {
                free.Add(id);
            }
        }

        foreach (var id in free)
        {
            _owners.Remove(id);
        }

        return free.Order(StringComparer.Ordinal).ToList();
    }

    public bool Owns(string preset) => _owners.Values.Any(list => list.Contains(preset));
}
