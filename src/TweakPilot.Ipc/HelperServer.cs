using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using TweakPilot.Core;

namespace TweakPilot.Ipc;

public interface IHelperHandler
{
    /// <summary>Fuehrt einen Whitelist-Befehl aus und liefert die Nutzdaten der Antwort.</summary>
    object? Handle(string command, JsonElement? payload);
}

public sealed class HelperServerOptions
{
    public required string PipeName { get; init; }

    public required SecurityIdentifier ClientUser { get; init; }

    public required IClientVerifier Verifier { get; init; }

    /// <summary>Der Helfer endet, wenn dieser Prozess endet (App oder CLI).</summary>
    public int? ParentProcessId { get; init; }

    public TimeSpan IdleTimeout { get; init; } = IpcProtocol.IdleTimeout;
}

/// <summary>
/// Named-Pipe-Server des Helfers. Eine Verbindung gleichzeitig, nur der startende Benutzer und SYSTEM duerfen die Pipe oeffnen.
/// Der Server endet nach Leerlauf, nach "Shutdown" oder wenn der startende Prozess endet.
/// </summary>
public sealed class HelperServer
{
    private readonly HelperServerOptions _options;
    private readonly IHelperHandler _handler;
    private readonly ILogSink _log;

    public HelperServer(HelperServerOptions options, IHelperHandler handler, ILogSink log)
    {
        _options = options;
        _handler = handler;
        _log = log;
    }

    public static PipeSecurity CreateSecurity(SecurityIdentifier clientUser)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(clientUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    public async Task RunAsync(CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var parentWatch = WatchParent(stop);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var pipe = NamedPipeServerStreamAcl.Create(
                    _options.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    CreateSecurity(_options.ClientUser));

                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    idle.CancelAfter(_options.IdleTimeout);
                    try
                    {
                        await pipe.WaitForConnectionAsync(idle.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _log.Write(new LogRecord { Event = "HelperIdleExit" });
                        return;
                    }
                }

                if (!_options.Verifier.Verify(pipe, out var reason))
                {
                    _log.Write(new LogRecord { Event = "ClientRejected", Level = LogLevel.Warning, Error = reason });
                    pipe.Disconnect();
                    continue;
                }

                _log.Write(new LogRecord { Event = "ClientConnected" });
                if (await ServeAsync(pipe, stop.Token).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await parentWatch.ConfigureAwait(false);
        }
    }

    /// <summary>Bedient einen Client. Liefert true, wenn der Helfer enden soll.</summary>
    private async Task<bool> ServeAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        long lastSequence = 0;
        while (true)
        {
            byte[]? frame;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                idle.CancelAfter(_options.IdleTimeout);
                try
                {
                    frame = await PipeFraming.ReadAsync(pipe, idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _log.Write(new LogRecord { Event = "HelperIdleExit" });
                    return true;
                }
                catch (Exception ex) when (ex is IpcProtocolException or IOException)
                {
                    _log.Write(new LogRecord { Event = "ProtocolError", Level = LogLevel.Warning, Error = ex.Message });
                    return false;
                }
            }

            if (frame is null)
            {
                return false;
            }

            IpcRequest? request = null;
            IpcResponse response;
            var close = false;
            var shutdown = false;
            try
            {
                request = JsonSerializer.Deserialize<IpcRequest>(frame, CoreJson.Options)
                    ?? throw new IpcProtocolException("Empty request.");
                if (request.Version != IpcProtocol.Version)
                {
                    throw new IpcProtocolException($"Protocol version {request.Version} is not supported.");
                }

                if (request.Sequence <= lastSequence)
                {
                    throw new IpcProtocolException("Counter not increasing (replay refused).");
                }

                lastSequence = request.Sequence;
                if (!IpcProtocol.Commands.Contains(request.Command))
                {
                    throw new IpcProtocolException($"Command {request.Command} is not allowed.");
                }

                shutdown = request.Command == IpcCommands.Shutdown;
                var result = shutdown ? null : _handler.Handle(request.Command, request.Payload);
                response = new IpcResponse
                {
                    Sequence = request.Sequence,
                    Ok = true,
                    Payload = result is null ? null : IpcProtocol.ToElement(result),
                };
            }
            catch (Exception ex) when (ex is IpcProtocolException or JsonException)
            {
                // Protokollverstoss: Antwort senden, danach Verbindung trennen.
                close = true;
                _log.Write(new LogRecord { Event = "ProtocolError", Level = LogLevel.Warning, Error = ex.Message });
                response = new IpcResponse { Sequence = request?.Sequence ?? 0, Ok = false, ErrorKind = ErrorKind.PermissionDenied, Error = ex.Message };
            }
            catch (TweakPilotException ex)
            {
                response = new IpcResponse { Sequence = request!.Sequence, Ok = false, ErrorKind = ex.Kind, Error = ex.Message };
            }
            catch (Exception ex)
            {
                _log.Write(new LogRecord { Event = "HandlerError", Level = LogLevel.Error, Error = ex.ToString() });
                response = new IpcResponse { Sequence = request!.Sequence, Ok = false, ErrorKind = ErrorKind.General, Error = ex.Message };
            }

            try
            {
                await PipeFraming.WriteAsync(pipe, IpcProtocol.Serialize(response), stop).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }

            if (shutdown)
            {
                _log.Write(new LogRecord { Event = "HelperShutdown" });
                return true;
            }

            if (close)
            {
                return false;
            }
        }
    }

    private Task WatchParent(CancellationTokenSource stop)
    {
        if (_options.ParentProcessId is not { } pid)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            try
            {
                using var parent = Process.GetProcessById(pid);
                await parent.WaitForExitAsync(stop.Token).ConfigureAwait(false);
                _log.Write(new LogRecord { Event = "ParentExited" });
            }
            catch (ArgumentException)
            {
                // Elternprozess existiert nicht mehr.
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await stop.CancelAsync().ConfigureAwait(false);
        });
    }
}
