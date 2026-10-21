using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Microsoft.Win32;
using TweakPilot.Core;
using TweakPilot.Engine.Executors;
using TweakPilot.Engine.State;
using TweakPilot.Engine.Windows;
using Xunit;

namespace TweakPilot.Engine.Tests;

/// <summary>
/// Echte Registry, aber nur in einem eigenen Testzweig unter HKCU\Software\TweakPilot.Tests.
/// Prueft WindowsRegistryAccess und den Rollback mit einem echten Zugriffsfehler.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RegistryIntegrationTests : IDisposable
{
    private readonly string _root = $@"Software\TweakPilot.Tests\{Guid.NewGuid():N}";
    private readonly string _state = Path.Combine(Path.GetTempPath(), "TweakPilot.Tests", Guid.NewGuid().ToString("N"));
    private readonly WindowsRegistryAccess _registry = new();

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\TweakPilot.Tests", writable: true))
        {
            if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\TweakPilot.Tests", throwOnMissingSubKey: false);
            }
        }

        if (Directory.Exists(_state))
        {
            Directory.Delete(_state, recursive: true);
        }
    }

    [Fact]
    public void All_value_kinds_roundtrip_through_real_registry()
    {
        RegistryData[] values =
        [
            RegistryData.DWord(0xFFFFFFFF),
            RegistryData.QWord(864000000000),
            RegistryData.String("ä"),
            RegistryData.ExpandString("%TEMP%"),
            RegistryData.MultiString(["a", "b"]),
            RegistryData.Binary([1, 2]),
        ];
        for (var i = 0; i < values.Length; i++)
        {
            _registry.SetValue(RegHive.HKCU, _root + @"\Kinds", $"v{i}", values[i]);
        }

        for (var i = 0; i < values.Length; i++)
        {
            _registry.GetValue(RegHive.HKCU, _root + @"\Kinds", $"v{i}").Should().Be(values[i]);
        }

        _registry.GetValueNames(RegHive.HKCU, _root + @"\Kinds").Should().HaveCount(values.Length);
        _registry.GetSubKeyNames(RegHive.HKCU, _root).Should().Equal("Kinds");
        _registry.DeleteValue(RegHive.HKCU, _root + @"\Kinds", "v0");
        _registry.GetValue(RegHive.HKCU, _root + @"\Kinds", "v0").Should().BeNull();
        _registry.DeleteKeyTree(RegHive.HKCU, _root + @"\Kinds");
        _registry.KeyExists(RegHive.HKCU, _root + @"\Kinds").Should().BeFalse();
        _registry.GetValue(RegHive.HKCU, _root + @"\Fehlt", "x").Should().BeNull();
        _registry.GetValueNames(RegHive.HKCU, _root + @"\Fehlt").Should().BeEmpty();
    }

    [Fact]
    public void Invalid_path_is_rejected_before_any_access()
    {
        var act = () => _registry.SetValue(RegHive.HKCU, @"Software\..\X", "n", RegistryData.DWord(1));

        act.Should().Throw<TweakPilotException>().Which.Kind.Should().Be(ErrorKind.CatalogInvalid);
    }

    [Fact]
    public void Real_access_denied_in_third_tweak_rolls_back_to_identical_tree()
    {
        var locked = _root + @"\Locked";
        _registry.SetValue(RegHive.HKCU, _root, "One", RegistryData.DWord(1));
        _registry.CreateKey(RegHive.HKCU, locked);
        DenySetValue(locked);
        var before = Dump(_root);

        var engine = new TweakEngine(
            new ListCatalog(
                TestTweaks.Dword("PRIV-901", _root, "One", 0),
                TestTweaks.TwoDwords("PRIV-902", _root + @"\Neu", "A", "B"),
                TestTweaks.Dword("PRIV-903", locked, "C", 0)),
            new ExecutorSet(_registry, new FakeServices(), new FakePower(), new FakeCommands(), Environment.SystemDirectory),
            new StateStore(_state, TimeProvider.System),
            new FakeEnvironment(),
            new FakeRestorePoints(),
            TimeProvider.System);

        var result = engine.ApplyBatch(["PRIV-901", "PRIV-902", "PRIV-903"], new ApplyOptions());

        result.Outcome.Should().Be(BatchOutcome.RolledBack);
        result.FailedTweakId.Should().Be("PRIV-903");
        result.Error!.Kind.Should().Be(ErrorKind.PermissionDenied);
        Dump(_root).Should().Equal(before);
    }

    private static void DenySetValue(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadKey)!;
        var security = key.GetAccessControl();
        security.AddAccessRule(new RegistryAccessRule(WindowsIdentity.GetCurrent().User!, RegistryRights.SetValue, AccessControlType.Deny));
        key.SetAccessControl(security);
    }

    private List<string> Dump(string path)
    {
        var lines = new List<string> { "K " + path };
        lines.AddRange(_registry.GetValueNames(RegHive.HKCU, path).Order().Select(n => $"V {path}|{n}={_registry.GetValue(RegHive.HKCU, path, n)}"));
        foreach (var sub in _registry.GetSubKeyNames(RegHive.HKCU, path).Order())
        {
            lines.AddRange(Dump(path + "\\" + sub));
        }

        return lines;
    }
}
