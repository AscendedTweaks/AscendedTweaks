using System.Buffers.Binary;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using TweakPilot.Core;
using TweakPilot.Engine;

namespace TweakPilot.Ipc;

/// <summary>Vertrag zwischen Oberflaeche/CLI und Helfer (Abschnitt 9.3).</summary>
public static partial class IpcProtocol
{
    public const int Version = 1;

    public const int MaxMessageBytes = 1024 * 1024;

    public const int MaxTweaksPerRequest = 500;

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Einzige Befehle, die der Helfer annimmt.</summary>
    public static readonly IReadOnlySet<string> Commands = new HashSet<string>(StringComparer.Ordinal)
    {
        IpcCommands.Ping,
        IpcCommands.ApplyTweaks,
        IpcCommands.RevertTweak,
        IpcCommands.Capture,
        IpcCommands.CreateRestorePoint,
        IpcCommands.RecoverInterrupted,
        IpcCommands.ScanCleanup,
        IpcCommands.RunCleanup,
        IpcCommands.RemoveApps,
        IpcCommands.RunMaintenance,
        IpcCommands.ReadDiskHealth,
        IpcCommands.Shutdown,
    };

    public static string NewPipeName(SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return $"TweakPilot.{user.Value}.{Guid.NewGuid():N}";
    }

    public static bool IsValidPipeName(string name, SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var match = PipeNamePattern().Match(name);
        return match.Success && string.Equals(match.Groups["sid"].Value, user.Value, StringComparison.Ordinal);
    }

    /// <summary>Katalog-ID (MODUL-NNN) oder eine aus der ID erzeugte ID (PERF-026 Autostart, GAME-007/008/016 pro Spiel, NET-004, UPD-001/007; streng geprueft).</summary>
    public static bool IsValidTweakId(string id) =>
        TweakIdPattern().IsMatch(id) || AutostartTweaks.TryParse(id, out _, out _) || GameTweaks.TryCreate(id, out _) || CustomDnsTweaks.TryCreate(id, out _) || UpdateTweaks.TryCreate(id, out _);

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, CoreJson.Options);

    public static T Deserialize<T>(JsonElement? element)
    {
        if (element is not { } e)
        {
            throw new IpcProtocolException("Nutzdaten fehlen.");
        }

        return e.Deserialize<T>(CoreJson.Options) ?? throw new IpcProtocolException("Nutzdaten leer.");
    }

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, CoreJson.Options);

    [GeneratedRegex(@"^TweakPilot\.(?<sid>S-1-[0-9]+(-[0-9]+)+)\.[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex PipeNamePattern();

    [GeneratedRegex(@"^[A-Z]{2,5}-[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex TweakIdPattern();
}

public static class IpcCommands
{
    public const string Ping = "Ping";
    public const string ApplyTweaks = "ApplyTweaks";
    public const string RevertTweak = "RevertTweak";
    public const string Capture = "Capture";
    public const string CreateRestorePoint = "CreateRestorePoint";
    public const string RecoverInterrupted = "RecoverInterrupted";
    public const string ScanCleanup = "ScanCleanup";
    public const string RunCleanup = "RunCleanup";
    public const string RemoveApps = "RemoveApps";
    public const string RunMaintenance = "RunMaintenance";
    public const string ReadDiskHealth = "ReadDiskHealth";
    public const string Shutdown = "Shutdown";
}

public sealed class IpcProtocolException : Exception
{
    public IpcProtocolException()
    {
    }

    public IpcProtocolException(string message)
        : base(message)
    {
    }

    public IpcProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class IpcRequest
{
    public required int Version { get; init; }

    /// <summary>Streng steigender Zaehler gegen Wiederholung.</summary>
    public required long Sequence { get; init; }

    public required string Command { get; init; }

    public JsonElement? Payload { get; init; }
}

public sealed class IpcResponse
{
    public required long Sequence { get; init; }

    public required bool Ok { get; init; }

    public ErrorKind? ErrorKind { get; init; }

    public string? Error { get; init; }

    public JsonElement? Payload { get; init; }
}

public sealed class ApplyTweaksRequest
{
    public required List<string> Ids { get; init; }

    public bool DryRun { get; init; }

    /// <summary>Der Aufrufer hat den Wiederherstellungspunkt bereits ueber CreateRestorePoint geklaert.</summary>
    public bool RestorePointHandled { get; init; }

    public bool ProceedWithoutRestorePoint { get; init; }
}

public sealed class RevertTweakRequest
{
    public required string Id { get; init; }

    /// <summary>Entscheidung je Sicherungsindex. Fehlt eine, meldet der Helfer den Konflikt zurueck und aendert nichts.</summary>
    public Dictionary<int, ConflictDecision> Decisions { get; init; } = [];
}

public sealed class RevertTweakResponse
{
    public required RevertOutcome Outcome { get; init; }

    public ErrorKind? ErrorKind { get; init; }

    public string? Error { get; init; }

    public List<RevertConflict> PendingConflicts { get; init; } = [];
}

public sealed class TweakIdRequest
{
    public required string Id { get; init; }
}

public sealed class RestorePointRequest
{
    public required string Name { get; init; }
}

/// <summary>Bereinigung im Helfer: nur Kategorie und Modus, nie Pfade. Der Helfer scannt selbst neu (Abschnitt 9.3, 9.4).</summary>
public sealed class CleanupRequest
{
    public required string CategoryId { get; init; }

    public CleanupMode Mode { get; init; } = CleanupMode.Permanent;
}

/// <summary>Apps fuer alle Benutzer entfernen: nur Paketnamen; der Helfer prueft Sperrliste und Herkunft selbst.</summary>
public sealed class RemoveAppsRequest
{
    public const int MaxApps = 60;

    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>Auch das Bereitstellungspaket entfernen (neue Benutzerkonten); Standard aus (Abschnitt 6.6).</summary>
    public bool Deprovision { get; init; }
}

/// <summary>Feste Wartungsaktion; Laufwerk nur als Grossbuchstabe.</summary>
public sealed class MaintenanceRequest
{
    public required TweakPilot.Engine.Tools.MaintenanceAction Action { get; init; }

    public char? Drive { get; init; }
}

public sealed class RecoverRequest
{
    public required string BatchId { get; init; }

    /// <summary>Machine oder UserPolicy; bestimmt den Zustandsspeicher im Helfer.</summary>
    public TweakScope Scope { get; init; } = TweakScope.Machine;
}

public sealed class PingResponse
{
    public required int ProcessId { get; init; }

    public required bool Elevated { get; init; }
}

/// <summary>Uebertragbare Form von <see cref="BatchResult"/>.</summary>
public sealed class BatchResultDto
{
    public required BatchOutcome Outcome { get; init; }

    public List<string> ReadyIds { get; init; } = [];

    public List<PreflightEntry> Excluded { get; init; } = [];

    public List<PlanItem> Items { get; init; } = [];

    public string? BatchId { get; init; }

    public string? BackupSetPath { get; init; }

    public RestorePointResult? RestorePoint { get; init; }

    public List<string> AppliedTweaks { get; init; } = [];

    public string? FailedTweakId { get; init; }

    public ErrorKind? ErrorKind { get; init; }

    public string? Error { get; init; }

    public List<string> RollbackErrors { get; init; } = [];

    public bool RebootRequired { get; init; }

    public static BatchResultDto From(BatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new BatchResultDto
        {
            Outcome = result.Outcome,
            ReadyIds = result.Plan.Ready.Select(t => t.Id).ToList(),
            Excluded = [.. result.Plan.Excluded],
            Items = [.. result.Plan.Items],
            BatchId = result.BatchId,
            BackupSetPath = result.BackupSetPath,
            RestorePoint = result.RestorePoint,
            AppliedTweaks = [.. result.AppliedTweaks],
            FailedTweakId = result.FailedTweakId,
            ErrorKind = result.Error?.Kind,
            Error = result.Error?.Message,
            RollbackErrors = [.. result.RollbackErrors],
            RebootRequired = result.RebootRequired,
        };
    }

    public BatchResult ToResult(ITweakCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var ready = ReadyIds.Select(id => catalog.TryGet(id, out var t) ? t : throw new IpcProtocolException($"Unknown ID {id} in response.")).ToList();
        return new BatchResult
        {
            Outcome = Outcome,
            Plan = new BatchPlan { Ready = ready, Excluded = Excluded, Items = Items },
            BatchId = BatchId,
            BackupSetPath = BackupSetPath,
            RestorePoint = RestorePoint,
            AppliedTweaks = AppliedTweaks,
            FailedTweakId = FailedTweakId,
            Error = ErrorKind is { } kind ? new TweakPilotException(kind, Error ?? string.Empty, FailedTweakId) : null,
            RollbackErrors = RollbackErrors,
            RebootRequired = RebootRequired,
        };
    }
}

/// <summary>Nachrichtenrahmen: 4 Byte Laenge (little endian), dann JSON. Hoechstens 1 MB.</summary>
public static class PipeFraming
{
    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0 || payload.Length > IpcProtocol.MaxMessageBytes)
        {
            throw new IpcProtocolException($"Message has an invalid size {payload.Length}.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    /// <summary>Liefert null, wenn die Gegenseite die Verbindung sauber geschlossen hat.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        var read = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, cancellation).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < 4)
        {
            throw new IpcProtocolException("Message header incomplete.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > IpcProtocol.MaxMessageBytes)
        {
            throw new IpcProtocolException($"Message has an invalid size {length}.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        return payload;
    }
}
