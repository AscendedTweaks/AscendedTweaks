using System.Security.AccessControl;
using System.Security.Principal;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Logging;
using AscendedTweaks.Engine.Windows;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class Phase2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Log_replaces_user_name_and_profile_and_rotates()
    {
        var log = new JsonLinesLog(_dir, "test", new FakeTime(), maxBytes: 400, maxFiles: 3, userName: "alex", userProfile: @"C:\Users\alex");

        for (var i = 0; i < 20; i++)
        {
            log.Write(new LogRecord { Event = "E", Target = @"C:\Users\alex\AppData\x", Error = "Benutzer alex" });
        }

        log.Write(new LogRecord { Event = "Debug", Level = LogLevel.Debug });
        var files = Directory.GetFiles(_dir);
        files.Should().HaveCount(3);
        var text = string.Concat(files.Select(File.ReadAllText));
        text.Should().NotContain("alex").And.Contain("%USERPROFILE%").And.Contain("%USERNAME%");
        text.Should().NotContain("Debug");

        log.Clear();
        Directory.GetFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public void Engine_writes_log_records_for_apply_and_revert()
    {
        using var h = new Harness();
        h.Registry.Seed(RegHive.HKCU, TestTweaks.Base, "One", RegistryData.DWord(1));
        var sink = new ListSink();
        var engine = new TweakEngine(new ListCatalog(TestTweaks.Dword("PRIV-901", TestTweaks.Base, "One", 0)), h.Executors, h.Store, h.Environment, h.RestorePoints, new FakeTime(), sink);

        engine.ApplyBatch(["PRIV-901"], new ApplyOptions());
        engine.RevertTweak("PRIV-901", _ => ConflictDecision.Cancel);

        sink.Records.Select(r => r.Event).Should().Equal("TweakApplied", "BatchCommitted", "TweakReverted");
        sink.Records[0].OldValue.Should().Be("DWord 1 (0x1)");
        sink.Records[0].NewValue.Should().Be("DWord 0 (0x0)");
    }

    [Fact]
    public void Preview_ignores_admin_gate_but_plan_does_not()
    {
        using var h = new Harness();
        h.Environment.IsElevated = false;
        var engine = h.Engine(TestTweaks.Dword("PERF-901", @"SOFTWARE\AscendedTweaksTest", "X", 0, RegHive.HKLM));

        engine.PlanPreview(["PERF-901"]).Ready.Should().ContainSingle();
        engine.Plan(["PERF-901"]).Excluded.Single().Outcome.Should().Be(PreflightOutcome.AdminRequired);
    }

    [Fact]
    public void Helper_registry_never_touches_hkcu()
    {
        var inner = new FakeRegistry();
        var registry = new HelperRegistryAccess(inner);
        var data = RegistryData.DWord(1);

        registry.SetValue(RegHive.HKLM, @"SOFTWARE\A", "x", data);
        registry.GetValue(RegHive.HKLM, @"SOFTWARE\A", "x").Should().Be(data);
        registry.KeyExists(RegHive.HKLM, @"SOFTWARE\A").Should().BeTrue();
        registry.GetValueNames(RegHive.HKLM, @"SOFTWARE\A").Should().ContainSingle();
        registry.GetSubKeyNames(RegHive.HKLM, "SOFTWARE").Should().ContainSingle();
        registry.CreateKey(RegHive.HKLM, @"SOFTWARE\B");
        registry.DeleteEmptyKey(RegHive.HKLM, @"SOFTWARE\B");
        registry.DeleteValue(RegHive.HKLM, @"SOFTWARE\A", "x");
        registry.DeleteKeyTree(RegHive.HKLM, @"SOFTWARE\A");

        Action[] hkcu =
        [
            () => registry.GetValue(RegHive.HKCU, "Software", "x"),
            () => registry.SetValue(RegHive.HKCU, "Software", "x", data),
            () => registry.KeyExists(RegHive.HKCU, "Software"),
            () => registry.DeleteKeyTree(RegHive.HKCU, "Software"),
        ];
        foreach (var act in hkcu)
        {
            act.Should().Throw<AscendedTweaksException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        }

        inner.WriteLog.Should().NotContain(l => l.Contains("Software\\", StringComparison.Ordinal) && !l.Contains("SOFTWARE", StringComparison.Ordinal));
    }

    [Fact]
    public void State_directory_acl_gives_users_read_only()
    {
        var rules = StateDirectorySecurity.Create()
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToDictionary(r => (SecurityIdentifier)r.IdentityReference, r => r.FileSystemRights);

        rules.Should().HaveCount(3);
        rules[new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)].Should().Be(FileSystemRights.FullControl);
        rules[new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)].Should().Be(FileSystemRights.FullControl);
        rules[new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)].Should().Be(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
    }

    [Fact]
    public void Preset_restore_point_is_acceptable()
    {
        new PresetRestorePointService(new RestorePointResult(RestorePointStatus.HandledSeparately, "x")).TryCreate("y").IsAcceptable.Should().BeTrue();
        new NoRestorePointService().TryCreate("y").IsAcceptable.Should().BeFalse();
    }

    private sealed class ListSink : ILogSink
    {
        public List<LogRecord> Records { get; } = [];

        public void Write(LogRecord record) => Records.Add(record);
    }
}
