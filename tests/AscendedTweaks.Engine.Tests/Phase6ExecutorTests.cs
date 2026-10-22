using AscendedTweaks.Core;
using AscendedTweaks.Engine.Windows;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class Phase6ExecutorTests : IDisposable
{
    private const string Task = @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator";
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Scheduled_task_is_disabled_verified_and_restored()
    {
        _h.Tasks.Tasks[Task] = true;
        var engine = _h.Engine(TaskTweak("PERF-923", Task));

        engine.GetStatus("PERF-923").State.Should().Be(TweakState.NotApplied);
        engine.ApplyBatch(["PERF-923"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Tasks.Tasks[Task].Should().BeFalse();
        engine.GetStatus("PERF-923").State.Should().Be(TweakState.Applied);

        engine.RevertTweak("PERF-923", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Tasks.Tasks[Task].Should().BeTrue();
    }

    [Fact]
    public void Missing_task_is_unavailable_and_never_written()
    {
        var engine = _h.Engine(TaskTweak("PERF-921", @"\Microsoft\Windows\Application Experience\ProgramDataUpdater"));

        engine.GetStatus("PERF-921").State.Should().Be(TweakState.Unavailable);
        engine.ApplyBatch(["PERF-921"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.NothingToDo);
        _h.Tasks.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Failing_task_change_rolls_back_earlier_tweaks()
    {
        _h.Tasks.Tasks[Task] = true;
        _h.Tasks.FailWrite = true;
        _h.Registry.Seed(RegHive.HKLM, @"SOFTWARE\AscendedTweaksTest", "X", RegistryData.DWord(1));
        var before = _h.Registry.Snapshot();
        var engine = _h.Engine(TestTweaks.Dword("PERF-901", @"SOFTWARE\AscendedTweaksTest", "X", 0, RegHive.HKLM), TaskTweak("PERF-923", Task));

        var result = engine.ApplyBatch(["PERF-901", "PERF-923"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        _h.Registry.Snapshot().Should().Be(before);
        _h.Tasks.Tasks[Task].Should().BeTrue();
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag", true)]
    [InlineData(@"\Microsoft\Windows\..\Evil", false)]
    [InlineData(@"\Fremd\Aufgabe", false)]
    [InlineData(@"\Microsoft\Windows\Aufgabe*", false)]
    [InlineData(@"\Microsoft\Windows\Aufgabe""/DELETE", false)]
    public void Task_paths_are_limited_to_windows_tasks(string path, bool allowed)
    {
        ScheduledTaskPaths.IsAllowed(path).Should().Be(allowed);
    }

    [Fact]
    public void Task_xml_is_parsed_language_independent()
    {
        const string ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        WindowsScheduledTaskAccess.ParseEnabled($"""<?xml version="1.0" encoding="UTF-16"?><Task xmlns="{ns}"><Settings><Enabled>false</Enabled></Settings><Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers></Task>""")
            .Should().BeFalse();
        WindowsScheduledTaskAccess.ParseEnabled($"""<Task xmlns="{ns}"><Settings><Hidden>true</Hidden></Settings></Task>""").Should().BeTrue();
        WindowsScheduledTaskAccess.ParseEnabled("kaputt").Should().BeNull();
        WindowsScheduledTaskAccess.ParseEnabled($"""<Task xmlns="{ns}" />""").Should().BeNull();
    }

    [Fact]
    public void Task_access_uses_schtasks_without_shell_and_reports_errors()
    {
        var runner = new RecordingRunner(new CommandResult(1, string.Empty, "FEHLER: Zugriff verweigert"));
        var access = new WindowsScheduledTaskAccess(runner, @"C:\Windows\System32");

        access.GetEnabled(Task).Should().BeNull();
        ((Action)(() => access.SetEnabled(Task, false))).Should().Throw<AscendedTweaksException>().WithMessage("*Zugriff verweigert*");
        ((Action)(() => access.SetEnabled(@"\Fremd\X", false))).Should().Throw<AscendedTweaksException>();

        runner.Calls.Should().HaveCount(2);
        runner.Calls[0].Exe.Should().Be(@"C:\Windows\System32\schtasks.exe");
        runner.Calls[1].Args.Should().Equal("/Change", "/TN", Task, "/DISABLE");
    }

    [Fact]
    public void System_parameter_is_applied_and_restored()
    {
        var engine = _h.Engine(SpiTweak("PERF-905", "ClientAreaAnimation", 0));

        engine.ApplyBatch(["PERF-905"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Parameters.Values["ClientAreaAnimation"].Should().Be(0);
        engine.Plan(["PERF-905"]).Excluded.Single().Outcome.Should().Be(PreflightOutcome.AlreadyApplied);

        engine.RevertTweak("PERF-905", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Parameters.Values["ClientAreaAnimation"].Should().Be(1);
    }

    [Fact]
    public void System_parameter_outside_range_is_refused_before_writing()
    {
        var engine = _h.Engine(SpiTweak("PERF-906", "MenuShowDelay", 9000));

        engine.ApplyBatch(["PERF-906"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.RolledBack);
        _h.Parameters.WriteCount.Should().Be(0);
        _h.Parameters.Values["MenuShowDelay"].Should().Be(400);
    }

    [Fact]
    public void Hibernation_command_uses_power_capabilities_for_detection()
    {
        _h.Commands.OnHibernate = on => _h.Power.Hibernation = on;
        var tweak = TestTweaks.Create(
            "PERF-909",
            new CommandQueryEqualsCondition { Command = "powercfg.hibernate", Value = "off" },
            [new CommandAction { Command = "powercfg.hibernate", Value = "off" }],
            TweakScope.Machine);
        var engine = _h.Engine(tweak);

        engine.ApplyBatch(["PERF-909"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Power.Hibernation.Should().BeFalse();
        _h.Commands.Calls.Last().Args.Should().Equal("/hibernate", "off");

        engine.RevertTweak("PERF-909", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Power.Hibernation.Should().BeTrue();

        _h.Power.Hibernation = null;
        engine.GetStatus("PERF-909").State.Should().Be(TweakState.Unavailable, "ohne Ruhezustand-Unterstuetzung");
    }

    private static TweakDefinition TaskTweak(string id, string path)
    {
        return TestTweaks.Create(
            id,
            new ScheduledTaskEnabledEqualsCondition { TaskPath = path, Value = false },
            [new ScheduledTaskSetEnabledAction { TaskPath = path, Enabled = false }],
            TweakScope.Machine);
    }

    private static TweakDefinition SpiTweak(string id, string parameter, uint value)
    {
        return TestTweaks.Create(
            id,
            new SystemParameterEqualsCondition { Parameter = parameter, Value = value },
            [new SystemParametersInfoAction { Parameter = parameter, Value = value }]);
    }

    private sealed class RecordingRunner(CommandResult result) : ICommandRunner
    {
        public List<(string Exe, IReadOnlyList<string> Args)> Calls { get; } = [];

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add((executable, arguments));
            return result;
        }
    }
}
