using System.IO;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

/// <summary>V2-5: Gaming-Checks in der Oberflaeche und Karte "Messung" mit Vorher/Nachher (Abschnitte 5.3 und 5.4).</summary>
public sealed class CheckTests : IDisposable
{
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly string _file = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"), "measure.json");

    public CheckTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_file)))
        {
            Directory.Delete(Path.GetDirectoryName(_file)!, recursive: true);
        }
    }

    [Fact]
    public async Task Check_rows_speak_plain_language_and_only_offer_reachable_jumps()
    {
        var opened = new List<string>();
        var jumps = new List<GamingJump>();
        var vm = new GamingChecksViewModel(() => new GamingFacts
        {
            Display = new DisplayFacts(60, 144),
            SystemDriveFreePercent = 8,
            Security = new SecurityFacts(false, 2, true, true, false, false),
            Today = new DateOnly(2026, 10, 20),
        }, _localizer, opened.Add)
        { Navigate = jumps.Add };

        await vm.LoadAsync();

        vm.Rows.Should().HaveCount(10);
        var refresh = vm.Rows[0];
        refresh.Title.Should().Be("Bildwiederholrate");
        refresh.Text.Should().Be("Dein Bildschirm kann 144 Hz, läuft aber mit 60 Hz. Das ist der häufigste Grund für ein ruckeliges Bild.");
        refresh.StateText.Should().Be("Prüfen");
        refresh.LinkText.Should().Be("Windows-Einstellungen öffnen");
        vm.OpenLinkCommand.Execute(refresh);
        opened.Should().Equal("ms-settings:display-advanced");

        var disk = vm.Rows.Single(r => r.Title == "Platz auf dem Systemlaufwerk");
        disk.CanJump.Should().BeTrue();
        vm.JumpCommand.Execute(disk);
        jumps.Should().Equal(GamingJump.Cleanup);
        vm.Rows.Single(r => r.Title.StartsWith("Secure Boot", StringComparison.Ordinal)).CanJump.Should().BeFalse("im Einfach-Modus gibt es keinen Anti-Cheat-Bereich");
        vm.Rows.Where(r => r.StateText == "Unbekannt").Should().OnlyContain(r => r.Text == "Konnte nicht gelesen werden.");
    }

    [Fact]
    public async Task Checks_a_tweak_can_fix_offer_fix_now_and_say_what_changes()
    {
        var fixedIds = new List<string>();
        var vm = new GamingChecksViewModel(() => new GamingFacts
        {
            GameMode = false,
            ActivePowerPlan = GamingChecks.PowerSaverPlan,
            Today = new DateOnly(2026, 10, 23),
        }, _localizer, _ => { })
        {
            Fix = id =>
            {
                fixedIds.Add(id);
                return Task.CompletedTask;
            },
            NameOf = id => "Name " + id,
        };

        await vm.LoadAsync();

        var gameMode = vm.Rows.Single(r => r.FixId == "GAME-001");
        gameMode.FixText.Should().Be("Ändert: Name GAME-001");
        gameMode.HowTo.Should().NotBeNullOrEmpty("the written guide stays next to the button");
        vm.Rows.Should().Contain(r => r.FixId == "PERF-001");
        vm.Rows.Where(r => r.FixId is null).Should().OnlyContain(r => r.FixText == null);

        await vm.FixCommand.ExecuteAsync(gameMode);
        fixedIds.Should().Equal("GAME-001");
    }

    [Fact]
    public async Task Without_a_fix_action_no_check_offers_fix_now()
    {
        var vm = new GamingChecksViewModel(() => new GamingFacts { GameMode = false, Today = new DateOnly(2026, 10, 23) }, _localizer, _ => { });

        await vm.LoadAsync();

        vm.Rows.Should().OnlyContain(r => r.FixId == null);
    }

    [Fact]
    public async Task Measurement_offers_after_only_after_a_restart_and_shows_the_difference()
    {
        var metrics = new QueueMetrics(
            new ProfileMetrics(14, 6_600_000_000, 100_000_000_000, 210, null),
            new ProfileMetrics(6, 5_800_000_000, 101_000_000_000, 180, null));
        var boot = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        var time = new StepTime(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        var vm = new MeasurementViewModel(metrics, new MeasurementStore(_file), () => boot, _localizer, time);
        vm.Status.Should().StartWith("Noch keine Messung");

        await vm.EnsureBeforeAsync();
        vm.PresetApplied();
        vm.CanMeasureAfter.Should().BeFalse("vor dem naechsten Neustart gibt es kein Nachher");
        vm.Status.Should().Contain("nächsten Neustart");

        boot = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var reopened = new MeasurementViewModel(metrics, new MeasurementStore(_file), () => boot, _localizer, time);
        reopened.CanMeasureAfter.Should().BeTrue();
        await reopened.MeasureAfterCommand.ExecuteAsync(null);

        reopened.Rows.Select(r => (r.Label, r.Value)).Should().Contain([("Autostart", "14 auf 6"), ("Laufende Prozesse", "210 auf 180")]);
        reopened.Rows.Single(r => r.Label == "Arbeitsspeicher belegt").Value.Should().Be("6,1 GB auf 5,4 GB");
        reopened.Rows.Should().NotContain(r => r.Label == "Letzter Start dauerte", "ohne Startdauer keine Zeile");
        reopened.CanMeasureAfter.Should().BeFalse();
    }

    [Fact]
    public async Task Measure_now_starts_over_and_a_broken_file_is_ignored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        await File.WriteAllTextAsync(_file, "{kaputt");
        var vm = new MeasurementViewModel(new QueueMetrics(new ProfileMetrics(3, 1L << 30, 2L << 30, 50, TimeSpan.FromSeconds(21))), new MeasurementStore(_file), () => DateTimeOffset.UnixEpoch, _localizer, TimeProvider.System);

        await vm.MeasureNowCommand.ExecuteAsync(null);

        vm.Rows.Single(r => r.Label == "Letzter Start dauerte").Value.Should().Be("21 s");
        vm.Status.Should().StartWith("Gemessen am");
    }

    /// <summary>Echter Lauf auf diesem Rechner, nur lesend: Messung und Gaming-Checks mit den Windows-Quellen.</summary>
    [Fact]
    public async Task Real_measurement_and_checks_read_this_pc_without_changing_anything()
    {
        var commands = new AscendedTweaks.Engine.Windows.ProcessCommandRunner();
        var autostart = new AutostartInventory(new AscendedTweaks.Engine.Windows.WindowsRegistryAccess(), new AscendedTweaks.Engine.Windows.WindowsAutostartSources(commands, Environment.SystemDirectory), new AscendedTweaks.Engine.Windows.WindowsSignatureVerifier());
        var metrics = new AscendedTweaks.App.Services.WindowsProfileMetrics(autostart.Read).Capture();

        metrics.EnabledAutostarts.Should().NotBeNull();
        metrics.RamUsedBytes.Should().BePositive();
        metrics.FreeSystemBytes.Should().BePositive();
        metrics.Processes.Should().BeGreaterThan(10);
        _output.WriteLine($"Messung: Autostart {metrics.EnabledAutostarts}, RAM belegt {metrics.RamUsedBytes / 1024 / 1024} MB, frei {metrics.FreeSystemBytes / 1024 / 1024 / 1024} GB, Prozesse {metrics.Processes}");

        var reader = new GamingFactsReader(
            new AscendedTweaks.Engine.Windows.WindowsSystemInfo(commands, Environment.SystemDirectory),
            new AscendedTweaks.Engine.Windows.WindowsHardwareFacts(),
            () => new AscendedTweaks.Engine.Windows.WindowsPowerPlanAccess().GetActive(),
            AscendedTweaks.Engine.Windows.BatteryInfo.HasBattery,
            autostart.Read,
            new WindowsProcessSampler(),
            new WindowsSecurityFacts(),
            TimeProvider.System);
        var vm = new GamingChecksViewModel(reader.Read, _localizer, _ => { });
        await vm.LoadAsync();

        vm.Rows.Should().HaveCount(10);
        foreach (var row in vm.Rows)
        {
            _output.WriteLine($"{row.Title}: {row.StateText}. {row.Text}");
        }
    }

    private sealed class QueueMetrics(params ProfileMetrics[] values) : IProfileMetrics
    {
        private readonly Queue<ProfileMetrics> _values = new(values);

        public ProfileMetrics Capture() => _values.Count > 1 ? _values.Dequeue() : _values.Peek();
    }

    private sealed class StepTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
