namespace TweakPilot.Core;

/// <summary>Typisierte Fehlerarten (Abschnitt 10.2).</summary>
public enum ErrorKind
{
    General,
    PermissionDenied,
    NotSupportedOnBuild,
    ValueMismatchAfterWrite,
    BackupFailed,
    RollbackFailed,
    Cancelled,
    Unavailable,
    CatalogInvalid,
    StateCorrupt,
}

public sealed class TweakPilotException : Exception
{
    public TweakPilotException()
        : this(ErrorKind.General, "Unknown error.")
    {
    }

    public TweakPilotException(string message)
        : this(ErrorKind.General, message)
    {
    }

    public TweakPilotException(string message, Exception innerException)
        : this(ErrorKind.General, message, null, innerException)
    {
    }

    public TweakPilotException(ErrorKind kind, string message, string? tweakId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        TweakId = tweakId;
    }

    public ErrorKind Kind { get; }

    public string? TweakId { get; }
}
