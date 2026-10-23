using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.Tests;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class Phase12PageTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly TweakCatalog _catalog;

    public Phase12PageTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
        {
            _h.Registry.Seed(RegHive.HKCU, P, name, RegistryData.DWord(1));
        }

        _h.Registry.Seed(RegHive.HKCU, P, "C", RegistryData.DWord(0));
        TweakDefinition[] tweaks =
        [
            TestTweaks.Dword("PRIV-901", P, "A", 0),
            TestTweaks.Dword("PRIV-902", P, "B", 0, risk: RiskLevel.Caution),
            TestTweaks.Dword("PRIV-903", P, "C", 0),
            TestTweaks.Create(
                "PRIV-904",
                new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = "D", Value = new RegLiteral { Number = 0 } },
                [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = "D", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
                evidence: EvidenceLevel.Disputed,
                hidden: true),
            TestTweaks.Create(
                "PRIV-905",
                new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = "E", Value = new RegLiteral { Number = 0 } },
                [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = "E", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
                evidence: EvidenceLevel.Minor),
            Dns("NET-901", "F"),
        ];
        var texts = tweaks.SelectMany(t => new[] { t.NameKey, t.DescriptionKey, t.SideEffectsKey }).ToDictionary(k => k, k => "Text " + k);
        _catalog = new TweakCatalog(tweaks, new Dictionary<string, TextBundle>
        {
            [Localizer.German] = new(Localizer.German, texts),
            [Localizer.English] = new(Localizer.English, texts),
        });
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task System_and_indicators_are_shown_without_an_overall_score()
    {
        using var formats = new GermanFormats();
        var dashboard = Dashboard(new ScriptedDialogs(_ => true));

        await dashboard.LoadAsync();

        dashboard.System.Select(r => r.Label).Should().Equal("Prozessor", "Grafik", "Arbeitsspeicher", "Datenträger", "Windows", "Letzter Start");
        dashboard.System[1].Value.Should().Contain("NVIDIA Test-GPU").And.Contain("12,0 GB").And.Contain("32.0.1");
        dashboard.System[3].Value.Should().Contain("NVMe-SSD");
        dashboard.System[5].Value.Should().Contain("nur mit Administratorrechten");
        dashboard.Indicators.Should().HaveCount(11);
        dashboard.Indicators.Should().OnlyContain(i => i.Explanation.Length > 0);
        dashboard.Indicators.Single(i => i.Label == "Autostart-Einträge (aktiv)").Value.Should().Be("1");
        dashboard.Indicators.Single(i => i.Label == "Microsoft Defender").Value.Should().Contain("aus").And.Contain("Fremd-AV");
        dashboard.Indicators.Single(i => i.Label == "Neustart ausstehend").Value.Should().Be("Nicht lesbar");
        dashboard.Indicators.Select(i => i.Label).Should().NotContain(l => l.Contains("Punkt", StringComparison.Ordinal) || l.Contains("Score", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_2684", "X", "NVIDIA")]
    [InlineData("PCI\\VEN_1002&DEV_7590", "X", "AMD")]
    [InlineData("PCI\\VEN_8086&DEV_A780", "X", "Intel")]
    [InlineData("ROOT\\BasicDisplay", "Microsoft Basic Display Adapter", "Microsoft")]
    public void Gpu_vendor_comes_from_the_pci_vendor_id(string pnp, string name, string vendor)
    {
        HardwareNames.GpuVendor(pnp, name).Should().Be(vendor);
    }

    [Fact]
    public void Disk_and_memory_types_map_documented_codes()
    {
        HardwareNames.DiskKind(4, 17).Should().Be(DiskKind.Nvme);
        HardwareNames.DiskKind(4, 11).Should().Be(DiskKind.Ssd);
        HardwareNames.DiskKind(3, 11).Should().Be(DiskKind.Hdd);
        HardwareNames.DiskKind(0, 7).Should().Be(DiskKind.Unknown);
        HardwareNames.MemoryType(34).Should().Be("DDR5");
        HardwareNames.MemoryType(26).Should().Be("DDR4");
        HardwareNames.MemoryType(2).Should().BeNull();
    }

    private static TweakDefinition Dns(string id, string name) => new()
    {
        Id = id,
        Category = "Network.Dns",
        NameKey = $"tweak.{id}.name",
        DescriptionKey = $"tweak.{id}.desc",
        SideEffectsKey = $"tweak.{id}.side",
        Risk = RiskLevel.Safe,
        Evidence = EvidenceLevel.Proven,
        Builds = [new BuildRange { Min = 19045 }],
        Editions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
        Scope = TweakScope.User,
        RequiresAdmin = false,
        RequiresReboot = false,
        Detect = new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = name, Value = new RegLiteral { Number = 0 } },
        Apply = [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = name, Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
        Revert = RevertSpec.RestoreFromBackup,
        Sources = ["Testquelle fuer Unit-Tests"],
    };

    private DashboardViewModel Dashboard(ScriptedDialogs dialogs) =>
        new(Workspace(dialogs), new FakeSystem(), new FakeSecurity(), () => [Entry(true), Entry(false)]);

    private static AutostartEntry Entry(bool enabled) => new()
    {
        Name = "x",
        Source = AutostartSource.Run,
        MachineWide = false,
        Command = "x.exe",
        Enabled = enabled,
        Signature = new SignatureInfo(SignatureStatus.Unsigned, null),
        Rating = AutostartRating.Optional,
        ReasonKey = "autostart.reason.optional",
    };

    private TweakWorkspace Workspace(ScriptedDialogs dialogs)
    {
        AscendedTweaks.Engine.TweakEngine Engine(string name) =>
            new(new CompositeCatalog(_catalog), _h.Executors, new AscendedTweaks.Engine.State.StateStore(Path.Combine(_h.StateRoot, name), new FakeTime()), _h.Environment, new AscendedTweaks.Engine.NoRestorePointService(), new FakeTime());
        var service = new AscendedTweaks.Ipc.TweakService(new CompositeCatalog(_catalog), Engine("u"), Engine("m"), Engine("p"), () => throw new InvalidOperationException(), new FakeTime());
        return new TweakWorkspace(_catalog, service, _localizer, new AppSettings(), dialogs, new SyncInvoker(), new CountingRestarter(), readOnly: false);
    }

    private sealed class FakeSystem : ISystemInfoSource
    {
        public SystemInfo Read() => new(
            new CpuInfo("Test-CPU", 6, 12),
            [new GpuInfo("NVIDIA", "Test-GPU", "32.0.1", new DateOnly(2026, 8, 17), 12L << 30)],
            new RamInfo(32L << 30, 8L << 30, 25, "DDR5"),
            [new DiskInfo("Test-NVMe", DiskKind.Nvme, 1L << 40, 1L << 39)],
            new WindowsInfo("Windows 11 Home", "25H2", "26200.1", DateTimeOffset.UnixEpoch),
            new BootInfo(DateTimeOffset.UnixEpoch, null, true));

        public HealthFacts ReadHealth() => new(null, "Ausbalanciert", true, false, ["Fremd-AV", "Windows Defender"], 50, 25);
    }

    private sealed class FakeSecurity : ISecurityFactsSource
    {
        public SecurityFacts Read() => new(true, 2, true, false, false, false);
    }
}
