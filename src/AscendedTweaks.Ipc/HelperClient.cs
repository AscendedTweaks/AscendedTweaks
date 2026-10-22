using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;

namespace AscendedTweaks.Ipc;

/// <summary>Privilegierte Operationen, die nur der Helfer ausfuehrt.</summary>
public interface IHelperChannel : IDisposable
{
    BatchResultDto ApplyTweaks(ApplyTweaksRequest request);

    RevertTweakResponse RevertTweak(RevertTweakRequest request);

    TweakStatus Capture(string tweakId);

    RestorePointResult CreateRestorePoint(string name);

    IReadOnlyList<string> RecoverInterrupted(string batchId, TweakScope scope);

    CleanupScan ScanCleanup(string categoryId);

    CleanupResult RunCleanup(string categoryId, CleanupMode mode);

    IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemoveApps(IReadOnlyList<string> names, bool deprovision);

    AscendedTweaks.Engine.Tools.MaintenanceResult RunMaintenance(AscendedTweaks.Engine.Tools.MaintenanceAction action, char? drive);

    IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> ReadDiskHealth();
}

public interface IHelperLauncher
{
    /// <summary>Startet den Helfer und liefert seine Prozess-ID.</summary>
    int Launch(string pipeName, SecurityIdentifier user);
}

/// <summary>Startet AscendedTweaks.Helper.exe ueber ShellExecute mit dem Verb "runas" (ein UAC-Dialog).</summary>
[ExcludeFromCodeCoverage(Justification = "Triggers a UAC prompt; testable manually only.")]
public sealed class ElevatedHelperLauncher : IHelperLauncher
{
    public const string HelperFileName = "AscendedTweaks.Helper.exe";
    private const int ErrorCancelled = 1223;

    /// <summary>Im Paketordner liegt der Helfer unter "app", im Entwicklungs- und Installer-Layout neben dem Programm.</summary>
    public string HelperPath { get; init; } = DefaultHelperPath(AppContext.BaseDirectory);

    /// <summary>
    /// SHA-256 des Helfers, beim Paketbau eingebettet (AssemblyMetadata "HelperSha256"). Ist er gesetzt, startet die App den Helfer nur,
    /// wenn die Datei genau diesen Inhalt hat: Ein portabler Ordner ist fuer den Benutzer beschreibbar (V2 Abschnitt 7.3).
    /// </summary>
    public string? ExpectedSha256 { get; init; } = EmbeddedHelperHash();

    public static string DefaultHelperPath(string baseDirectory)
    {
        var packaged = Path.Combine(baseDirectory, "app", HelperFileName);
        return File.Exists(packaged) ? packaged : Path.Combine(baseDirectory, HelperFileName);
    }

    /// <summary>Wert aus der Einstiegs-Assembly; leer oder fehlend bedeutet "nicht eingebettet" (Entwicklungs-Build).</summary>
    public static string? EmbeddedHelperHash() =>
        System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "HelperSha256")?.Value is { Length: 64 } hash ? hash : null;

    /// <summary>Prueft den eingebetteten Hash; wirft, wenn die Datei veraendert wurde.</summary>
    public static void VerifyHash(string path, string? expected)
    {
        if (expected is null)
        {
            return;
        }

        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new AscendedTweaksException(ErrorKind.General, "The helper was modified (checksum does not match). Please download Ascended Tweaks again; nothing was changed.");
        }
    }

    public int Launch(string pipeName, SecurityIdentifier user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!File.Exists(HelperPath))
        {
            throw new AscendedTweaksException(ErrorKind.General, $"Helper not found: {HelperPath}");
        }

        VerifyHash(HelperPath, ExpectedSha256);

        var info = new ProcessStartInfo(HelperPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = string.Create(CultureInfo.InvariantCulture, $"--pipe {pipeName} --parent {Environment.ProcessId} --client-sid {user.Value}"),
        };
        try
        {
            using var process = Process.Start(info)
                ?? throw new AscendedTweaksException(ErrorKind.General, "The helper could not be started.");
            return process.Id;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new AscendedTweaksException(ErrorKind.PermissionDenied, "The administrator prompt was declined. Nothing was changed.", innerException: ex);
        }
    }
}

/// <summary>
/// Verbindung zum Helfer. Startet ihn beim ersten privilegierten Schritt; bei Leerlauf-Ende wird er beim naechsten Schritt neu gestartet.
/// Der Client prueft, dass am anderen Ende wirklich der gestartete Helfer-Prozess sitzt.
/// </summary>
public sealed class HelperClient : IHelperChannel
{
    private readonly IHelperLauncher _launcher;
    private readonly TimeSpan _connectTimeout;
    private readonly SecurityIdentifier _user;
    private NamedPipeClientStream? _pipe;
    private long _sequence;

    public HelperClient(IHelperLauncher launcher, TimeSpan? connectTimeout = null)
    {
        _launcher = launcher;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(30);
        using var identity = WindowsIdentity.GetCurrent();
        _user = identity.User ?? throw new AscendedTweaksException(ErrorKind.General, "User SID cannot be determined.");
    }

    public int Launches { get; private set; }

    public PingResponse Ping() => Send<PingResponse>(IpcCommands.Ping, null);

    public BatchResultDto ApplyTweaks(ApplyTweaksRequest request) => Send<BatchResultDto>(IpcCommands.ApplyTweaks, request);

    public RevertTweakResponse RevertTweak(RevertTweakRequest request) => Send<RevertTweakResponse>(IpcCommands.RevertTweak, request);

    public TweakStatus Capture(string tweakId) => Send<TweakStatus>(IpcCommands.Capture, new TweakIdRequest { Id = tweakId });

    public RestorePointResult CreateRestorePoint(string name) => Send<RestorePointResult>(IpcCommands.CreateRestorePoint, new RestorePointRequest { Name = name });

    public IReadOnlyList<string> RecoverInterrupted(string batchId, TweakScope scope) =>
        Send<List<string>>(IpcCommands.RecoverInterrupted, new RecoverRequest { BatchId = batchId, Scope = scope });

    public CleanupScan ScanCleanup(string categoryId) =>
        Send<CleanupScan>(IpcCommands.ScanCleanup, new CleanupRequest { CategoryId = categoryId });

    public CleanupResult RunCleanup(string categoryId, CleanupMode mode) =>
        Send<CleanupResult>(IpcCommands.RunCleanup, new CleanupRequest { CategoryId = categoryId, Mode = mode });

    public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemoveApps(IReadOnlyList<string> names, bool deprovision) =>
        Send<List<AscendedTweaks.Engine.Debloat.RemovedApp>>(IpcCommands.RemoveApps, new RemoveAppsRequest { Names = names, Deprovision = deprovision });

    public AscendedTweaks.Engine.Tools.MaintenanceResult RunMaintenance(AscendedTweaks.Engine.Tools.MaintenanceAction action, char? drive) =>
        Send<AscendedTweaks.Engine.Tools.MaintenanceResult>(IpcCommands.RunMaintenance, new MaintenanceRequest { Action = action, Drive = drive });

    public IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> ReadDiskHealth() =>
        Send<List<AscendedTweaks.Engine.Tools.DiskHealth>>(IpcCommands.ReadDiskHealth, new { });

    public void Dispose()
    {
        if (_pipe is { IsConnected: true })
        {
            try
            {
                Exchange(IpcCommands.Shutdown, null);
            }
            catch (Exception ex) when (ex is IOException or AscendedTweaksException or IpcProtocolException or ObjectDisposedException)
            {
                // Helfer ist bereits beendet.
            }
        }

        _pipe?.Dispose();
        _pipe = null;
    }

    private T Send<T>(string command, object? payload)
    {
        var response = Exchange(command, payload);
        if (!response.Ok)
        {
            throw new AscendedTweaksException(response.ErrorKind ?? ErrorKind.General, response.Error ?? "The helper reported an error.");
        }

        return IpcProtocol.Deserialize<T>(response.Payload);
    }

    private IpcResponse Exchange(string command, object? payload)
    {
        var request = new IpcRequest
        {
            Version = IpcProtocol.Version,
            Sequence = ++_sequence,
            Command = command,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, payload.GetType(), CoreJson.Options),
        };
        var frame = IpcProtocol.Serialize(request);

        // Nur Schreibfehler duerfen zu einem Neustart des Helfers fuehren: dann wurde die Anfrage sicher nicht verarbeitet.
        try
        {
            WriteFrame(EnsureConnected(), frame);
        }
        catch (IOException)
        {
            _pipe?.Dispose();
            _pipe = null;
            try
            {
                WriteFrame(EnsureConnected(), frame);
            }
            catch (IOException ex)
            {
                // Auch der neu gestartete Helfer hat die Verbindung sofort getrennt: Client-Pruefung fehlgeschlagen.
                _pipe?.Dispose();
                _pipe = null;
                throw new AscendedTweaksException(ErrorKind.PermissionDenied, "The helper refused the connection.", innerException: ex);
            }
        }

        byte[]? answer;
        try
        {
            answer = PipeFraming.ReadAsync(_pipe!, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (IOException ex)
        {
            throw new AscendedTweaksException(ErrorKind.General, "Connection to the helper was lost. An open change will be detected on the next start.", innerException: ex);
        }

        if (answer is null)
        {
            _pipe?.Dispose();
            _pipe = null;
            throw new AscendedTweaksException(ErrorKind.PermissionDenied, "The helper refused the connection.");
        }

        var response = JsonSerializer.Deserialize<IpcResponse>(answer, CoreJson.Options)
            ?? throw new IpcProtocolException("Empty response.");
        if (response.Sequence != request.Sequence)
        {
            throw new IpcProtocolException("Response does not match the request.");
        }

        return response;
    }

    private static void WriteFrame(NamedPipeClientStream pipe, byte[] frame)
    {
        PipeFraming.WriteAsync(pipe, frame, CancellationToken.None).GetAwaiter().GetResult();
    }

    private NamedPipeClientStream EnsureConnected()
    {
        if (_pipe is { IsConnected: true })
        {
            return _pipe;
        }

        var name = IpcProtocol.NewPipeName(_user);
        var serverPid = _launcher.Launch(name, _user);
        Launches++;
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            pipe.Connect(_connectTimeout);
            if (!PipeNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) || actual != serverPid)
            {
                throw new AscendedTweaksException(ErrorKind.PermissionDenied, "The other end of the pipe is not the started helper. Connection closed.");
            }
        }
        catch (TimeoutException ex)
        {
            pipe.Dispose();
            throw new AscendedTweaksException(ErrorKind.General, "The helper did not respond.", innerException: ex);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        // Der Zaehler laeuft ueber Neustarts hinweg weiter; der Server verlangt nur "steigend".
        _pipe = pipe;
        return pipe;
    }
}
