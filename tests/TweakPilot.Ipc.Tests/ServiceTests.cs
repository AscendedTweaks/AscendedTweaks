using System.Text.Json;
using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine;
using TweakPilot.Engine.State;
using TweakPilot.Engine.Tests;
using Xunit;

namespace TweakPilot.Ipc.Tests;

/// <summary>Kanal ohne Pipe, aber mit derselben JSON-Serialisierung wie die echte Verbindung.</summary>
internal sealed class DirectChannel : IHelperChannel
{
    private readonly IHelperHandler _handler;

    public DirectChannel(IHelperHandler handler) => _handler = handler;

    public List<string> Commands { get; } = [];

    public BatchResultDto ApplyTweaks(ApplyTweaksRequest request) => Call<BatchResultDto>(IpcCommands.ApplyTweaks, request);

    public RevertTweakResponse RevertTweak(RevertTweakRequest request) => Call<RevertTweakResponse>(IpcCommands.RevertTweak, request);

    public TweakStatus Capture(string tweakId) => Call<TweakStatus>(IpcCommands.Capture, new TweakIdRequest { Id = tweakId });

    public RestorePointResult CreateRestorePoint(string name) => Call<RestorePointResult>(IpcCommands.CreateRestorePoint, new RestorePointRequest { Name = name });

    public IReadOnlyList<string> RecoverInterrupted(string batchId, TweakScope scope) => Call<List<string>>(IpcCommands.RecoverInterrupted, new RecoverRequest { BatchId = batchId, Scope = scope });

    public CleanupScan ScanCleanup(string categoryId) => Call<CleanupScan>(IpcCommands.ScanCleanup, new CleanupRequest { CategoryId = categoryId });

    public CleanupResult RunCleanup(string categoryId, CleanupMode mode) => Call<CleanupResult>(IpcCommands.RunCleanup, new CleanupRequest { CategoryId = categoryId, Mode = mode });

    public IReadOnlyList<TweakPilot.Engine.Debloat.RemovedApp> RemoveApps(IReadOnlyList<string> names, bool deprovision) =>
        Call<List<TweakPilot.Engine.Debloat.RemovedApp>>(IpcCommands.RemoveApps, new RemoveAppsRequest { Names = names, Deprovision = deprovision });

    public TweakPilot.Engine.Tools.MaintenanceResult RunMaintenance(TweakPilot.Engine.Tools.MaintenanceAction action, char? drive) =>
        Call<TweakPilot.Engine.Tools.MaintenanceResult>(IpcCommands.RunMaintenance, new MaintenanceRequest { Action = action, Drive = drive });

    public IReadOnlyList<TweakPilot.Engine.Tools.DiskHealth> ReadDiskHealth() => Call<List<TweakPilot.Engine.Tools.DiskHealth>>(IpcCommands.ReadDiskHealth, new { });

    public void Dispose()
    {
    }

    private T Call<T>(string command, object request)
    {
        Commands.Add(command);
        var result = _handler.Handle(command, JsonSerializer.SerializeToElement(request, request.GetType(), CoreJson.Options));
        var json = JsonSerializer.Serialize(result, result!.GetType(), CoreJson.Options);
        return JsonSerializer.Deserialize<T>(json, CoreJson.Options)!;
    }
}

public sealed class ServiceTests : IDisposable
{
    private const string UserPath = TestTweaks.Base;
    private const string MachinePath = @"SOFTWARE\TweakPilotTest";
    private readonly Harness _h = new();
    private readonly string _machineRoot;
    private readonly ListCatalog _catalog;
    private DirectChannel? _channel;

    public ServiceTests()
    {
        _machineRoot = Path.Combine(_h.StateRoot, "Machine");
        _h.Registry.Seed(RegHive.HKCU, UserPath, "U", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKLM, MachinePath, "M", RegistryData.DWord(1));
        _h.Registry.HkuAliasSid = PolicySid;
        _h.Registry.SeedKey(RegHive.HKCU, @"Software\Policies"); // existiert unter Windows immer
        _catalog = new ListCatalog(
            TestTweaks.Dword("PRIV-901", UserPath, "U", 0),
            TestTweaks.Dword("PRIV-902", UserPath + @"\Gesperrt", "X", 0),
            TestTweaks.Dword("PERF-901", MachinePath, "M", 0, RegHive.HKLM),
            TestTweaks.Dword("PERF-902", MachinePath + @"\Gesperrt", "X", 0, RegHive.HKLM),
            TestTweaks.Dword("PRIV-904", @"Software\Policies\TweakPilotTest", "P", 1));
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Preview_and_status_never_start_the_helper()
    {
        using var service = CreateService();

        var plan = service.Preview(["PRIV-901", "PERF-901"]);
        var status = service.GetStatus("PERF-901");

        plan.Ready.Select(t => t.Id).Should().Equal("PERF-901", "PRIV-901");
        status.State.Should().Be(TweakState.NotApplied);
        service.Apply(["PERF-901"], new ApplyOptions { DryRun = true }).Outcome.Should().Be(BatchOutcome.DryRun);
        service.HelperStarted.Should().BeFalse();
        _h.TotalWrites.Should().Be(0);
    }

    [Fact]
    public void User_only_batch_runs_without_helper()
    {
        using var service = CreateService();

        var result = service.Apply(["PRIV-901"], new ApplyOptions { ConfirmWithoutRestorePoint = _ => true });

        result.Outcome.Should().Be(BatchOutcome.Committed);
        result.RestorePoint!.Status.Should().Be(RestorePointStatus.NotAttempted);
        service.HelperStarted.Should().BeFalse();
    }

    [Fact]
    public void Mixed_batch_uses_one_restore_point_and_both_processes()
    {
        using var service = CreateService();

        var result = service.Apply(["PRIV-901", "PERF-901"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.Committed);
        result.AppliedTweaks.Should().BeEquivalentTo("PRIV-901", "PERF-901");
        _h.RestorePoints.Calls.Should().Be(1);
        _channel!.Commands.Should().Equal(IpcCommands.CreateRestorePoint, IpcCommands.ApplyTweaks);
        _h.Registry.GetValue(RegHive.HKLM, MachinePath, "M").Should().Be(RegistryData.DWord(0));
        _h.Registry.GetValue(RegHive.HKCU, UserPath, "U").Should().Be(RegistryData.DWord(0));
        service.GetAppliedTweaks().Should().HaveCount(2);
        result.BackupSetPath.Should().Contain(";");
    }

    [Fact]
    public void Declined_restore_point_changes_nothing()
    {
        _h.RestorePoints.Status = RestorePointStatus.ProtectionDisabled;
        using var service = CreateService();

        var result = service.Apply(["PRIV-901", "PERF-901"], new ApplyOptions { ConfirmWithoutRestorePoint = _ => false });

        result.Outcome.Should().Be(BatchOutcome.Aborted);
        _h.Registry.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Failing_machine_part_leaves_user_part_untouched()
    {
        _h.Registry.FailWrite = p => p.EndsWith("Gesperrt", StringComparison.Ordinal);
        using var service = CreateService();
        var before = _h.Registry.Snapshot();

        var result = service.Apply(["PRIV-901", "PERF-901", "PERF-902"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.FailedTweakId.Should().Be("PERF-902");
        result.Error!.Kind.Should().Be(ErrorKind.PermissionDenied);
        _h.Registry.FailWrite = null;
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Failing_user_part_compensates_machine_part()
    {
        _h.Registry.FailWrite = p => p.EndsWith("Gesperrt", StringComparison.Ordinal);
        using var service = CreateService();
        var before = _h.Registry.Snapshot();

        var result = service.Apply(["PRIV-901", "PRIV-902", "PERF-901"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.FailedTweakId.Should().Be("PRIV-902");
        _h.Registry.FailWrite = null;
        _h.Registry.Snapshot().Should().Be(before);
        service.GetAppliedTweaks().Should().BeEmpty();
    }

    [Fact]
    public void Machine_revert_asks_about_conflicts_through_the_helper()
    {
        using var service = CreateService();
        service.Apply(["PERF-901"], new ApplyOptions());
        _h.Registry.Seed(RegHive.HKLM, MachinePath, "M", RegistryData.DWord(7));
        var asked = new List<RevertConflict>();

        var cancelled = service.Revert("PERF-901", c =>
        {
            asked.Add(c);
            return ConflictDecision.Cancel;
        });
        var reverted = service.Revert("PERF-901", _ => ConflictDecision.RestoreOriginal);

        cancelled.Outcome.Should().Be(RevertOutcome.Cancelled);
        asked.Single().View.Current.Should().Be("DWord 7 (0x7)");
        reverted.Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Registry.GetValue(RegHive.HKLM, MachinePath, "M").Should().Be(RegistryData.DWord(1));
        service.Revert("PERF-901", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.NotApplied);
    }

    [Fact]
    public void RevertAll_spans_both_stores()
    {
        using var service = CreateService();
        var before = _h.Registry.Snapshot();
        service.Apply(["PRIV-901", "PERF-901"], new ApplyOptions());

        var results = service.RevertAll(_ => ConflictDecision.Cancel);

        results.Should().HaveCount(2).And.OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Interrupted_batches_from_both_stores_are_listed_and_recovered()
    {
        using var service = CreateService();
        var helperEngine = MachineEngine(_h.RestorePoints, elevated: true);
        var plan = helperEngine.Plan(["PERF-901"]);
        var context = helperEngine.BeginBatch(plan);
        helperEngine.ApplyTweak(context, plan.Ready[0]);

        var open = service.GetInterruptedBatches();

        open.Should().ContainSingle().Which.Scope.Should().Be(TweakScope.Machine);
        service.RecoverInterrupted(TweakScope.Machine, open[0].Batch.BatchId).Should().BeEmpty();
        _h.Registry.GetValue(RegHive.HKLM, MachinePath, "M").Should().Be(RegistryData.DWord(1));
    }

    [Fact]
    public void User_policy_runs_in_helper_against_the_users_hive_and_reverts()
    {
        using var service = CreateService();
        var before = _h.Registry.Snapshot();

        var preview = service.Preview(["PRIV-904"]);
        service.HelperStarted.Should().BeFalse("die Vorschau braucht keinen Helfer");
        preview.Ready.Single().Scope.Should().Be(TweakScope.UserPolicy);

        var result = service.Apply(["PRIV-901", "PRIV-904", "PERF-901"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.Committed, result.Error?.ToString());
        result.AppliedTweaks.Should().Equal("PERF-901", "PRIV-904", "PRIV-901");
        _h.Registry.WriteLog.Should().Contain(l => l.Contains(PolicySid, StringComparison.Ordinal), "der Helfer schreibt ueber HKU\\SID");
        _h.Registry.GetValue(RegHive.HKCU, @"Software\Policies\TweakPilotTest", "P").Should().Be(RegistryData.DWord(1));
        service.GetStatus("PRIV-904").State.Should().Be(TweakState.Applied);
        service.GetStatus("PRIV-904").TrackedInLedger.Should().BeTrue();

        service.RevertAll(_ => ConflictDecision.Cancel).Should().HaveCount(3).And.OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Failing_user_part_compensates_machine_and_policy_parts()
    {
        _h.Registry.FailWrite = p => p.EndsWith("Gesperrt", StringComparison.Ordinal);
        using var service = CreateService();
        var before = _h.Registry.Snapshot();

        var result = service.Apply(["PERF-901", "PRIV-904", "PRIV-902"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        _h.Registry.FailWrite = null;
        _h.Registry.Snapshot().Should().Be(before);
        service.GetAppliedTweaks().Should().BeEmpty();
    }

    [Fact]
    public void Helper_registry_maps_only_user_policies()
    {
        var registry = new HelperRegistryAccess(_h.Registry, PolicySid);

        registry.SetValue(RegHive.HKCU, @"Software\Policies\X", "v", RegistryData.DWord(1));

        _h.Registry.WriteLog.Last().Should().Contain(PolicySid);
        ((Action)(() => registry.SetValue(RegHive.HKCU, @"Software\Microsoft\X", "v", RegistryData.DWord(1))))
            .Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        ((Action)(() => new HelperRegistryAccess(_h.Registry).SetValue(RegHive.HKCU, @"Software\Policies\X", "v", RegistryData.DWord(1))))
            .Should().Throw<TweakPilotException>();
        ((Action)(() => registry.GetValue(RegHive.HKU, @"S-1-5-18\Software", "v")))
            .Should().Throw<TweakPilotException>("direkte HKU-Pfade sind nicht erlaubt");
        registry.KeyExists(RegHive.HKCU, @"Software\Policies").Should().BeTrue("Vorfahren duerfen gelesen werden");
        ((Action)(() => registry.CreateKey(RegHive.HKCU, @"Software\Policies")))
            .Should().Throw<TweakPilotException>("Vorfahren duerfen nie geschrieben werden");
        ((Action)(() => registry.KeyExists(RegHive.HKCU, @"Software\Microsoft")))
            .Should().Throw<TweakPilotException>();
    }

    [Fact]
    public void Helper_cleanup_accepts_only_known_machine_categories_without_paths()
    {
        var handler = new EngineHelperHandler(_catalog, (rp, _) => MachineEngine(rp, true), _h.RestorePoints, elevated: true);
        var noCleanup = () => handler.Handle(IpcCommands.ScanCleanup, Element(new CleanupRequest { CategoryId = "CLEAN-002" }));
        noCleanup.Should().Throw<IpcProtocolException>("ohne eingerichtete Bereinigung");

        var cleanup = new TweakPilot.Engine.Cleanup.CleanupEngine(
            new EmptyFs(), new NoRecycler(), new NoProcesses(), new NoSpecials(), _h.Services,
            new Dictionary<CleanupBase, string> { [CleanupBase.Windows] = @"C:\Windows" }, [], TweakScope.Machine, new FakeTime());
        handler = new EngineHelperHandler(_catalog, (rp, _) => MachineEngine(rp, true), _h.RestorePoints, elevated: true, cleanup);

        ((CleanupScan)handler.Handle(IpcCommands.ScanCleanup, Element(new CleanupRequest { CategoryId = "CLEAN-002" }))!).CategoryId.Should().Be("CLEAN-002");
        ((Action)(() => handler.Handle(IpcCommands.RunCleanup, Element(new CleanupRequest { CategoryId = "CLEAN-001" }))))
            .Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        ((Action)(() => handler.Handle(IpcCommands.RunCleanup, Element(new CleanupRequest { CategoryId = "CLEAN-999" })))).Should().Throw<IpcProtocolException>();
        var withPath = JsonDocument.Parse("""{"categoryId":"CLEAN-002","path":"C:\\Users"}""").RootElement;
        ((Action)(() => handler.Handle(IpcCommands.RunCleanup, withPath))).Should().Throw<Exception>("unbekannte Felder wie Pfade werden abgelehnt");
    }

    [Fact]
    public void Helper_remove_apps_validates_names_and_requires_the_remover()
    {
        var plain = new EngineHelperHandler(_catalog, (rp, _) => MachineEngine(rp, true), _h.RestorePoints, elevated: true);
        ((Action)(() => plain.Handle(IpcCommands.RemoveApps, Element(new RemoveAppsRequest { Names = ["Microsoft.BingNews"] })))).Should().Throw<IpcProtocolException>();

        var appx = new ListAppx();
        var handler = new EngineHelperHandler(_catalog, (rp, _) => MachineEngine(rp, true), _h.RestorePoints, elevated: true, apps: new TweakPilot.Engine.Debloat.AppRemover(appx, new FakeTime()));
        ((Action)(() => handler.Handle(IpcCommands.RemoveApps, Element(new RemoveAppsRequest { Names = [] })))).Should().Throw<IpcProtocolException>();
        ((Action)(() => handler.Handle(IpcCommands.RemoveApps, Element(new RemoveAppsRequest { Names = [@"..\x"] })))).Should().Throw<IpcProtocolException>();
        ((Action)(() => handler.Handle(IpcCommands.RemoveApps, Element(new RemoveAppsRequest { Names = ["Microsoft.WindowsStore"] }))))
            .Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);

        var result = (List<TweakPilot.Engine.Debloat.RemovedApp>)handler.Handle(IpcCommands.RemoveApps, Element(new RemoveAppsRequest { Names = ["Microsoft.BingNews"] }))!;
        result.Single().Ok.Should().BeTrue();
        appx.Removed.Should().Equal("Microsoft.BingNews_1_x64__8wekyb3d8bbwe");
    }

    private sealed class ListAppx : TweakPilot.Engine.Debloat.IAppxAccess
    {
        public List<string> Removed { get; } = [];

        public IReadOnlyList<TweakPilot.Engine.Debloat.AppxPackageInfo> ListForCurrentUser() => [];

        public IReadOnlyList<TweakPilot.Engine.Debloat.AppxPackageInfo> FindForAllUsers(string name) =>
            Removed.Count > 0 ? [] : [new(name, name + "_1_x64__8wekyb3d8bbwe", name + "_8wekyb3d8bbwe", "Microsoft Corporation", true, false, false, false, null)];

        public bool IsProvisioned(string familyName) => false;

        public void RemoveForAllUsers(string fullName) => Removed.Add(fullName);

        public void Deprovision(string familyName)
        {
        }

        public TweakPilot.Engine.Debloat.AppxRestoreOutcome RegisterByFamilyName(string familyName) => TweakPilot.Engine.Debloat.AppxRestoreOutcome.Registered;

        public TweakPilot.Engine.Debloat.AppxRestoreOutcome RegisterManifest(string manifestPath) => TweakPilot.Engine.Debloat.AppxRestoreOutcome.Registered;
    }

    [Fact]
    public void Service_estimates_machine_cleanup_without_starting_the_helper()
    {
        using var service = CreateService();
        var estimator = new TweakPilot.Engine.Cleanup.CleanupEngine(
            new EmptyFs(), new NoRecycler(), new NoProcesses(), new NoSpecials(), _h.Services,
            new Dictionary<CleanupBase, string>(), [], TweakScope.Machine, new FakeTime());
        var user = new TweakPilot.Engine.Cleanup.CleanupEngine(
            new EmptyFs(), new NoRecycler(), new NoProcesses(), new NoSpecials(), _h.Services,
            new Dictionary<CleanupBase, string>(), [], TweakScope.User, new FakeTime());
        service.AttachCleanup(user, estimator);

        service.ScanCleanup("CLEAN-002").Estimated.Should().BeTrue();
        service.ScanCleanup("CLEAN-001").Estimated.Should().BeFalse();
        service.HelperStarted.Should().BeFalse("ein Scan braucht keinen UAC-Dialog");
    }

    private sealed class EmptyFs : TweakPilot.Engine.Cleanup.ICleanupFileSystem
    {
        public bool DirectoryExists(string path) => false;

        public TweakPilot.Engine.Cleanup.FsEntry? Stat(string path) => null;

        public IEnumerable<TweakPilot.Engine.Cleanup.FsEntry> List(string directory) => [];

        public TweakPilot.Engine.Cleanup.DeleteOutcome DeleteFile(string path) => TweakPilot.Engine.Cleanup.DeleteOutcome.Missing;

        public bool DeleteEmptyDirectory(string path) => false;
    }

    private sealed class NoRecycler : TweakPilot.Engine.Cleanup.IRecycler
    {
        public void Recycle(IReadOnlyList<string> paths)
        {
        }
    }

    private sealed class NoProcesses : TweakPilot.Engine.Cleanup.IProcessProbe
    {
        public bool IsRunning(string processName) => false;
    }

    private sealed class NoSpecials : TweakPilot.Engine.Cleanup.ICleanupSpecials
    {
        public CleanupScan ScanRecycleBin() => new() { CategoryId = string.Empty };

        public CleanupResult EmptyRecycleBin() => new() { CategoryId = string.Empty, Mode = CleanupMode.Permanent };

        public CleanupResult RunPreviousInstallations() => EmptyRecycleBin();

        public CleanupResult RunComponentStoreCleanup() => EmptyRecycleBin();
    }

    [Fact]
    public void Handler_refuses_mixed_scopes_in_one_request()
    {
        var act = () => Handler().Handle(IpcCommands.ApplyTweaks, Element(new ApplyTweaksRequest { Ids = ["PERF-901", "PRIV-904"] }));

        act.Should().Throw<IpcProtocolException>().WithMessage("*one scope*");
    }

    [Fact]
    public void Handler_refuses_user_tweaks_and_bad_input()
    {
        var handler = Handler();

        var user = () => handler.Handle(IpcCommands.ApplyTweaks, Element(new ApplyTweaksRequest { Ids = ["PRIV-901"] }));
        var unknown = () => handler.Handle(IpcCommands.ApplyTweaks, Element(new ApplyTweaksRequest { Ids = ["PERF-999"] }));
        var empty = () => handler.Handle(IpcCommands.ApplyTweaks, Element(new ApplyTweaksRequest { Ids = [] }));
        var name = () => handler.Handle(IpcCommands.CreateRestorePoint, Element(new RestorePointRequest { Name = "a\u0001" }));
        var batch = () => handler.Handle(IpcCommands.RecoverInterrupted, Element(new RecoverRequest { BatchId = "..\\x" }));
        var command = () => handler.Handle("RunScript", null);
        var missing = () => handler.Handle(IpcCommands.Capture, null);

        user.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        unknown.Should().Throw<IpcProtocolException>();
        empty.Should().Throw<IpcProtocolException>();
        name.Should().Throw<IpcProtocolException>();
        batch.Should().Throw<IpcProtocolException>();
        command.Should().Throw<IpcProtocolException>();
        missing.Should().Throw<IpcProtocolException>();
        ((PingResponse)handler.Handle(IpcCommands.Ping, null)!).Elevated.Should().BeTrue();
        ((TweakStatus)handler.Handle(IpcCommands.Capture, Element(new TweakIdRequest { Id = "PERF-901" }))!).State.Should().Be(TweakState.NotApplied);
    }

    [Fact]
    public void Helper_maintenance_accepts_only_fixed_actions_and_single_drive_letters()
    {
        var runner = new MaintenanceRunner();
        var maintenance = new TweakPilot.Engine.Tools.Maintenance(runner, new OneSsd(), @"C:\Windows\System32");
        var handler = new EngineHelperHandler(_catalog, (rp, _) => MachineEngine(rp, elevated: true), _h.RestorePoints, elevated: true, maintenance: maintenance);
        JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement;

        var unknownAction = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"Format","drive":"C"}"""));
        var numericAction = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":99,"drive":"C"}"""));
        var lowerDrive = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"ReTrim","drive":"c"}"""));
        var digitDrive = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"ReTrim","drive":"1"}"""));
        var extraField = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"Defrag","drive":"C","args":"/X"}"""));
        var hddOnSsd = () => handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"Defrag","drive":"C"}"""));
        var noHealth = () => handler.Handle(IpcCommands.ReadDiskHealth, null);

        unknownAction.Should().Throw<Exception>();
        numericAction.Should().Throw<IpcProtocolException>();
        lowerDrive.Should().Throw<IpcProtocolException>();
        digitDrive.Should().Throw<IpcProtocolException>();
        extraField.Should().Throw<Exception>("unbekannte Felder werden abgelehnt");
        hddOnSsd.Should().Throw<TweakPilotException>("Defragmentieren auf SSD ist gesperrt; der Helfer prueft den Medientyp selbst");
        noHealth.Should().Throw<IpcProtocolException>();
        runner.Calls.Should().BeEmpty();

        handler.Handle(IpcCommands.RunMaintenance, Raw("""{"action":"ReTrim","drive":"C"}"""));
        runner.Calls.Should().Equal("defrag.exe C: /L");
    }

    [Fact]
    public void Batch_result_dto_roundtrips()
    {
        var dto = new BatchResultDto
        {
            Outcome = BatchOutcome.RolledBack,
            ReadyIds = ["PERF-901"],
            ErrorKind = ErrorKind.ValueMismatchAfterWrite,
            Error = "x",
            FailedTweakId = "PERF-901",
        };

        var result = dto.ToResult(_catalog);

        result.Plan.Ready.Single().Id.Should().Be("PERF-901");
        result.Error!.Kind.Should().Be(ErrorKind.ValueMismatchAfterWrite);
        BatchResultDto.From(result).ErrorKind.Should().Be(ErrorKind.ValueMismatchAfterWrite);
        ((Action)(() => new BatchResultDto { Outcome = BatchOutcome.Committed, ReadyIds = ["NOPE-001"] }.ToResult(_catalog)))
            .Should().Throw<IpcProtocolException>();
    }

    private TweakService CreateService()
    {
        var userEngine = new TweakEngine(_catalog, _h.Executors, new StateStore(Path.Combine(_h.StateRoot, "User"), new FakeTime()), _h.Environment, new NoRestorePointService(), new FakeTime());
        var reader = MachineEngine(new NoRestorePointService(), elevated: false);
        var policyReader = PolicyEngine(new NoRestorePointService(), elevated: false, _h.Registry);
        return new TweakService(_catalog, userEngine, reader, policyReader, () => _channel = new DirectChannel(Handler()), new FakeTime());
    }

    private EngineHelperHandler Handler()
    {
        return new EngineHelperHandler(
            _catalog,
            (rp, scope) => scope == TweakScope.UserPolicy
                ? PolicyEngine(rp, elevated: true, new HelperRegistryAccess(_h.Registry, PolicySid))
                : MachineEngine(rp, elevated: true),
            _h.RestorePoints,
            elevated: true);
    }

    /// <summary>Benutzer-Richtlinien: Der Helfer schreibt ueber HKU\SID; die Fake-Registry bildet das wie Windows auf HKCU ab.</summary>
    private const string PolicySid = "S-1-5-21-1-2-3-1001";

    private TweakEngine PolicyEngine(IRestorePointService restorePoints, bool elevated, IRegistryAccess registry)
    {
        var executors = new TweakPilot.Engine.Executors.ExecutorSet(registry, _h.Services, _h.Power, _h.Commands, _h.Environment.SystemDirectory);
        return new TweakEngine(_catalog, executors, new StateStore(Path.Combine(_h.StateRoot, "Policy"), new FakeTime()), new FakeEnvironment { IsElevated = elevated }, restorePoints, new FakeTime());
    }

    private TweakEngine MachineEngine(IRestorePointService restorePoints, bool elevated)
    {
        var environment = new FakeEnvironment { IsElevated = elevated };
        return new TweakEngine(_catalog, _h.Executors, new StateStore(_machineRoot, new FakeTime()), environment, restorePoints, new FakeTime());
    }

    private sealed class MaintenanceRunner : ICommandRunner
    {
        public List<string> Calls { get; } = [];

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add(Path.GetFileName(executable) + " " + string.Join(' ', arguments));
            return new CommandResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class OneSsd : TweakPilot.Engine.Tools.IVolumeInventory
    {
        public IReadOnlyList<TweakPilot.Engine.Tools.VolumeInfo> FixedVolumes() => [new('C', TweakPilot.Engine.Tools.MediaKind.Ssd, 100, 50, "SSD")];
    }

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, value.GetType(), CoreJson.Options);
}
