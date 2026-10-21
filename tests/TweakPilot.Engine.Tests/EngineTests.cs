using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.State;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class EngineTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly ApplyOptions _apply = new();

    public EngineTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        _h.Registry.Seed(RegHive.HKCU, P, "One", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "Two", RegistryData.DWord(1));
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public void ApplyBatch_commits_and_records_backup_journal_and_ledger()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.TwoDwords("PRIV-902", P + @"\New", "A", "B"));

        var result = engine.ApplyBatch(["PRIV-901", "PRIV-902"], _apply);

        result.Outcome.Should().Be(BatchOutcome.Committed);
        result.AppliedTweaks.Should().Equal("PRIV-901", "PRIV-902");
        _h.Registry.GetValue(RegHive.HKCU, P, "One").Should().Be(RegistryData.DWord(0));
        engine.GetStatus("PRIV-902").State.Should().Be(TweakState.Applied);
        engine.GetAppliedTweaks().Select(e => e.TweakId).Should().Equal("PRIV-901", "PRIV-902");
        _h.Store.LoadJournal().Batches.Single().Status.Should().Be(BatchStatus.Committed);
        _h.RestorePoints.Calls.Should().Be(1);

        var files = Directory.GetFiles(result.BackupSetPath!, "*", SearchOption.AllDirectories).Select(Path.GetFileName);
        files.Should().Contain(["manifest.json", "values.json", "checksums.json", "PRIV-901.reg", "PRIV-902.reg"]);
        File.ReadAllText(Path.Combine(result.BackupSetPath!, "export", "PRIV-901.reg")).Should().Contain("\"One\"=dword:00000001");
    }

    [Fact]
    public void DryRun_changes_nothing_and_writes_no_state()
    {
        _h.Services.Services["Fax"] = new ServiceConfig(ServiceStartType.Manual, false, true);
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Service("PERF-901", "Fax", ServiceStartSetting.Disabled));
        var before = _h.Registry.Snapshot();

        var result = engine.ApplyBatch(["PRIV-901", "PERF-901"], new ApplyOptions { DryRun = true });

        result.Outcome.Should().Be(BatchOutcome.DryRun);
        result.Plan.Items.Should().HaveCount(2);
        result.Plan.Items[0].Current.Should().Be("DWord 1 (0x1)");
        result.Plan.Items[0].New.Should().Be("DWord 0 (0x0)");
        result.Plan.Items[1].Current.Should().Contain("Manual");
        _h.TotalWrites.Should().Be(0);
        _h.Registry.Snapshot().Should().Be(before);
        Directory.Exists(_h.StateRoot).Should().BeFalse();
        _h.RestorePoints.Calls.Should().Be(0);
    }

    [Fact]
    public void Failure_in_third_tweak_rolls_back_all_in_reverse_order()
    {
        var engine = _h.Engine(
            TestTweaks.Dword("PRIV-901", P, "One", 0),
            TestTweaks.TwoDwords("PRIV-902", P + @"\Neu", "A", "B"),
            TestTweaks.Dword("PRIV-903", P + @"\Gesperrt", "C", 0));
        var before = _h.Registry.Snapshot();
        _h.Registry.FailWrite = path => path.EndsWith("Gesperrt", StringComparison.Ordinal);

        var result = engine.ApplyBatch(["PRIV-901", "PRIV-902", "PRIV-903"], _apply);

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.FailedTweakId.Should().Be("PRIV-903");
        result.Error!.Kind.Should().Be(ErrorKind.PermissionDenied);
        _h.Registry.FailWrite = null;
        _h.Registry.Snapshot().Should().Be(before);
        engine.GetAppliedTweaks().Should().BeEmpty();

        var journal = _h.Store.LoadJournal().Batches.Single();
        journal.Status.Should().Be(BatchStatus.RolledBack);
        journal.Tweaks.Should().OnlyContain(t => t.Status == TweakJournalStatus.RolledBack);

        var restoreWrites = _h.Registry.WriteLog.SkipWhile(l => !l.Contains("Gesperrt", StringComparison.Ordinal)).Skip(1).ToList();
        restoreWrites.Should().Equal(
            @"delete Software\TweakPilotTest\Neu|B",
            @"delete Software\TweakPilotTest\Neu|A",
            @"delete-key Software\TweakPilotTest\Neu",
            @"set Software\TweakPilotTest|One");
    }

    [Fact]
    public void Value_blocked_by_windows_is_detected_and_rolled_back()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "Two", 0), TestTweaks.Dword("PRIV-902", P, "One", 0));
        _h.Registry.IgnoreWrite = _ => true;

        var result = engine.ApplyBatch(["PRIV-902", "PRIV-901"], _apply);

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.Error!.Kind.Should().Be(ErrorKind.ValueMismatchAfterWrite);
        result.FailedTweakId.Should().Be("PRIV-902");
    }

    [Fact]
    public void Failed_rollback_is_reported_as_critical_with_backup_path()
    {
        _h.Services.Services["Svc"] = new ServiceConfig(ServiceStartType.Manual, false, true);
        _h.Services.FailStart = true;
        var tweakA = TestTweaks.Service("PERF-901", "Svc", ServiceStartSetting.Disabled);
        var tweakB = TestTweaks.Dword("PERF-902", @"SOFTWARE\TweakPilotTest", "X", 0, RegHive.HKLM);
        var engine = _h.Engine(tweakA, tweakB);
        _h.Registry.FailWrite = _ => true;

        var result = engine.ApplyBatch(["PERF-901", "PERF-902"], _apply);

        result.Outcome.Should().Be(BatchOutcome.RollbackFailed);
        result.RollbackErrors.Should().ContainSingle().Which.Should().Contain("PERF-901");
        result.BackupSetPath.Should().NotBeNull();
        Directory.Exists(result.BackupSetPath).Should().BeTrue();
        _h.Store.LoadJournal().Batches.Single().Status.Should().Be(BatchStatus.RollbackFailed);
    }

    [Fact]
    public void Backup_failure_changes_nothing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_h.StateRoot)!);
        File.WriteAllText(_h.StateRoot, "Datei statt Ordner");
        try
        {
            var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));

            var result = engine.ApplyBatch(["PRIV-901"], _apply);

            result.Outcome.Should().Be(BatchOutcome.BackupFailed);
            result.Error!.Kind.Should().Be(ErrorKind.BackupFailed);
            _h.Registry.WriteCount.Should().Be(0);
        }
        finally
        {
            File.Delete(_h.StateRoot);
        }
    }

    [Fact]
    public void Declined_restore_point_aborts_without_changes()
    {
        _h.RestorePoints.Status = RestorePointStatus.Failed;
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));

        var result = engine.ApplyBatch(["PRIV-901"], new ApplyOptions { ConfirmWithoutRestorePoint = _ => false });

        result.Outcome.Should().Be(BatchOutcome.Aborted);
        _h.TotalWrites.Should().Be(0);
    }

    [Fact]
    public void Confirmed_missing_restore_point_continues()
    {
        _h.RestorePoints.Status = RestorePointStatus.ProtectionDisabled;
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));

        var result = engine.ApplyBatch(["PRIV-901"], new ApplyOptions { ConfirmWithoutRestorePoint = _ => true });

        result.Outcome.Should().Be(BatchOutcome.Committed);
        result.RestorePoint!.Status.Should().Be(RestorePointStatus.ProtectionDisabled);
    }

    [Fact]
    public void Cancellation_between_tweaks_rolls_back()
    {
        using var cts = new CancellationTokenSource();
        var progress = new CancelAfterFirst(cts);
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P, "Two", 0));
        var before = _h.Registry.Snapshot();

        var result = engine.ApplyBatch(["PRIV-901", "PRIV-902"], new ApplyOptions { Cancellation = cts.Token, Progress = progress });

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.Error!.Kind.Should().Be(ErrorKind.Cancelled);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Interrupted_batch_is_recovered_by_a_new_engine()
    {
        var tweaks = new[] { TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.TwoDwords("PRIV-902", P + @"\Neu", "A", "B") };
        var before = _h.Registry.Snapshot();
        var first = _h.Engine(tweaks);
        var plan = first.Plan(["PRIV-901", "PRIV-902"]);
        var context = first.BeginBatch(plan);
        first.ApplyTweak(context, plan.Ready[0]);
        first.ApplyTweak(context, plan.Ready[1]);

        // Absturz vor Commit: neue Engine-Instanz wie nach einem Neustart der App.
        var second = _h.Engine(tweaks);
        var interrupted = second.GetInterruptedBatches();
        interrupted.Should().ContainSingle();

        var errors = second.RecoverInterrupted(interrupted[0].BatchId);

        errors.Should().BeEmpty();
        _h.Registry.Snapshot().Should().Be(before);
        second.GetInterruptedBatches().Should().BeEmpty();
        second.GetAppliedTweaks().Should().BeEmpty();
        _h.Store.LoadJournal().Batches.Single().Status.Should().Be(BatchStatus.RolledBack);
    }

    [Fact]
    public void New_batch_is_blocked_while_an_interrupted_batch_is_open()
    {
        var tweaks = new[] { TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P, "Two", 0) };
        var engine = _h.Engine(tweaks);
        var plan = engine.Plan(["PRIV-901"]);
        var context = engine.BeginBatch(plan);
        engine.ApplyTweak(context, plan.Ready[0]);
        var writes = _h.Registry.WriteCount;

        var blocked = engine.ApplyBatch(["PRIV-902"], _apply);

        blocked.Outcome.Should().Be(BatchOutcome.BlockedByInterruptedBatch);
        blocked.BatchId.Should().Be(context.Batch.BatchId);
        _h.Registry.WriteCount.Should().Be(writes);

        engine.RecoverInterrupted(context.Batch.BatchId).Should().BeEmpty();
        engine.ApplyBatch(["PRIV-902"], _apply).Outcome.Should().Be(BatchOutcome.Committed);
    }

    [Fact]
    public void Second_instance_cannot_change_state_while_locked()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        engine.ApplyBatch(["PRIV-901"], _apply);

        using (_h.Store.AcquireLock())
        {
            var apply = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P, "Two", 0)).ApplyBatch(["PRIV-902"], _apply);
            apply.Outcome.Should().Be(BatchOutcome.BackupFailed);
            apply.Error!.Message.Should().Contain("andere");

            var revert = engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel);
            revert.Outcome.Should().Be(RevertOutcome.Failed);
        }

        _h.Registry.GetValue(RegHive.HKCU, P, "Two").Should().Be(RegistryData.DWord(1));
        engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
    }

    [Fact]
    public void Recovery_refuses_tampered_backup()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        var plan = engine.Plan(["PRIV-901"]);
        var context = engine.BeginBatch(plan);
        engine.ApplyTweak(context, plan.Ready[0]);
        File.AppendAllText(Path.Combine(_h.Store.BackupSetPath(context.Batch.BackupSetId), "values.json"), " ");

        var act = () => engine.RecoverInterrupted(context.Batch.BatchId);

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.StateCorrupt);
    }

    [Fact]
    public void Revert_single_tweak_restores_exact_original()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.TwoDwords("PRIV-902", P + @"\Neu", "A", "B"));
        var before = _h.Registry.Snapshot();
        engine.ApplyBatch(["PRIV-901", "PRIV-902"], _apply);

        var result = engine.RevertTweak("PRIV-902", _ => throw new InvalidOperationException("kein Konflikt erwartet"));

        result.Outcome.Should().Be(RevertOutcome.Reverted);
        engine.GetStatus("PRIV-902").State.Should().Be(TweakState.NotApplied);
        engine.GetStatus("PRIV-901").State.Should().Be(TweakState.Applied);
        engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
        engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.NotApplied);
    }

    [Theory]
    [InlineData(ConflictDecision.RestoreOriginal, 1u, RevertOutcome.Reverted, false)]
    [InlineData(ConflictDecision.KeepCurrent, 5u, RevertOutcome.Reverted, false)]
    [InlineData(ConflictDecision.Cancel, 5u, RevertOutcome.Cancelled, true)]
    public void Revert_conflict_follows_user_decision(ConflictDecision decision, uint expected, RevertOutcome outcome, bool stillTracked)
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        engine.ApplyBatch(["PRIV-901"], _apply);
        _h.Registry.Seed(RegHive.HKCU, P, "One", RegistryData.DWord(5));
        RevertConflict? seen = null;

        var result = engine.RevertTweak("PRIV-901", c =>
        {
            seen = c;
            return decision;
        });

        result.Outcome.Should().Be(outcome);
        seen!.View.Current.Should().Be("DWord 5 (0x5)");
        seen.View.Original.Should().Be("DWord 1 (0x1)");
        seen.View.Written.Should().Be("DWord 0 (0x0)");
        _h.Registry.GetValue(RegHive.HKCU, P, "One").Should().Be(RegistryData.DWord(expected));
        engine.GetAppliedTweaks().Any().Should().Be(stillTracked);
    }

    [Fact]
    public void RevertAll_reverts_in_reverse_order_of_application()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P, "Two", 0));
        var before = _h.Registry.Snapshot();
        engine.ApplyBatch(["PRIV-901"], _apply);
        engine.ApplyBatch(["PRIV-902"], _apply);

        var results = engine.RevertAll(_ => ConflictDecision.Cancel);

        results.Select(r => r.TweakId).Should().Equal("PRIV-902", "PRIV-901");
        results.Should().OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Explicit_revert_actions_are_used_on_revert()
    {
        const string clsid = @"Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}";
        var tweak = TestTweaks.Create(
            "UI-901",
            new RegistryKeyExistsCondition { Hive = RegHive.HKCU, Path = clsid + @"\InprocServer32" },
            [new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = clsid + @"\InprocServer32", DefaultValue = string.Empty }],
            revert: new RevertSpec([new RegistryDeleteKeyAction { Hive = RegHive.HKCU, Path = clsid }]));
        var engine = _h.Engine(tweak);
        engine.ApplyBatch(["UI-901"], _apply);

        var result = engine.RevertTweak("UI-901", _ => ConflictDecision.Cancel);

        result.Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Registry.WriteLog.Should().Contain($"delete-tree {clsid}");
        _h.Registry.KeyExists(RegHive.HKCU, clsid).Should().BeFalse();
    }

    [Fact]
    public void Reapply_after_drift_keeps_original_ledger_entry()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        engine.ApplyBatch(["PRIV-901"], _apply);
        _h.Registry.Seed(RegHive.HKCU, P, "One", RegistryData.DWord(3));

        engine.ApplyBatch(["PRIV-901"], _apply).Outcome.Should().Be(BatchOutcome.Committed);
        engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel);

        _h.Registry.GetValue(RegHive.HKCU, P, "One").Should().Be(RegistryData.DWord(1));
    }

    [Fact]
    public void Preflight_excludes_with_reasons()
    {
        _h.Environment.IsElevated = false;
        var engine = _h.Engine(
            TestTweaks.Dword("PRIV-901", P, "One", 1),
            TestTweaks.Dword("PERF-902", @"SOFTWARE\TweakPilotTest", "X", 0, RegHive.HKLM),
            TestTweaks.Create("PRIV-903", new RegistryValueAbsentCondition { Hive = RegHive.HKCU, Path = P, Name = "Z" }, [new RegistryDeleteValueAction { Hive = RegHive.HKCU, Path = P, Name = "Z" }], builds: [new BuildRange { Min = 30000 }]),
            TestTweaks.Create("PRIV-904", new RegistryValueAbsentCondition { Hive = RegHive.HKCU, Path = P, Name = "One" }, [new RegistryDeleteValueAction { Hive = RegHive.HKCU, Path = P, Name = "One" }], editions: [WindowsEdition.Enterprise]),
            TestTweaks.Service("PERF-905", "Fehlt", ServiceStartSetting.Disabled),
            TestTweaks.Dword("PRIV-906", P, "Two", 0),
            TestTweaks.Dword("PRIV-907", P, "Two", 5));
        _h.Environment.IsElevated = false;

        var plan = engine.Plan(["PRIV-901", "PERF-902", "PRIV-903", "PRIV-904", "PERF-905", "PRIV-906", "PRIV-907", "XYZ-000"]);

        plan.Ready.Select(t => t.Id).Should().Equal("PRIV-906");
        plan.Excluded.Select(e => (e.TweakId, e.Outcome)).Should().BeEquivalentTo(new[]
        {
            ("PRIV-901", PreflightOutcome.AlreadyApplied),
            ("PERF-902", PreflightOutcome.AdminRequired),
            ("PRIV-903", PreflightOutcome.NotSupportedOnBuild),
            ("PRIV-904", PreflightOutcome.EditionNotSupported),
            ("PERF-905", PreflightOutcome.AdminRequired),
            ("PRIV-907", PreflightOutcome.Conflict),
            ("XYZ-000", PreflightOutcome.Unknown),
        });
    }

    [Fact]
    public void Unavailable_service_is_excluded_when_elevated()
    {
        var engine = _h.Engine(TestTweaks.Service("PERF-905", "Fehlt", ServiceStartSetting.Disabled));

        var plan = engine.Plan(["PERF-905"]);

        plan.Excluded.Single().Outcome.Should().Be(PreflightOutcome.Unavailable);
        plan.Excluded.Single().Reason.Should().Contain("nicht installiert");
        engine.ApplyBatch(["PERF-905"], _apply).Outcome.Should().Be(BatchOutcome.NothingToDo);
    }

    [Fact]
    public void Ledger_owned_target_blocks_other_tweak()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P, "One", 7));
        engine.ApplyBatch(["PRIV-901"], _apply);

        var plan = engine.Plan(["PRIV-902"]);

        plan.Excluded.Single().Outcome.Should().Be(PreflightOutcome.Conflict);
    }

    [Theory]
    [InlineData(19044, RiskLevel.Safe, PreflightOutcome.ReadOnlyMode)]
    [InlineData(22621, RiskLevel.Safe, PreflightOutcome.ReadOnlyMode)]
    [InlineData(26300, RiskLevel.Expert, PreflightOutcome.ExpertLockedOnUntestedBuild)]
    public void Build_gates(int build, RiskLevel risk, PreflightOutcome expected)
    {
        _h.Environment.Build = build;
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0, risk: risk));

        engine.Plan(["PRIV-901"]).Excluded.Single().Outcome.Should().Be(expected);
        engine.GetStatus("PRIV-901").State.Should().Be(TweakState.Unavailable);
    }

    [Fact]
    public void Untested_build_allows_safe_tweaks()
    {
        _h.Environment.Build = 26300;
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));

        engine.Plan(["PRIV-901"]).Ready.Should().ContainSingle();
    }

    [Fact]
    public void Unknown_tweak_status_is_unavailable()
    {
        _h.Engine().GetStatus("NOPE-001").State.Should().Be(TweakState.Unavailable);
    }

    [Fact]
    public void Corrupt_ledger_is_reported_not_reset()
    {
        Directory.CreateDirectory(_h.StateRoot);
        File.WriteAllText(Path.Combine(_h.StateRoot, "ledger.json"), "{ kaputt");
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));

        var act = () => engine.GetAppliedTweaks();

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.StateCorrupt);
    }

    [Fact]
    public void Machine_tweaks_with_service_power_and_command_roundtrip()
    {
        var balanced = Guid.NewGuid();
        var high = Guid.NewGuid();
        _h.Power.Schemes.AddRange([balanced, high]);
        _h.Power.Active = balanced;
        _h.Services.Services["Fax"] = new ServiceConfig(ServiceStartType.Manual, false, false);
        var command = TestTweaks.Create(
            "PERF-911",
            new CommandQueryEqualsCondition { Command = "fsutil.disablelastaccess", Value = "1" },
            [new CommandAction { Command = "fsutil.disablelastaccess", Value = "1" }],
            TweakScope.Machine);
        var engine = _h.Engine(TestTweaks.Service("PERF-901", "Fax", ServiceStartSetting.Disabled), TestTweaks.Power("PERF-902", high), command);

        var result = engine.ApplyBatch(["PERF-901", "PERF-902", "PERF-911"], _apply);

        result.Outcome.Should().Be(BatchOutcome.Committed);
        File.Exists(Path.Combine(result.BackupSetPath!, "services.json")).Should().BeTrue();
        File.Exists(Path.Combine(result.BackupSetPath!, "power.json")).Should().BeTrue();
        File.Exists(Path.Combine(result.BackupSetPath!, "commands.json")).Should().BeTrue();

        engine.RevertAll(_ => ConflictDecision.Cancel).Should().OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Power.Active.Should().Be(balanced);
        _h.Services.Services["Fax"].StartType.Should().Be(ServiceStartType.Manual);
        _h.Commands.LastAccessValue.Should().Be("2");
    }

    [Fact]
    public void Revert_reports_failure_when_windows_blocks_the_restore()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        engine.ApplyBatch(["PRIV-901"], _apply);
        _h.Registry.IgnoreWrite = _ => true;

        var result = engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel);

        result.Outcome.Should().Be(RevertOutcome.Failed);
        result.Error!.Kind.Should().Be(ErrorKind.ValueMismatchAfterWrite);
        engine.GetAppliedTweaks().Should().ContainSingle("der Tweak bleibt gemerkt, damit ein erneuter Versuch moeglich ist");
    }

    [Fact]
    public void Revert_reports_permission_error()
    {
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0));
        engine.ApplyBatch(["PRIV-901"], _apply);
        _h.Registry.FailWrite = _ => true;

        var result = engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel);

        result.Outcome.Should().Be(RevertOutcome.Failed);
        result.Error!.Kind.Should().Be(ErrorKind.PermissionDenied);
    }

    [Fact]
    public void Explicit_revert_that_does_not_take_effect_fails()
    {
        const string clsid = @"Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}";
        var tweak = TestTweaks.Create(
            "UI-901",
            new RegistryKeyExistsCondition { Hive = RegHive.HKCU, Path = clsid + @"\InprocServer32" },
            [new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = clsid + @"\InprocServer32" }],
            revert: new RevertSpec([new RegistryDeleteKeyAction { Hive = RegHive.HKCU, Path = clsid }]));
        var engine = _h.Engine(tweak);
        engine.ApplyBatch(["UI-901"], _apply);
        _h.Registry.IgnoreWrite = _ => true;

        var result = engine.RevertTweak("UI-901", _ => ConflictDecision.Cancel);

        result.Outcome.Should().Be(RevertOutcome.Failed);
        result.Error!.Message.Should().Contain("Gegenaktion");
    }

    [Fact]
    public void Power_restore_fails_when_original_plan_was_deleted()
    {
        var balanced = Guid.NewGuid();
        var high = Guid.NewGuid();
        _h.Power.Schemes.AddRange([balanced, high]);
        _h.Power.Active = balanced;
        var engine = _h.Engine(TestTweaks.Power("PERF-902", high));
        engine.ApplyBatch(["PERF-902"], _apply);
        _h.Power.Schemes.Remove(balanced);

        var result = engine.RevertTweak("PERF-902", _ => ConflictDecision.Cancel);

        result.Outcome.Should().Be(RevertOutcome.Failed);
        result.Error!.Kind.Should().Be(ErrorKind.RollbackFailed);
    }

    [Fact]
    public void CreateKey_is_not_original_while_created_key_remains()
    {
        const string path = @"Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}\InprocServer32";
        var tweak = TestTweaks.Create(
            "UI-902",
            new RegistryKeyExistsCondition { Hive = RegHive.HKCU, Path = path },
            [new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = path }]);
        var engine = _h.Engine(tweak);
        engine.ApplyBatch(["UI-902"], _apply);
        _h.Registry.IgnoreWrite = _ => true;

        engine.RevertTweak("UI-902", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Failed);
    }

    [Fact]
    public void Unwritable_state_files_during_rollback_are_reported()
    {
        var locks = new List<FileStream>();
        var engine = _h.Engine(TestTweaks.Dword("PRIV-901", P, "One", 0), TestTweaks.Dword("PRIV-902", P + @"\Gesperrt", "C", 0));
        _h.Registry.FailWrite = path =>
        {
            if (!path.EndsWith("Gesperrt", StringComparison.Ordinal))
            {
                return false;
            }

            // Simuliert z. B. einen Virenscanner, der die Zustandsdateien gerade sperrt.
            foreach (var file in new[] { "ledger.json", "journal.json" })
            {
                locks.Add(new FileStream(Path.Combine(_h.StateRoot, file), FileMode.Open, FileAccess.Read, FileShare.None));
            }

            return true;
        };

        try
        {
            var result = engine.ApplyBatch(["PRIV-901", "PRIV-902"], _apply);

            result.Outcome.Should().Be(BatchOutcome.RollbackFailed);
            result.RollbackErrors.Should().Contain(e => e.StartsWith("Ledger:", StringComparison.Ordinal));
            result.RollbackErrors.Should().Contain(e => e.StartsWith("Journal:", StringComparison.Ordinal));
            _h.Registry.GetValue(RegHive.HKCU, P, "One").Should().Be(RegistryData.DWord(1), "die Registry wurde trotzdem zurueckgesetzt");
        }
        finally
        {
            locks.ForEach(l => l.Dispose());
        }
    }

    private sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value)
        {
            if (value.Index == 1)
            {
                cts.Cancel();
            }
        }
    }
}
