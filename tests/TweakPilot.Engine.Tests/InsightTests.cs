using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class InsightTests
{
    private const string Store = PermissionOverview.ConsentStorePath;

    [Fact]
    public void Permission_overview_reads_store_and_desktop_apps_without_writing()
    {
        var registry = new FakeRegistry();
        var used = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        registry.Seed(RegHive.HKLM, Store + @"\webcam", "Value", RegistryData.String("Allow"));
        registry.Seed(RegHive.HKCU, Store + @"\webcam", "Value", RegistryData.String("Allow"));
        registry.Seed(RegHive.HKCU, Store + @"\webcam\Microsoft.WindowsCamera_8wekyb3d8bbwe", "Value", RegistryData.String("Prompt"));
        registry.Seed(RegHive.HKCU, Store + @"\webcam\NonPackaged", "Value", RegistryData.String("Deny"));
        registry.Seed(RegHive.HKCU, Store + @"\webcam\NonPackaged\C:#Users#max#Apps#cam.exe", "LastUsedTimeStart", RegistryData.QWord((ulong)used.ToFileTime()));
        registry.Seed(RegHive.HKLM, Store + @"\location", "Value", RegistryData.String("Deny"));
        var writes = registry.WriteCount;

        var result = new PermissionOverview(registry, @"C:\Users\max\").Read();

        registry.WriteCount.Should().Be(writes, "die Uebersicht ist reine Anzeige");
        result.Select(c => c.Capability).Should().Equal("webcam", "microphone", "location");
        var webcam = result[0];
        webcam.Device.Should().Be(ConsentState.Allow);
        webcam.DesktopApps.Should().Be(ConsentState.Deny);
        webcam.Apps.Should().HaveCount(2);
        webcam.Apps[0].Should().Be(new PermissionApp("cam.exe", @"%USERPROFILE%\Apps\cam.exe", true, ConsentState.Inherited, used));
        webcam.Apps[1].Name.Should().Be("Microsoft.WindowsCamera");
        webcam.Apps[1].State.Should().Be(ConsentState.Prompt);
        result[1].Apps.Should().BeEmpty();
        result[1].User.Should().Be(ConsentState.Inherited);
        result[2].Device.Should().Be(ConsentState.Deny);
    }

    [Fact]
    public void Permission_overview_ignores_broken_timestamps()
    {
        var registry = new FakeRegistry();
        registry.Seed(RegHive.HKCU, Store + @"\microphone\App_x", "LastUsedTimeStart", RegistryData.QWord(ulong.MaxValue));
        registry.Seed(RegHive.HKCU, Store + @"\microphone\Other_y", "LastUsedTimeStart", RegistryData.String("kaputt"));

        var apps = new PermissionOverview(registry).Read()[1].Apps;

        apps.Should().HaveCount(2).And.OnlyContain(a => a.LastUsed == null);
    }
}
