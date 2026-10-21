using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;
using Xunit;

namespace TweakPilot.Engine.Tests;

/// <summary>V2-4: Energie-Unter-Einstellungen mit Notebook-Sperre, Energy-efficient Ethernet, Defender-Scan-Last, bekannte Programme.</summary>
public sealed class V24Tests : IDisposable
{
    private const string Eth = "{11111111-1111-1111-1111-111111111111}";
    private const string Wifi = "{22222222-2222-2222-2222-222222222222}";
    private static readonly Guid PlanA = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PlanB = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Processor = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid MinState = Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private readonly Harness _h = new();
    private readonly EeeNetwork _net = new();
    private readonly FakeDefender _defender = new();

    public V24Tests()
    {
        _h.Power.Schemes.AddRange([PlanA, PlanB]);
        _h.Power.Active = PlanA;
        _h.Power.AcValues[(PlanA, Processor, MinState)] = 5;
        _h.Power.AcValues[(PlanB, Processor, MinState)] = 100;
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Power_setting_is_written_to_the_active_plan_and_restored_in_that_plan_after_a_plan_switch()
    {
        var engine = Engine(PowerTweak("POWR-903"));
        engine.PlanPreview(["POWR-903"]).Ready.Should().ContainSingle();
        _h.Power.WriteCount.Should().Be(0, "die Vorschau aendert nichts");

        engine.ApplyBatch(["POWR-903"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Power.AcValues[(PlanA, Processor, MinState)].Should().Be(100);
        _h.Power.Reactivations.Should().Be(1, "der aktive Plan wird neu aktiviert, damit der Wert gilt");
        engine.GetStatus("POWR-903").State.Should().Be(TweakState.Applied);

        _h.Power.Active = PlanB;
        engine.RevertTweak("POWR-903", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Power.AcValues[(PlanA, Processor, MinState)].Should().Be(5, "der Originalwert gehoert in den Plan, der beim Anwenden aktiv war");
        _h.Power.AcValues[(PlanB, Processor, MinState)].Should().Be(100, "der andere Plan bleibt unberuehrt");
    }

    [Fact]
    public void Desktop_only_tweaks_are_unavailable_on_devices_with_a_battery_and_missing_settings_too()
    {
        _h.Environment.HasBattery = true;
        var status = Engine(PowerTweak("POWR-904")).GetStatus("POWR-904");
        status.State.Should().Be(TweakState.Unavailable);
        status.Reason.Should().Contain("battery");

        _h.Environment.HasBattery = false;
        var missing = TestTweaks.Create("POWR-905", new PowerSettingEqualsCondition { Subgroup = Processor, Setting = Guid.NewGuid(), Value = 1 }, [new PowerSettingSetAction { Subgroup = Processor, Setting = Guid.NewGuid(), Value = 1 }], TweakScope.Machine);
        Engine(missing).GetStatus("POWR-905").State.Should().Be(TweakState.Unavailable);
    }

    [Fact]
    public void Energy_efficient_ethernet_only_counts_adapters_whose_driver_offers_it()
    {
        _net.Eee[Eth] = "on";
        var engine = Engine(Command("NET-912", "nic.eee", "off"));

        engine.ApplyBatch(["NET-912"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _net.Eee[Eth].Should().Be("off");
        _net.Eee.Should().NotContainKey(Wifi, "der WLAN-Adapter hat die Eigenschaft nicht und wird nie beschrieben");
        engine.RevertTweak("NET-912", _ => ConflictDecision.Cancel);
        _net.Eee[Eth].Should().Be("on");

        _net.Eee.Clear();
        var status = Engine(Command("NET-913", "nic.eee", "off")).GetStatus("NET-913");
        status.State.Should().Be(TweakState.Unavailable);
        status.Reason.Should().Contain("Energy-efficient ethernet");
    }

    [Fact]
    public void Defender_scan_load_changes_only_that_value_and_is_unavailable_when_a_policy_sets_it()
    {
        var engine = Engine(Command("DEFN-901", "defender.scanload", "20"));

        engine.ApplyBatch(["DEFN-901"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _defender.Load.Should().Be(20);
        engine.RevertTweak("DEFN-901", _ => ConflictDecision.Cancel);
        _defender.Load.Should().Be(50);

        _defender.Load = null;
        var status = engine.GetStatus("DEFN-901");
        status.State.Should().Be(TweakState.Unavailable);
        status.Reason.Should().Contain("policy");
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("5", true)]
    [InlineData("20", true)]
    [InlineData("100", true)]
    [InlineData("4", false)]
    [InlineData("101", false)]
    [InlineData("020", false)]
    [InlineData("-1", false)]
    [InlineData("20; Set-MpPreference -DisableRealtimeMonitoring 1", false)]
    public void Scan_load_values_follow_set_mppreference(string value, bool valid)
    {
        CommandWhitelist.IsValidScanLoad(value).Should().Be(valid);
    }

    [Fact]
    public void Power_settings_are_exported_with_powercfg_and_original_value()
    {
        var export = new Export.ScriptExporter(Executors(), new ListCatalog(PowerTweak("POWR-906"))).Export(["POWR-906"], id => id, "Test", DateTimeOffset.UnixEpoch);

        export.ApplyPs1.Should().Contain($"/setacvalueindex {PlanA:D} {Processor:D} {MinState:D} 100").And.Contain($"/setactive {PlanA:D}");
        export.RevertPs1.Should().Contain($"/setacvalueindex {PlanA:D} {Processor:D} {MinState:D} 5");
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "Valve Corp.", "AUTO-001")]
    [InlineData(@"C:\Users\a\AppData\Local\Discord\Update.exe", "\"C:\\Users\\a\\AppData\\Local\\Discord\\Update.exe\" --processStart Discord.exe --process-start-args \"--start-inactive\"", "Discord Inc.", "AUTO-003")]
    [InlineData(@"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe", "x", "Electronic Arts, Inc.", "AUTO-005")]
    [InlineData(@"C:\Users\a\AppData\Roaming\Spotify\Spotify.exe", "x", "Spotify AB", "AUTO-007")]
    [InlineData(@"C:\Temp\steam.exe", "x", "Unbekannt GmbH", null)]
    [InlineData(@"C:\Users\a\AppData\Local\Other\Update.exe", "Update.exe --processStart Discord.exe", "Other Ltd", null)]
    public void Known_programs_are_recognised_by_file_and_publisher_never_by_name(string path, string command, string publisher, string? expected)
    {
        KnownStartupApps.Match(path, command, new SignatureInfo(SignatureStatus.Valid, publisher))?.Id.Should().Be(expected);
        if (expected is null)
        {
            KnownStartupApps.Match(path, command, new SignatureInfo(SignatureStatus.Valid, publisher)).Should().BeNull();
        }

        KnownStartupApps.Match(path, command, new SignatureInfo(SignatureStatus.Unsigned, publisher)).Should().BeNull("ohne gueltige Signatur wird nichts erkannt");
    }

    [Fact]
    public void Memory_is_only_shown_for_running_programs_and_processes_get_plain_text()
    {
        var steam = KnownStartupApps.All.Single(a => a.Id == "AUTO-001");
        KnownStartupApps.RunningMemory(steam, [new ProcessSample(1, "explorer", null, 100)]).Should().BeNull();
        KnownStartupApps.RunningMemory(steam, [new ProcessSample(1, "steam", null, 100), new ProcessSample(2, "Steam", null, 50)]).Should().Be(150);

        KnownProcesses.KeyFor("svchost").Should().Be("process.known.svchost");
        KnownProcesses.KeyFor("Discord").Should().Be("autostart.known.AUTO-003.short");
        KnownProcesses.KeyFor("irgendwas").Should().Be("process.unknown");
        ProcessOverview.Top.Should().Be(15);
    }

    private TweakDefinition PowerTweak(string id) => TestTweaks.Create(
        id,
        new PowerSettingEqualsCondition { Subgroup = Processor, Setting = MinState, Value = 100 },
        [new PowerSettingSetAction { Subgroup = Processor, Setting = MinState, Value = 100 }],
        TweakScope.Machine) is var t
        ? new TweakDefinition
        {
            Id = t.Id,
            Category = "Performance.Power",
            NameKey = t.NameKey,
            DescriptionKey = t.DescriptionKey,
            SideEffectsKey = t.SideEffectsKey,
            Risk = RiskLevel.Caution,
            Evidence = t.Evidence,
            Builds = t.Builds,
            Editions = t.Editions,
            Scope = t.Scope,
            RequiresAdmin = t.RequiresAdmin,
            RequiresReboot = false,
            Detect = t.Detect,
            Apply = t.Apply,
            Revert = t.Revert,
            Sources = t.Sources,
            DesktopOnly = true,
        }
        : throw new InvalidOperationException();

    private static TweakDefinition Command(string id, string command, string value) =>
        TestTweaks.Create(id, new CommandQueryEqualsCondition { Command = command, Value = value }, [new CommandAction { Command = command, Value = value }], TweakScope.Machine);

    private TweakEngine Engine(params TweakDefinition[] tweaks) => new(new ListCatalog(tweaks), Executors(), _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());

    private Executors.ExecutorSet Executors() => new(_h.Registry, _h.Services, _h.Power, _h.Commands, _h.Environment.SystemDirectory, _h.Tasks, _h.Parameters, _net, _defender);

    private sealed class EeeNetwork : INetworkAccess
    {
        /// <summary>Nur Adapter mit Eintrag haben die Eigenschaft *EEE.</summary>
        public Dictionary<string, string> Eee { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> PhysicalAdapters() => [Eth, Wifi];

        public IReadOnlyList<string> GetDnsServers(string adapter) => [];

        public void SetDnsServers(string adapter, IReadOnlyList<string> servers)
        {
        }

        public string? GetAutotuning() => "normal";

        public bool? GetPowerSaving(string adapter) => true;

        public void SetPowerSaving(string adapter, bool enabled)
        {
        }

        public string? GetEnergyEfficientEthernet(string adapter) => Eee.TryGetValue(adapter, out var v) ? v : null;

        public void SetEnergyEfficientEthernet(string adapter, bool enabled)
        {
            Eee.Should().ContainKey(adapter, "nur Adapter mit der Eigenschaft werden beschrieben");
            Eee[adapter] = enabled ? "on" : "off";
        }
    }
}
