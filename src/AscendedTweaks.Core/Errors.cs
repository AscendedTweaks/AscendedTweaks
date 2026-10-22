namespace AscendedTweaks.Core;

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

public sealed class AscendedTweaksException : Exception
{
    public AscendedTweaksException()
        : this(ErrorKind.General, "Unknown error.")
    {
    }

    public AscendedTweaksException(string message)
        : this(ErrorKind.General, message)
    {
    }

    public AscendedTweaksException(string message, Exception innerException)
        : this(ErrorKind.General, message, null, innerException)
    {
    }

    public AscendedTweaksException(ErrorKind kind, string message, string? tweakId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        TweakId = tweakId;
    }

    public ErrorKind Kind { get; }

    public string? TweakId { get; }
}
