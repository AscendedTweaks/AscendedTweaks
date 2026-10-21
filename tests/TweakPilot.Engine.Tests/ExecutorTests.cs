using FluentAssertions;
using TweakPilot.Core;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class ExecutorTests : IDisposable
{
    private const string ClsidPath = @"Software\Classes\CLSID\{11111111-2222-3333-4444-555555555555}\InprocServer32";
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void SetValue_on_missing_key_creates_and_restore_removes_created_keys()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        var before = _h.Registry.Snapshot();
        var action = new RegistrySetValueAction { Hive = RegHive.HKCU, Path = @"Software\A\B", Name = "X", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } };
        var executor = _h.Executors.For(action);

        var capture = executor.Capture(action);
        ((RegistryValueCapture)capture).MissingKeys.Should().Equal(@"Software\A", @"Software\A\B");
        var written = executor.Apply(action, capture);

        executor.Verify(action).Should().BeTrue();
        executor.IsAtWrittenState(written).Should().BeTrue();
        executor.IsAtOriginalState(written).Should().BeFalse();

        executor.Restore(written);
        executor.IsAtOriginalState(written).Should().BeTrue();
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void SetValue_restores_original_kind_and_value()
    {
        _h.Registry.Seed(RegHive.HKCU, @"Software\A", "X", RegistryData.String("alt"));
        var before = _h.Registry.Snapshot();
        var action = new RegistrySetValueAction { Hive = RegHive.HKCU, Path = @"Software\A", Name = "X", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 1 } };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        _h.Registry.GetValue(RegHive.HKCU, @"Software\A", "X").Should().Be(RegistryData.DWord(1));

        executor.Restore(written);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Restore_does_not_write_when_already_original()
    {
        _h.Registry.Seed(RegHive.HKCU, @"Software\A", "X", RegistryData.DWord(1));
        var action = new RegistrySetValueAction { Hive = RegHive.HKCU, Path = @"Software\A", Name = "X", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } };
        var executor = _h.Executors.For(action);
        var capture = executor.Capture(action);

        executor.Restore(capture);

        _h.Registry.WriteCount.Should().Be(0);
    }

    [Fact]
    public void DeleteValue_roundtrip()
    {
        _h.Registry.Seed(RegHive.HKCU, @"Software\A", "X", RegistryData.MultiString(["a", "b"]));
        var before = _h.Registry.Snapshot();
        var action = new RegistryDeleteValueAction { Hive = RegHive.HKCU, Path = @"Software\A", Name = "X" };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        executor.Verify(action).Should().BeTrue();
        executor.IsAtWrittenState(written).Should().BeTrue();
        executor.Describe("T", action).New.Should().Contain("removed");

        executor.Restore(written);
        executor.IsAtOriginalState(written).Should().BeTrue();
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void CreateKey_roundtrip_keeps_preexisting_parent()
    {
        _h.Registry.SeedKey(RegHive.HKCU, @"Software\Classes\CLSID");
        var before = _h.Registry.Snapshot();
        var action = new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = ClsidPath, DefaultValue = string.Empty };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        executor.Verify(action).Should().BeTrue();
        executor.IsAtWrittenState(written).Should().BeTrue();
        _h.Registry.GetValue(RegHive.HKCU, ClsidPath, string.Empty).Should().Be(RegistryData.String(string.Empty));

        executor.Restore(written);
        executor.IsAtOriginalState(written).Should().BeTrue();
        _h.Registry.Snapshot().Should().Be(before);
        _h.Registry.KeyExists(RegHive.HKCU, @"Software\Classes\CLSID").Should().BeTrue();
    }

    [Fact]
    public void CreateKey_outside_allowlist_is_refused()
    {
        var action = new RegistryCreateKeyAction { Hive = RegHive.HKCU, Path = @"Software\Fremd\X" };
        var executor = _h.Executors.For(action);

        var act = () => executor.Apply(action, executor.Capture(action));

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.CatalogInvalid);
        _h.Registry.WriteCount.Should().Be(0);
    }

    [Fact]
    public void DeleteKey_restores_full_tree()
    {
        const string path = @"Software\Microsoft\TweakPilotTest\Tree";
        _h.Registry.Seed(RegHive.HKCU, path, "A", RegistryData.DWord(7));
        _h.Registry.Seed(RegHive.HKCU, path + @"\Sub", "B", RegistryData.Binary([1, 2, 3]));
        var before = _h.Registry.Snapshot();
        var action = new RegistryDeleteKeyAction { Hive = RegHive.HKCU, Path = path };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        executor.Verify(action).Should().BeTrue();
        executor.IsAtWrittenState(written).Should().BeTrue();
        executor.Describe("T", action).Current.Should().Be("(not present)");

        executor.Restore(written);
        executor.IsAtOriginalState(written).Should().BeTrue();
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Service_roundtrip_stops_and_restarts()
    {
        _h.Services.Services["Fax"] = new ServiceConfig(ServiceStartType.Manual, false, true);
        var action = new ServiceSetStartTypeAction { Service = "Fax", StartType = ServiceStartSetting.Disabled, StopNow = true };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        _h.Services.Services["Fax"].Should().Be(new ServiceConfig(ServiceStartType.Disabled, false, false));
        executor.Verify(action).Should().BeTrue();
        executor.IsAtWrittenState(written).Should().BeTrue();
        executor.View(written).Original.Should().Be("Manual");

        executor.Restore(written);
        _h.Services.Services["Fax"].Should().Be(new ServiceConfig(ServiceStartType.Manual, false, true));
        executor.IsAtOriginalState(written).Should().BeTrue();
    }

    [Fact]
    public void Service_delayed_start_is_kept()
    {
        _h.Services.Services["Svc"] = new ServiceConfig(ServiceStartType.Automatic, true, false);
        var action = new ServiceSetStartTypeAction { Service = "Svc", StartType = ServiceStartSetting.Manual };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        executor.Restore(written);

        _h.Services.Services["Svc"].Should().Be(new ServiceConfig(ServiceStartType.Automatic, true, false));
    }

    [Fact]
    public void Missing_service_is_unavailable()
    {
        var action = new ServiceSetStartTypeAction { Service = "Fehlt", StartType = ServiceStartSetting.Disabled };
        var act = () => _h.Executors.For(action).Capture(action);

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.Unavailable);
    }

    [Fact]
    public void PowerPlan_roundtrip()
    {
        var balanced = Guid.NewGuid();
        var high = Guid.NewGuid();
        _h.Power.Schemes.AddRange([balanced, high]);
        _h.Power.Active = balanced;
        var action = new PowerPlanSetAction { SchemeGuid = high };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        _h.Power.Active.Should().Be(high);
        executor.Verify(action).Should().BeTrue();
        executor.Describe("T", action).Current.Should().Be(high.ToString());

        executor.Restore(written);
        _h.Power.Active.Should().Be(balanced);
        executor.IsAtOriginalState(written).Should().BeTrue();
    }

    [Fact]
    public void PowerPlan_duplicate_is_removed_on_restore()
    {
        var balanced = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        _h.Power.Schemes.AddRange([balanced, source]);
        _h.Power.Active = balanced;
        var action = new PowerPlanSetAction { SchemeGuid = target, DuplicateFrom = source };
        var executor = _h.Executors.For(action);

        executor.Describe("T", action).New.Should().Contain("created from");
        var written = executor.Apply(action, executor.Capture(action));
        ((PowerPlanCapture)written).CreatedScheme.Should().Be(target);

        executor.Restore(written);
        _h.Power.Schemes.Should().Equal(balanced, source);
        _h.Power.Active.Should().Be(balanced);
    }

    [Fact]
    public void PowerPlan_missing_without_duplicate_is_unavailable()
    {
        _h.Power.Schemes.Add(Guid.NewGuid());
        var action = new PowerPlanSetAction { SchemeGuid = Guid.NewGuid() };
        var executor = _h.Executors.For(action);

        var act = () => executor.Apply(action, executor.Capture(action));

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.Unavailable);
    }

    [Fact]
    public void Command_roundtrip_uses_system_directory_and_argument_array()
    {
        var action = new CommandAction { Command = "fsutil.disablelastaccess", Value = "1" };
        var executor = _h.Executors.For(action);

        var written = executor.Apply(action, executor.Capture(action));
        _h.Commands.LastAccessValue.Should().Be("1");
        executor.Verify(action).Should().BeTrue();
        _h.Commands.Calls.Should().OnlyContain(c => c.Exe == @"C:\Windows\System32\fsutil.exe");
        executor.Describe("T", action).Target.Should().Be("fsutil.exe behavior set disablelastaccess 1");

        executor.Restore(written);
        _h.Commands.LastAccessValue.Should().Be("2");
        executor.IsAtOriginalState(written).Should().BeTrue();
    }

    [Fact]
    public void Command_value_outside_whitelist_is_refused()
    {
        var action = new CommandAction { Command = "fsutil.disablelastaccess", Value = "1 & del" };
        var executor = _h.Executors.For(action);

        var act = () => executor.Apply(action, executor.Capture(action));

        act.Should().Throw<TweakPilotException>();
        _h.Commands.WriteCount.Should().Be(0);
    }

    [Fact]
    public void Command_unreadable_output_is_unavailable()
    {
        _h.Commands.BrokenOutput = true;
        var action = new CommandAction { Command = "fsutil.disablelastaccess", Value = "1" };

        var act = () => _h.Executors.For(action).Capture(action);

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.Unavailable);
    }

    [Fact]
    public void Unknown_command_is_refused()
    {
        var action = new CommandAction { Command = "cmd.exe", Value = "x" };

        var act = () => _h.Executors.For(action).Capture(action);

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.CatalogInvalid);
    }
}
