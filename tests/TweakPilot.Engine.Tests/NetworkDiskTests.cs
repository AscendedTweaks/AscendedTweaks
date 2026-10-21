using System.Net;
using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.Cleanup;
using TweakPilot.Engine.Tools;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class NetworkDiskTests : IDisposable
{
    private const string Eth = "{BC824789-5EB3-4BAF-8C5F-1A638BC37EBC}";
    private const string Wifi = "{11111111-2222-3333-4444-555555555555}";
    private readonly Harness _h = new();
    private readonly FakeNetwork _net = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("1.1.1.1,1.0.0.1", true)]
    [InlineData(DnsServers.Cloudflare, true)]
    [InlineData("dhcp", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("fe80::1%12", false)]
    [InlineData("1.1.1.1;rm -rf", false)]
    [InlineData("1.1.1", false)]
    [InlineData("01.1.1.1", false)]
    [InlineData("{BC824789-5EB3-4BAF-8C5F-1A638BC37EBC}=dhcp;{11111111-2222-3333-4444-555555555555}=9.9.9.9", true)]
    [InlineData("{kaputt}=dhcp", false)]
    public void Dns_states_are_validated_strictly(string state, bool valid)
    {
        AdapterState.IsValidDnsState(state).Should().Be(valid);
    }

    [Fact]
    public void Private_addresses_are_recognized_for_confirmation()
    {
        DnsServers.IsPrivate(IPAddress.Parse("192.168.178.1")).Should().BeTrue();
        DnsServers.IsPrivate(IPAddress.Parse("10.0.0.53")).Should().BeTrue();
        DnsServers.IsPrivate(IPAddress.Parse("fd00::1")).Should().BeTrue();
        DnsServers.IsPrivate(IPAddress.Parse("1.1.1.1")).Should().BeFalse();
    }

    [Fact]
    public void Dns_tweak_saves_each_adapter_and_restores_exactly()
    {
        _net.Dns[Eth] = ["192.168.178.1"];
        _net.Dns[Wifi] = [];
        var engine = Engine(Dns("NET-001", DnsServers.Cloudflare));

        engine.GetStatus("NET-001").State.Should().Be(TweakState.NotApplied);
        engine.ApplyBatch(["NET-001"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _net.Dns[Eth].Should().Equal(DnsServers.Cloudflare.Split(','));
        _net.Dns[Wifi].Should().Equal(DnsServers.Cloudflare.Split(','));
        engine.GetStatus("NET-001").State.Should().Be(TweakState.Applied);

        engine.RevertTweak("NET-001", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _net.Dns[Eth].Should().Equal("192.168.178.1");
        _net.Dns[Wifi].Should().BeEmpty("vorher automatisch (DHCP)");
    }

    [Fact]
    public void Custom_dns_is_a_generated_tweak_with_strict_validation()
    {
        var id = CustomDnsTweaks.IdFor([IPAddress.Parse("94.140.14.14"), IPAddress.Parse("94.140.15.15")]);
        id.Should().Be("NET-004/94.140.14.14,94.140.15.15");
        CustomDnsTweaks.TryCreate(id, out var tweak).Should().BeTrue();
        tweak.Scope.Should().Be(TweakScope.Machine);
        CustomDnsTweaks.TryCreate("NET-004/127.0.0.1", out _).Should().BeFalse();
        CustomDnsTweaks.TryCreate("NET-004/1.1.1.1 ,1.0.0.1", out _).Should().BeFalse("nur die normalisierte Form ist gueltig");

        _net.Dns[Eth] = [];
        var engine = new TweakEngine(new CompositeCatalog(new ListCatalog()), Executors(), _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());
        engine.ApplyBatch([id], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _net.Dns[Eth].Should().Equal("94.140.14.14", "94.140.15.15");
    }

    [Fact]
    public void Adapter_power_saving_is_per_adapter_and_needs_admin_to_read()
    {
        _net.Power[Eth] = true;
        _net.Power[Wifi] = false;
        var engine = Engine(TestTweaks.Create("NET-911", new CommandQueryEqualsCondition { Command = "nic.powersaving", Value = "off" }, [new CommandAction { Command = "nic.powersaving", Value = "off" }], TweakScope.Machine));

        engine.ApplyBatch(["NET-911"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _net.Power.Values.Should().OnlyContain(v => !v);
        engine.RevertTweak("NET-911", _ => ConflictDecision.Cancel);
        _net.Power[Eth].Should().BeTrue();
        _net.Power[Wifi].Should().BeFalse();

        _net.PowerReadable = false;
        _h.Environment.IsElevated = false;
        var reader = Engine(TestTweaks.Create("NET-912", new CommandQueryEqualsCondition { Command = "nic.powersaving", Value = "off" }, [new CommandAction { Command = "nic.powersaving", Value = "off" }], TweakScope.Machine));
        var status = reader.GetStatus("NET-912");
        status.State.Should().Be(TweakState.Unavailable);
        status.Reason.Should().Contain("Administratorrechten");
        reader.PlanPreview(["NET-912"]).Ready.Should().ContainSingle("die Vorschau behaelt den Tweak, der Helfer prueft echt");
    }

    [Fact]
    public void Ping_summary_computes_median_jitter_and_loss()
    {
        var stats = PingTest.Summarize("1.1.1.1", [10, 12, null, 11, 15]);

        stats.Received.Should().Be(4);
        stats.LossPercent.Should().Be(20);
        stats.MinMs.Should().Be(10);
        stats.MaxMs.Should().Be(15);
        stats.MedianMs.Should().Be(11.5);
        stats.JitterMs.Should().BeApproximately((2 + 1 + 4) / 3.0, 0.001);
        PingTest.Summarize("x", [null, null]).MedianMs.Should().BeNull();
    }

    [Fact]
    public async Task Ping_test_sends_fifty_packets_and_rejects_bad_targets()
    {
        var pinger = new CountingPinger();
        var stats = await new PingTest(pinger, (_, _) => Task.CompletedTask).RunAsync("8.8.8.8", null, CancellationToken.None);

        pinger.Count.Should().Be(PingTest.Count);
        stats.Received.Should().Be(PingTest.Count);
        await ((Func<Task>)(() => new PingTest(pinger).RunAsync("evil.example; calc", null, CancellationToken.None))).Should().ThrowAsync<TweakPilotException>();
        await ((Func<Task>)(() => new PingTest(pinger).RunAsync("127.0.0.1", null, CancellationToken.None))).Should().ThrowAsync<TweakPilotException>();
    }

    [Fact]
    public void Maintenance_blocks_defrag_on_ssd_and_unknown_media()
    {
        var runner = new Recorder();
        var maintenance = new Maintenance(runner, new Volumes(), @"C:\Windows\System32");

        ((Action)(() => maintenance.Run(MaintenanceAction.Defrag, 'C'))).Should().Throw<TweakPilotException>().WithMessage("*gesperrt*");
        ((Action)(() => maintenance.Run(MaintenanceAction.Defrag, 'E'))).Should().Throw<TweakPilotException>("unbekannter Medientyp ist gesperrt");
        ((Action)(() => maintenance.Run(MaintenanceAction.ReTrim, 'D'))).Should().Throw<TweakPilotException>();
        ((Action)(() => maintenance.Run(MaintenanceAction.ReTrim, 'Z'))).Should().Throw<TweakPilotException>();
        runner.Calls.Should().BeEmpty();

        maintenance.Run(MaintenanceAction.Defrag, 'D').Ok.Should().BeTrue();
        maintenance.Run(MaintenanceAction.ReTrim, 'C').Ok.Should().BeTrue();
        maintenance.Run(MaintenanceAction.NetworkReset, null).RebootRequired.Should().BeTrue();
        runner.Calls.Should().Equal("defrag.exe D: /D", "defrag.exe C: /L", "netsh.exe winsock reset", "netsh.exe interface ip reset");
    }

    [Fact]
    public void Large_file_finder_skips_links_and_system_folder()
    {
        var fs = new TreeFs();
        fs.Add(@"C:\Users\max\Videos\film.mkv", 2_000_000_000);
        fs.Add(@"C:\Users\max\klein.txt", 10);
        fs.Add(@"C:\Users\max\Link\riesig.iso", 9_000_000_000, reparseParent: true);
        fs.Add(@"C:\Windows\big.dat", 3_000_000_000);

        var (files, truncated) = new LargeFileFinder(fs, [@"C:\Windows"]).Find([@"C:\"], CancellationToken.None);

        truncated.Should().BeFalse();
        files.Select(f => f.Path).Should().Equal(@"C:\Users\max\Videos\film.mkv");
    }

    [Theory]
    [InlineData("https://cloudflare-dns.com/dns-query", true)]
    [InlineData("http://cloudflare-dns.com/dns-query", false)]
    [InlineData("https://user:pw@dns.example/dns-query", false)]
    [InlineData("https://-bad.example/dns", false)]
    [InlineData("https://1.1.1.1/dns-query", false)]
    public void Doh_templates_need_https_and_a_valid_host(string template, bool valid)
    {
        DohCheck.IsValidTemplate(template).Should().Be(valid);
    }

    [Fact]
    public void Doh_detection_uses_well_known_servers()
    {
        DohCheck.Evaluate(["1.1.1.1", "8.8.8.8"], ["1.1.1.1", "192.168.178.1"]).ServersWithDoh.Should().Equal("1.1.1.1");
        DohCheck.Evaluate([], ["1.1.1.1"]).Supported.Should().BeFalse();
    }

    private TweakEngine Engine(params TweakDefinition[] tweaks) => new(new ListCatalog(tweaks), Executors(), _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());

    private Executors.ExecutorSet Executors() => new(_h.Registry, _h.Services, _h.Power, _h.Commands, _h.Environment.SystemDirectory, _h.Tasks, _h.Parameters, _net);

    private static TweakDefinition Dns(string id, string value) =>
        TestTweaks.Create(id, new CommandQueryEqualsCondition { Command = "dns.servers", Value = value }, [new CommandAction { Command = "dns.servers", Value = value }], TweakScope.Machine);

    private sealed class FakeNetwork : INetworkAccess
    {
        public Dictionary<string, IReadOnlyList<string>> Dns { get; } = new(StringComparer.OrdinalIgnoreCase) { [Eth] = [], [Wifi] = [] };

        public Dictionary<string, bool> Power { get; } = new(StringComparer.OrdinalIgnoreCase) { [Eth] = true, [Wifi] = true };

        public bool PowerReadable { get; set; } = true;

        public IReadOnlyList<string> PhysicalAdapters() => [Eth, Wifi];

        public IReadOnlyList<string> GetDnsServers(string adapter) => Dns[adapter];

        public void SetDnsServers(string adapter, IReadOnlyList<string> servers) => Dns[adapter] = servers.ToList();

        public string? GetAutotuning() => "normal";

        public bool? GetPowerSaving(string adapter) => PowerReadable ? Power[adapter] : null;

        public void SetPowerSaving(string adapter, bool enabled) => Power[adapter] = enabled;
    }

    private sealed class CountingPinger : IPinger
    {
        public int Count { get; private set; }

        public Task<double?> PingAsync(IPAddress target, TimeSpan timeout, CancellationToken cancellation)
        {
            Count++;
            return Task.FromResult<double?>(20);
        }
    }

    private sealed class Volumes : IVolumeInventory
    {
        public IReadOnlyList<VolumeInfo> FixedVolumes() =>
        [
            new('C', MediaKind.Ssd, 1000, 500, "SSD"),
            new('D', MediaKind.Hdd, 2000, 900, "HDD"),
            new('E', MediaKind.Unknown, 100, 50, null),
        ];
    }

    private sealed class Recorder : ICommandRunner
    {
        public List<string> Calls { get; } = [];

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add($"{Path.GetFileName(executable)} {string.Join(' ', arguments)}");
            return new CommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class TreeFs : ICleanupFileSystem
    {
        private readonly Dictionary<string, FsEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string file, long size, bool reparseParent = false)
        {
            var parent = Path.GetDirectoryName(file);
            while (parent is not null && parent.Length > 3)
            {
                _entries.TryAdd(parent, new FsEntry(parent, true, false, 0, DateTimeOffset.UnixEpoch));
                parent = Path.GetDirectoryName(parent);
            }

            if (reparseParent)
            {
                var dir = Path.GetDirectoryName(file)!;
                _entries[dir] = _entries[dir] with { IsReparsePoint = true };
            }

            _entries[file] = new FsEntry(file, false, false, size, DateTimeOffset.UnixEpoch);
        }

        public bool DirectoryExists(string path) => path.Length <= 3 || (_entries.TryGetValue(path, out var e) && e.IsDirectory);

        public FsEntry? Stat(string path) => _entries.GetValueOrDefault(path);

        public IEnumerable<FsEntry> List(string directory) =>
            _entries.Values.Where(e => string.Equals(Path.GetDirectoryName(e.FullPath), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                || (directory.Length <= 3 && string.Equals(Path.GetDirectoryName(e.FullPath), directory, StringComparison.OrdinalIgnoreCase)));

        public DeleteOutcome DeleteFile(string path) => throw new NotSupportedException();

        public bool DeleteEmptyDirectory(string path) => throw new NotSupportedException();
    }
}
