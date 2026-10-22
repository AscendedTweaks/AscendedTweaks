using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Engine.Update;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class Phase13PageTests
{
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly FakeLogs _logs = new();
    private readonly FakeUpdates _updates = new();

    [Fact]
    public async Task Logs_are_measured_and_deleted_only_after_confirmation()
    {
        var no = Maintenance(new ScriptedDialogs(_ => false));
        no.LogText.Should().Be("3 Dateien, 2 KB");
        await no.DeleteLogsCommand.ExecuteAsync(null);
        _logs.Deleted.Should().Be(0);

        var dialogs = new ScriptedDialogs(_ => true);
        var yes = Maintenance(dialogs);
        await yes.DeleteLogsCommand.ExecuteAsync(null);
        _logs.Deleted.Should().Be(1);
        yes.LogText.Should().StartWith("0 Dateien").And.Contain("1 Dateien sind gerade in Benutzung");
        dialogs.Last<ConfirmDialogViewModel>()!.IsDanger.Should().BeTrue();
    }

    [Fact]
    public async Task Update_check_is_honest_when_not_configured()
    {
        var vm = Maintenance(new ScriptedDialogs(_ => true));

        await vm.CheckUpdateCommand.ExecuteAsync(null);

        vm.UpdateText.Should().Contain("noch nicht eingerichtet");
        vm.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task Installer_starts_only_after_verified_download_and_second_confirmation()
    {
        _updates.Result = new UpdateCheckResult(UpdateCheckOutcome.Available, Manifest(), null);
        var declined = Maintenance(new ScriptedDialogs(_ => false));
        await declined.CheckUpdateCommand.ExecuteAsync(null);
        declined.UpdateText.Should().Contain("1.0.0");
        await declined.InstallUpdateCommand.ExecuteAsync(null);
        _updates.Started.Should().BeNull("Bestaetigung abgelehnt");

        var dialogs = new ScriptedDialogs(_ => true);
        var accepted = Maintenance(dialogs);
        await accepted.CheckUpdateCommand.ExecuteAsync(null);
        await accepted.InstallUpdateCommand.ExecuteAsync(null);
        _updates.Started.Should().Be(@"C:\Temp\setup.exe");
        dialogs.Last<ConfirmDialogViewModel>()!.Body.Should().Contain("Signatur").And.Contain("Neu: Test");

        _updates.Started = null;
        _updates.Download = new UpdateDownload(false, null, "Pruefsumme falsch");
        var failed = Maintenance(new ScriptedDialogs(_ => true));
        await failed.CheckUpdateCommand.ExecuteAsync(null);
        await failed.InstallUpdateCommand.ExecuteAsync(null);
        failed.UpdateText.Should().Contain("Pruefsumme falsch");
        _updates.Started.Should().BeNull();
    }

    [Fact]
    public async Task Reset_needs_confirmation()
    {
        var resets = 0;
        var vm = new SettingsMaintenanceViewModel(_logs, _updates, new ScriptedDialogs(_ => true), _localizer, () => resets++);

        await vm.ResetSettingsCommand.ExecuteAsync(null);

        resets.Should().Be(1);
        vm.CliText.Should().Contain("ascendedtweaks-cli.exe").And.Contain("--allow-expert");
    }

    private SettingsMaintenanceViewModel Maintenance(ScriptedDialogs dialogs) => new(_logs, _updates, dialogs, _localizer, () => { });

    private static UpdateManifest Manifest() => new()
    {
        Version = "1.0.0",
        InstallerUrl = new Uri("https://updates.example.test/setup.exe"),
        Sha256 = new string('0', 64),
        Size = 1,
        Notes = "Neu: Test",
    };

    private sealed class FakeLogs : ILogFolder
    {
        public int Deleted { get; private set; }

        public (int Files, long Bytes) Measure() => Deleted == 0 ? (3, 2048) : (0, 0);

        public int Delete()
        {
            Deleted++;
            return 1;
        }

        public void Open()
        {
        }
    }

    private sealed class FakeUpdates : IUpdateService
    {
        public UpdateCheckResult Result { get; set; } = new(UpdateCheckOutcome.NotConfigured, null, null);

        public UpdateDownload Download { get; set; } = new(true, @"C:\Temp\setup.exe", null);

        public string? Started { get; set; }

        public Task<UpdateCheckResult> CheckAsync() => Task.FromResult(Result);

        public Task<UpdateDownload> DownloadAsync(UpdateManifest manifest) => Task.FromResult(Download);

        public void StartInstaller(string path) => Started = path;
    }
}
