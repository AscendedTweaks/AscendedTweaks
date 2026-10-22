using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class PermissionTests
{
    [Fact]
    public async Task Overview_shows_effective_state_and_blocked_capabilities()
    {
        var data = new List<PermissionCapability>
        {
            new("webcam", ConsentState.Allow, ConsentState.Allow, ConsentState.Deny,
            [
                new PermissionApp("cam.exe", @"C:\cam.exe", true, ConsentState.Inherited, null),
                new PermissionApp("Camera", null, false, ConsentState.Allow, DateTimeOffset.UnixEpoch),
            ]),
            new("location", ConsentState.Deny, ConsentState.Allow, ConsentState.Inherited,
            [
                new PermissionApp("Maps", null, false, ConsentState.Allow, null),
            ]),
        };
        string? opened = null;
        var vm = new PermissionOverviewViewModel(() => data, new Localizer(Localizer.German), u => opened = u);

        await vm.LoadAsync();

        vm.Error.Should().BeNull();
        vm.Groups.Should().HaveCount(2);
        vm.Groups[0].Title.Should().Be("Kamera");
        vm.Groups[0].Summary.Should().Be("Apps mit Zugriff: 1");
        vm.Groups[0].Apps.Select(a => a.State).Should().Equal("Gesperrt", "Erlaubt");
        vm.Groups[0].Apps[0].Detail.Should().Contain("Desktop-App").And.Contain(@"C:\cam.exe");
        vm.Groups[1].IsBlocked.Should().BeTrue();
        vm.Groups[1].Summary.Should().Be("Für das ganze Gerät gesperrt");
        vm.Groups[1].Apps.Single().IsAllowed.Should().BeFalse("die Gerätesperre gilt für alle Apps");

        vm.OpenSettingsCommand.Execute(null);
        opened.Should().Be("ms-settings:privacy");
    }

    [Fact]
    public async Task Read_error_is_shown_instead_of_crashing()
    {
        var vm = new PermissionOverviewViewModel(() => throw new UnauthorizedAccessException("verweigert"), new Localizer(Localizer.English), _ => { });

        await vm.LoadAsync();

        vm.Error.Should().Contain("verweigert");
        vm.IsLoading.Should().BeFalse();
    }
}
