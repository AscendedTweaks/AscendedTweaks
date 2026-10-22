namespace AscendedTweaks.Engine.State;

public enum BatchStatus
{
    Pending,
    Applying,
    Committed,
    RolledBack,
    RollbackFailed,
}

public enum TweakJournalStatus
{
    Pending,
    Applying,
    Applied,
    RolledBack,
}

/// <summary>Write-Ahead-Journal: jeder Schritt wird vor bzw. nach der Ausfuehrung festgehalten.</summary>
public sealed class JournalDocument
{
    public int Version { get; set; } = 1;

    public List<JournalBatch> Batches { get; set; } = [];
}

public sealed class JournalBatch
{
    public required string BatchId { get; set; }

    public required string BackupSetId { get; set; }

    public required DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? FinishedUtc { get; set; }

    public BatchStatus Status { get; set; }

    public List<JournalTweak> Tweaks { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class JournalTweak
{
    public required string Id { get; set; }

    public TweakJournalStatus Status { get; set; }
}

/// <summary>Merkt pro angewendetem Tweak den Zustand vor dem Anwenden und den geschriebenen Zustand (Abschnitt 7.4).</summary>
public sealed class LedgerDocument
{
    public int Version { get; set; } = 1;

    public List<LedgerEntry> Entries { get; set; } = [];
}

public sealed class LedgerEntry
{
    public required string TweakId { get; set; }

    public required string BatchId { get; set; }

    public required string BackupSetId { get; set; }

    public required DateTimeOffset AppliedUtc { get; set; }

    public List<ActionCapture> Captures { get; set; } = [];
}

public sealed class BackupManifest
{
    public required string AppVersion { get; set; }

    public required int WindowsBuild { get; set; }

    public required int WindowsUbr { get; set; }

    public required DateTimeOffset CreatedUtc { get; set; }

    public required string BatchId { get; set; }

    public required List<string> TweakIds { get; set; }
}

public sealed class BackupEntry
{
    public required string TweakId { get; set; }

    public required int ActionIndex { get; set; }

    public required ActionCapture Capture { get; set; }
}

public sealed class BaselineInfo
{
    public required string BackupSetId { get; set; }
}

public sealed record BackupSetInfo
{
    public required string Id { get; init; }

    public required string Path { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required IReadOnlyList<string> TweakIds { get; init; }

    public required bool IsBaseline { get; init; }

    public required long SizeBytes { get; init; }

    public required bool Readable { get; init; }
}
