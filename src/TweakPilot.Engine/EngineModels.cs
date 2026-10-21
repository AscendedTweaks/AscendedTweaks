using TweakPilot.Core;

namespace TweakPilot.Engine;

/// <summary>
/// Technische Meldungen der Engine. Nutzertexte der Oberflaeche kommen ab Phase 3 aus den Sprachdateien;
/// diese Texte erscheinen in Protokoll, CLI und Fehlerdetails.
/// </summary>
internal static class EngineText
{
    public const string Missing = "(not present)";
}

/// <summary>Eine Zeile der Vorschau (Dry-Run, Regel S3).</summary>
public sealed record PlanItem
{
    public required string TweakId { get; init; }

    public required string ActionType { get; init; }

    /// <summary>Registry-Pfad, Dienstname, Energieplan oder Befehl.</summary>
    public required string Target { get; init; }

    public string? ValueName { get; init; }

    public string? ValueKind { get; init; }

    public required string Current { get; init; }

    public required string New { get; init; }
}

/// <summary>Lesbare Sicht auf eine Sicherung: aktueller, urspruenglicher und von der App geschriebener Zustand.</summary>
public sealed record CaptureView(string Target, string Current, string Original, string Written);

public sealed record DetectResult(TweakState State, string? Reason)
{
    public static DetectResult Of(TweakState state) => new(state, null);

    /// <summary>Nicht lesbar, weil Administratorrechte fehlen; im Helfer wird der Zustand echt geprueft.</summary>
    public bool NeedsElevation { get; init; }
}

public enum PreflightOutcome
{
    Ready,
    AlreadyApplied,
    Unknown,
    ReadOnlyMode,
    ExpertLockedOnUntestedBuild,
    NotSupportedOnBuild,
    EditionNotSupported,
    AdminRequired,
    Unavailable,
    Conflict,
}

public sealed record PreflightEntry(string TweakId, PreflightOutcome Outcome, string Reason);

public sealed record BatchPlan
{
    public required IReadOnlyList<TweakDefinition> Ready { get; init; }

    public required IReadOnlyList<PreflightEntry> Excluded { get; init; }

    public required IReadOnlyList<PlanItem> Items { get; init; }

    public bool RequiresReboot => Ready.Any(t => t.RequiresReboot);
}

public enum RestorePointStatus
{
    Created,
    AlreadyExistsWithin24Hours,
    ProtectionDisabled,
    Failed,
    NotAttempted,

    /// <summary>Der Aufrufer hat den Wiederherstellungspunkt fuer diesen Batch schon erstellt oder bestaetigt.</summary>
    HandledSeparately,
}

public sealed record RestorePointResult(RestorePointStatus Status, string Message)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAcceptable => Status is RestorePointStatus.Created or RestorePointStatus.AlreadyExistsWithin24Hours or RestorePointStatus.HandledSeparately;
}

/// <summary>Gibt ein vorab ermitteltes Ergebnis zurueck (Wiederherstellungspunkt einmal je Gesamt-Batch).</summary>
public sealed class PresetRestorePointService : IRestorePointService
{
    private readonly RestorePointResult _result;

    public PresetRestorePointService(RestorePointResult result) => _result = result;

    public RestorePointResult TryCreate(string description) => _result;
}

/// <summary>Wiederherstellungspunkt (best effort, Regel S1a).</summary>
public interface IRestorePointService
{
    RestorePointResult TryCreate(string description);
}

/// <summary>
/// Kein Wiederherstellungspunkt: fuer reine Benutzer-Tweaks (ohne Admin nicht moeglich) und fuer die Vorschau.
/// Benutzer-Tweaks sind ueber die eigene Sicherung vollstaendig umkehrbar.
/// </summary>
public sealed class NoRestorePointService : IRestorePointService
{
    public RestorePointResult TryCreate(string description)
    {
        return new RestorePointResult(RestorePointStatus.NotAttempted, "No restore point is created for user settings (it needs administrator rights). TweakPilot creates its own backup.");
    }
}

public sealed class ApplyOptions
{
    public bool DryRun { get; init; }

    /// <summary>Rueckfrage, wenn kein Wiederherstellungspunkt erstellt werden konnte. Ohne Rueckfrage wird abgebrochen.</summary>
    public Func<RestorePointResult, bool>? ConfirmWithoutRestorePoint { get; init; }

    public IProgress<BatchProgress>? Progress { get; init; }

    public CancellationToken Cancellation { get; init; }
}

public sealed record BatchProgress(int Index, int Total, string TweakId);

public enum BatchOutcome
{
    DryRun,
    NothingToDo,
    Aborted,

    /// <summary>Ein unterbrochener Batch muss zuerst zurueckgenommen werden.</summary>
    BlockedByInterruptedBatch,
    BackupFailed,
    Committed,
    RolledBack,
    RollbackFailed,
}

public sealed record BatchResult
{
    public required BatchOutcome Outcome { get; init; }

    public required BatchPlan Plan { get; init; }

    public string? BatchId { get; init; }

    public string? BackupSetPath { get; init; }

    public RestorePointResult? RestorePoint { get; init; }

    public IReadOnlyList<string> AppliedTweaks { get; init; } = [];

    public string? FailedTweakId { get; init; }

    public TweakPilotException? Error { get; init; }

    public IReadOnlyList<string> RollbackErrors { get; init; } = [];

    public bool RebootRequired { get; init; }
}

public enum ConflictDecision
{
    RestoreOriginal,
    KeepCurrent,
    Cancel,
}

public sealed record RevertConflict(string TweakId, int CaptureIndex, CaptureView View);

public delegate ConflictDecision ConflictResolver(RevertConflict conflict);

public enum RevertOutcome
{
    Reverted,
    NotApplied,
    Cancelled,
    Failed,
}

public sealed record RevertResult(string TweakId, RevertOutcome Outcome, TweakPilotException? Error = null);

public sealed record TweakStatus(string TweakId, TweakState State, string? Reason, bool TrackedInLedger);

public sealed record BaselineResult(string BackupSetId, int CapturedPlaces, IReadOnlyList<string> Skipped, bool AlreadyExisted);

public sealed record BaselineDeviation(string TweakId, CaptureView View);
