namespace TweakPilot.Core;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

/// <summary>Ein Protokolleintrag (Abschnitt 10.1). Zeitstempel setzt die Senke.</summary>
public sealed record LogRecord
{
    public required string Event { get; init; }

    public LogLevel Level { get; init; } = LogLevel.Information;

    public string? TweakId { get; init; }

    public string? Target { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public string? Result { get; init; }

    public long? DurationMs { get; init; }

    public string? Error { get; init; }
}

/// <summary>Lokale Protokollsenke. Es gibt keinen Netzwerkversand.</summary>
public interface ILogSink
{
    void Write(LogRecord record);
}

public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    public void Write(LogRecord record)
    {
    }
}
