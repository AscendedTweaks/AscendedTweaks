using System.Text.Json.Serialization;

namespace TweakPilot.Core;

/// <summary>Ein Tweak als Daten (Abschnitt 5.1). Texte liegen in den Sprachdateien, referenziert ueber Schluessel.</summary>
public sealed class TweakDefinition
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string NameKey { get; init; }

    public required string DescriptionKey { get; init; }

    public required string SideEffectsKey { get; init; }

    public required RiskLevel Risk { get; init; }

    public required EvidenceLevel Evidence { get; init; }

    public required IReadOnlyList<BuildRange> Builds { get; init; }

    public required IReadOnlyList<WindowsEdition> Editions { get; init; }

    public required TweakScope Scope { get; init; }

    public required bool RequiresAdmin { get; init; }

    public required bool RequiresReboot { get; init; }

    /// <summary>Explorer nach Rueckfrage neu starten, damit die Aenderung sichtbar wird.</summary>
    public bool RestartExplorer { get; init; }

    public required DetectCondition Detect { get; init; }

    public required IReadOnlyList<TweakAction> Apply { get; init; }

    public required RevertSpec Revert { get; init; }

    public required IReadOnlyList<string> Sources { get; init; }

    public IReadOnlyList<VerificationRecord> VerifiedOn { get; init; } = [];

    public bool HiddenByDefault { get; init; }

    /// <summary>Nur auf Geraeten ohne Akku (Energie-Tweaks, die auf Notebooks Akkulaufzeit kosten); sonst "nicht verfuegbar".</summary>
    public bool DesktopOnly { get; init; }

    /// <summary>Wirkung fuer den Nutzer; im Katalog Pflicht (der Linter meldet fehlende Angaben).</summary>
    public ImpactLevel? Impact { get; init; }

    /// <summary>Textschluessel mit einer Zeile Begruendung der Wirkung (tweak.ID.impact).</summary>
    [JsonPropertyName("impactNote")]
    public string? ImpactNoteKey { get; init; }

    /// <summary>Messbelege (Mess-Skript-Ergebnis oder Microsoft-Beleg fuer das Verhalten); Pflicht fuer "Spuerbar".</summary>
    public IReadOnlyList<string> MeasuredOn { get; init; } = [];

    /// <summary>Nutzen-Titel fuer den Einfach-Modus, hoechstens 5 Woerter (tweak.ID.plain.title).</summary>
    public string? PlainTitleKey { get; init; }

    /// <summary>Nutzen-Satz fuer den Einfach-Modus, hoechstens 90 Zeichen (tweak.ID.plain.desc).</summary>
    public string? PlainDescKey { get; init; }

    [JsonIgnore]
    public string Module => Id[..Id.IndexOf('-', StringComparison.Ordinal)];

    public bool SupportsBuild(int build) => Builds.Any(b => b.Contains(build));
}

public sealed record BuildRange
{
    public required int Min { get; init; }

    public int? Max { get; init; }

    public bool Contains(int build) => build >= Min && (Max is null || build <= Max);
}

public sealed record VerificationRecord
{
    public required int Build { get; init; }

    public required WindowsEdition Edition { get; init; }

    public required DateOnly Date { get; init; }

    public required string Method { get; init; }
}

/// <summary>Rueckgaengig: Standard ist Wiederherstellung aus der Sicherung, alternativ explizite Gegenaktionen.</summary>
public sealed class RevertSpec
{
    public static readonly RevertSpec RestoreFromBackup = new(null);

    public RevertSpec(IReadOnlyList<TweakAction>? actions)
    {
        Actions = actions;
    }

    public IReadOnlyList<TweakAction>? Actions { get; }

    public bool IsRestoreFromBackup => Actions is null;
}
