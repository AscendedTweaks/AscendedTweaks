using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using TweakPilot.Core;
using Xunit;

namespace TweakPilot.Ipc.Tests;

/// <summary>Server im eigenen Prozess; "Launch" startet nur die Server-Schleife.</summary>
internal sealed class InProcessLauncher : IHelperLauncher
{
    private readonly Func<string, SecurityIdentifier, HelperServerOptions> _options;
    private readonly IHelperHandler _handler;

    public InProcessLauncher(IHelperHandler handler, Func<string, SecurityIdentifier, HelperServerOptions> options)
    {
        _handler = handler;
        _options = options;
    }

    public int ReportedProcessId { get; init; } = Environment.ProcessId;

    public List<Task> Servers { get; } = [];

    public int Launch(string pipeName, SecurityIdentifier user)
    {
        Servers.Add(Task.Run(() => new HelperServer(_options(pipeName, user), _handler, NullLogSink.Instance).RunAsync(CancellationToken.None)));
        return ReportedProcessId;
    }
}

internal sealed class EchoHandler : IHelperHandler
{
    public List<string> Commands { get; } = [];

    public object? Handle(string command, JsonElement? payload)
    {
        Commands.Add(command);
        return command == IpcCommands.Ping
            ? new PingResponse { ProcessId = Environment.ProcessId, Elevated = false }
            : throw new TweakPilotException(ErrorKind.Unavailable, "Testfehler");
    }
}

public sealed class PipeTests
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;
    private static readonly string MyDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;

    [Fact]
    public async Task Framing_roundtrip_and_limits()
    {
        using var stream = new MemoryStream();
        await PipeFraming.WriteAsync(stream, [1, 2, 3], CancellationToken.None);
        stream.Position = 0;
        (await PipeFraming.ReadAsync(stream, CancellationToken.None)).Should().Equal(1, 2, 3);
        (await PipeFraming.ReadAsync(stream, CancellationToken.None)).Should().BeNull();

        var tooLarge = () => PipeFraming.WriteAsync(new MemoryStream(), new byte[IpcProtocol.MaxMessageBytes + 1], CancellationToken.None);
        await tooLarge.Should().ThrowAsync<IpcProtocolException>();
        var empty = () => PipeFraming.WriteAsync(new MemoryStream(), [], CancellationToken.None);
        await empty.Should().ThrowAsync<IpcProtocolException>();

        foreach (var length in new[] { 0, -1, IpcProtocol.MaxMessageBytes + 1 })
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, length);
            var bad = () => PipeFraming.ReadAsync(new MemoryStream(header), CancellationToken.None);
            await bad.Should().ThrowAsync<IpcProtocolException>();
        }

        var truncated = () => PipeFraming.ReadAsync(new MemoryStream([1, 0]), CancellationToken.None);
        await truncated.Should().ThrowAsync<IpcProtocolException>();
    }

    [Fact]
    public void Pipe_names_are_bound_to_the_user()
    {
        var name = IpcProtocol.NewPipeName(Me);
        IpcProtocol.IsValidPipeName(name, Me).Should().BeTrue();
        IpcProtocol.IsValidPipeName(name, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)).Should().BeFalse();
        IpcProtocol.IsValidPipeName("TweakPilot.S-1-5-18.x", Me).Should().BeFalse();
        IpcProtocol.IsValidTweakId("PRIV-001").Should().BeTrue();
        IpcProtocol.IsValidTweakId("PRIV-001;x").Should().BeFalse();
        IpcProtocol.IsValidTweakId("PERF-026/HKLM-RUN/Tool").Should().BeTrue();
        IpcProtocol.IsValidTweakId("PERF-026/ANDERS/Tool").Should().BeFalse();
        IpcProtocol.IsValidTweakId("PERF-026/HKLM-RUN/" + new string('x', 300)).Should().BeFalse();
        IpcProtocol.IsValidTweakId("GAME-016/spiel.exe").Should().BeTrue();
        IpcProtocol.IsValidTweakId("GAME-016/spiel.exe\\Debugger").Should().BeFalse();
        IpcProtocol.IsValidTweakId("UPD-007/11/25H2").Should().BeTrue();
        IpcProtocol.IsValidTweakId("UPD-001/2099-01-01/35").Should().BeFalse("Pause mit Startdatum in der Zukunft");
        IpcProtocol.IsValidTweakId("UPD-001/" + DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "/36").Should().BeFalse("hoechstens 35 Tage");
    }

    [Fact]
    public void Pipe_acl_allows_only_user_and_system()
    {
        var name = IpcProtocol.NewPipeName(Me);
        using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, HelperServer.CreateSecurity(Me));

        var rules = pipe.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

        rules.Select(r => r.IdentityReference).Should().BeEquivalentTo(new IdentityReference[]
        {
            Me,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        });
        rules.Should().OnlyContain(r => r.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public void Verified_client_can_ping_and_receives_typed_errors()
    {
        var handler = new EchoHandler();
        var launcher = new InProcessLauncher(handler, Options(Environment.ProcessId, MyDirectory));
        using var client = new HelperClient(launcher, TimeSpan.FromSeconds(10));

        client.Ping().ProcessId.Should().Be(Environment.ProcessId);
        client.Ping().Elevated.Should().BeFalse();
        var failing = () => client.Capture("PRIV-001");

        failing.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.Unavailable);
        client.Launches.Should().Be(1);
        handler.Commands.Should().Equal(IpcCommands.Ping, IpcCommands.Ping, IpcCommands.Capture);
    }

    [Fact]
    public void Client_from_unexpected_process_is_rejected()
    {
        var handler = new EchoHandler();
        var launcher = new InProcessLauncher(handler, Options(Environment.ProcessId + 100000, MyDirectory));
        using var client = new HelperClient(launcher, TimeSpan.FromSeconds(10));

        var act = () => client.Ping();

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        handler.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Client_outside_install_directory_is_rejected()
    {
        var handler = new EchoHandler();
        var launcher = new InProcessLauncher(handler, Options(Environment.ProcessId, Path.GetTempPath()));
        using var client = new HelperClient(launcher, TimeSpan.FromSeconds(10));

        var act = () => client.Ping();

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        handler.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Client_refuses_a_server_that_is_not_the_launched_helper()
    {
        var launcher = new InProcessLauncher(new EchoHandler(), Options(Environment.ProcessId, MyDirectory)) { ReportedProcessId = 4 };
        using var client = new HelperClient(launcher, TimeSpan.FromSeconds(10));

        var act = () => client.Ping();

        act.Should().Throw<TweakPilotException>().WithMessage("*not the started helper*");
    }

    [Fact]
    public async Task Server_rejects_protocol_violations_and_closes()
    {
        var name = IpcProtocol.NewPipeName(Me);
        var handler = new EchoHandler();
        var server = Task.Run(() => new HelperServer(Options(Environment.ProcessId, MyDirectory)(name, Me), handler, NullLogSink.Instance).RunAsync(CancellationToken.None));

        (await Send(name, Raw(1, 1, IpcCommands.Ping), Raw(1, 1, IpcCommands.Ping))).Should().Equal("ok", "error:replay", "closed");
        (await Send(name, Raw(2, 1, IpcCommands.Ping))).Should().Equal("error:Protocol version", "closed");
        (await Send(name, Encoding.UTF8.GetBytes("{ kaputt"))).Should().Equal("error:", "closed");
        (await Send(name, Raw(1, 1, "RunScript"))).Should().Equal("error:is not allowed", "closed");
        (await Send(name, Raw(1, 1, IpcCommands.Shutdown))).Should().Equal("ok", "closed");

        await server.WaitAsync(TimeSpan.FromSeconds(10));
        handler.Commands.Should().Equal(IpcCommands.Ping);
    }

    [Fact]
    public async Task Oversized_frame_closes_connection()
    {
        var name = IpcProtocol.NewPipeName(Me);
        var options = Options(Environment.ProcessId, MyDirectory)(name, Me);
        var server = Task.Run(() => new HelperServer(options, new EchoHandler(), NullLogSink.Instance).RunAsync(CancellationToken.None));
        using (var pipe = Connect(name))
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, IpcProtocol.MaxMessageBytes + 1);
            await pipe.WriteAsync(header);
            (await PipeFraming.ReadAsync(pipe, CancellationToken.None)).Should().BeNull();
        }

        (await Send(name, Raw(1, 1, IpcCommands.Shutdown))).Should().Equal("ok", "closed");
        await server.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Server_ends_after_idle_timeout()
    {
        var options = Options(Environment.ProcessId, MyDirectory, TimeSpan.FromMilliseconds(200))(IpcProtocol.NewPipeName(Me), Me);

        var run = new HelperServer(options, new EchoHandler(), NullLogSink.Instance).RunAsync(CancellationToken.None);

        await run.WaitAsync(TimeSpan.FromSeconds(10));
        run.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Server_ends_when_parent_process_is_gone()
    {
        using var parent = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        await parent.WaitForExitAsync();
        var options = new HelperServerOptions
        {
            PipeName = IpcProtocol.NewPipeName(Me),
            ClientUser = Me,
            Verifier = Verifier(Environment.ProcessId, MyDirectory),
            ParentProcessId = parent.Id,
            IdleTimeout = TimeSpan.FromMinutes(5),
        };

        var run = new HelperServer(options, new EchoHandler(), NullLogSink.Instance).RunAsync(CancellationToken.None);

        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Directory_check_is_exact()
    {
        WindowsClientVerifier.IsInDirectory(@"C:\App\tool.exe", @"C:\App\").Should().BeTrue();
        WindowsClientVerifier.IsInDirectory(@"C:\App\sub\tool.exe", @"C:\App").Should().BeFalse();
        WindowsClientVerifier.IsInDirectory(@"C:\AppEvil\tool.exe", @"C:\App").Should().BeFalse();

        // V2 Paketordner: App oben, Helfer in "app". Erlaubt sind genau diese beiden Ordner, nie weitere Unterordner.
        WindowsClientVerifier.DirectoriesFor(@"C:\Users\a\Desktop\TweakPilot\app\").Should().Equal(@"C:\Users\a\Desktop\TweakPilot\app", @"C:\Users\a\Desktop\TweakPilot");
        WindowsClientVerifier.DirectoriesFor(@"C:\Program Files\TweakPilot").Should().Equal(@"C:\Program Files\TweakPilot");
        WindowsClientVerifier.DirectoriesFor(@"C:\x\application").Should().ContainSingle();
    }

    private static Func<string, SecurityIdentifier, HelperServerOptions> Options(int expectedPid, string directory, TimeSpan? idle = null)
    {
        return (name, user) => new HelperServerOptions
        {
            PipeName = name,
            ClientUser = user,
            Verifier = Verifier(expectedPid, directory),
            IdleTimeout = idle ?? TimeSpan.FromSeconds(20),
        };
    }

    private static WindowsClientVerifier Verifier(int expectedPid, string directory)
    {
        return new WindowsClientVerifier(new ClientVerifierOptions { ExpectedProcessId = expectedPid, AllowedDirectories = [directory], ExpectedUser = Me });
    }

    private static byte[] Raw(int version, long sequence, string command)
    {
        return IpcProtocol.Serialize(new IpcRequest { Version = version, Sequence = sequence, Command = command });
    }

    private static NamedPipeClientStream Connect(string name)
    {
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        pipe.Connect(10000);
        return pipe;
    }

    /// <summary>Sendet Rahmen und protokolliert die Antworten bis zum Verbindungsende.</summary>
    private static async Task<List<string>> Send(string name, params byte[][] frames)
    {
        var results = new List<string>();
        using var pipe = Connect(name);
        foreach (var frame in frames)
        {
            try
            {
                await PipeFraming.WriteAsync(pipe, frame, CancellationToken.None);
            }
            catch (IOException)
            {
                break;
            }

            var answer = await PipeFraming.ReadAsync(pipe, CancellationToken.None);
            if (answer is null)
            {
                break;
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(answer, CoreJson.Options)!;
            results.Add(response.Ok ? "ok" : "error:" + Keyword(response.Error));
        }

        results.Add((await PipeFraming.ReadAsync(pipe, CancellationToken.None)) is null ? "closed" : "open");
        return results;
    }

    private static string Keyword(string? error)
    {
        foreach (var keyword in new[] { "replay", "Protocol version", "is not allowed" })
        {
            if (error?.Contains(keyword, StringComparison.Ordinal) == true)
            {
                return keyword;
            }
        }

        return string.Empty;
    }
}
