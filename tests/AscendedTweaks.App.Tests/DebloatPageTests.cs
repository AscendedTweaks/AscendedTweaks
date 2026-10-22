using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Debloat;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class DebloatPageTests
{
    private readonly FakeApps _apps = new();
    private readonly Localizer _localizer = new(Localizer.German);

    [Fact]
    public async Task Apps_are_grouped_nothing_is_preselected_and_system_apps_are_locked()
    {
        var vm = new AppsViewModel(_apps, new ScriptedDialogs(_ => true), _localizer, 26200, readOnly: false);

        await vm.LoadAsync();

        vm.Groups.Select(g => g.Title).Should().Contain(["Nachrichten und Info", "Weitere Apps (Hersteller und Drittanbieter)", "Systemapps (gesperrt)"]);
        vm.Groups.SelectMany(g => g.Apps).Should().OnlyContain(a => !a.IsSelected);
        var store = vm.Groups.Single(g => g.Title == "Systemapps (gesperrt)").Apps.Single();
        store.CanSelect.Should().BeFalse();
        store.LockReason.Should().Contain("Systemapp");
        var vendor = vm.Groups.Single(g => g.Title.StartsWith("Weitere", StringComparison.Ordinal)).Apps;
        vendor.Single(a => a.Name == "RealtekSemiconductorCorp.RealtekAudioControl").Warning.Should().NotBeNull("Treiber-App bekommt einen Hinweis");
        vm.Groups.SelectMany(g => g.Apps).Single(a => a.Row.Catalog?.Id == "DEB-004").CanSelect.Should().BeFalse("nicht installiert");
        vm.CanRemove.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_asks_with_restore_note_and_sends_only_package_names()
    {
        var dialogs = new ScriptedDialogs(_ => true);
        var vm = new AppsViewModel(_apps, dialogs, _localizer, 26200, readOnly: false);
        await vm.LoadAsync();
        vm.Groups.SelectMany(g => g.Apps).Single(a => a.Row.Catalog?.Id == "DEB-001").IsSelected = true;
        vm.Deprovision = true;

        await vm.RemoveCommand.ExecuteAsync(null);

        dialogs.Last<ConfirmDialogViewModel>()!.Body.Should().Contain("Microsoft Nachrichten").And.Contain("Nicht jede Wiederherstellung ist garantiert").And.Contain("Bereitstellungspaket");
        _apps.Removed.Should().ContainSingle();
        _apps.Removed[0].Names.Should().Equal("Microsoft.BingNews");
        _apps.Removed[0].Deprovision.Should().BeTrue();
        dialogs.Last<ResultDialogViewModel>()!.Rows.Should().ContainSingle(r => r.Ok);
    }

    [Fact]
    public async Task Restore_page_lists_removed_apps_and_restores()
    {
        using var h = new AscendedTweaks.Engine.Tests.Harness();
        _apps.RemovedList.Add(new RemovedApp { PackageName = "Microsoft.BingNews", FamilyName = "Microsoft.BingNews_x", FullName = "Microsoft.BingNews_1_x", RemovedUtc = DateTimeOffset.UnixEpoch, Ok = true });
        var dialogs = new ScriptedDialogs(_ => true);
        var workspace = Workspace(h, dialogs);
        var page = new RestorePageViewModel(workspace, dialogs, _apps);

        await page.LoadAsync();
        page.HasRemovedApps.Should().BeTrue();
        await page.RestoreAppCommand.ExecuteAsync(page.RemovedApps.Single());

        _apps.Restored.Should().Equal("Microsoft.BingNews_x");
        dialogs.Last<ResultDialogViewModel>()!.Summary.Should().Contain("Store-Seite");
    }

    [Fact]
    public async Task OneDrive_view_explains_blocks_and_never_uninstalls_when_blocked()
    {
        var uninstalls = 0;
        var blocked = new OneDriveStatus(true, @"C:\x\OneDriveSetup.exe", [OneDriveBlock.KnownFolderMove, OneDriveBlock.OnlineOnlyFiles], ["Desktop"], 7);
        var vm = new OneDriveViewModel(() => blocked, () => { uninstalls++; return blocked; }, new ScriptedDialogs(_ => true), _localizer, readOnly: false);

        await vm.LoadAsync();
        vm.CanUninstall.Should().BeFalse();
        vm.Reasons.Should().HaveCount(2);
        vm.Reasons[0].Should().Contain("Desktop");
        vm.Reasons[1].Should().Contain("7");
        await vm.UninstallCommand.ExecuteAsync(null);
        uninstalls.Should().Be(0);

        var ready = new OneDriveStatus(true, @"C:\x\OneDriveSetup.exe", [], [], 0);
        var dialogs = new ScriptedDialogs(_ => true);
        var ok = new OneDriveViewModel(() => ready, () => { uninstalls++; return new OneDriveStatus(false, null, [OneDriveBlock.NotInstalled], [], 0); }, dialogs, _localizer, readOnly: false);
        await ok.LoadAsync();
        await ok.UninstallCommand.ExecuteAsync(null);
        uninstalls.Should().Be(1);
        dialogs.Last<ConfirmDialogViewModel>()!.IsDanger.Should().BeTrue();
        ok.StatusText.Should().Be("OneDrive ist nicht installiert.");
    }

    private TweakWorkspace Workspace(AscendedTweaks.Engine.Tests.Harness h, ScriptedDialogs dialogs)
    {
        var catalog = new AscendedTweaks.Catalog.TweakCatalog([], new Dictionary<string, AscendedTweaks.Catalog.TextBundle>
        {
            [Localizer.German] = new(Localizer.German, new Dictionary<string, string>()),
            [Localizer.English] = new(Localizer.English, new Dictionary<string, string>()),
        });
        AscendedTweaks.Engine.TweakEngine Engine(string name) =>
            new(catalog, h.Executors, new AscendedTweaks.Engine.State.StateStore(Path.Combine(h.StateRoot, name), new AscendedTweaks.Engine.Tests.FakeTime()), h.Environment, new AscendedTweaks.Engine.NoRestorePointService(), new AscendedTweaks.Engine.Tests.FakeTime());
        var service = new AscendedTweaks.Ipc.TweakService(catalog, Engine("u"), Engine("m"), Engine("p"), () => throw new InvalidOperationException(), new AscendedTweaks.Engine.Tests.FakeTime());
        return new TweakWorkspace(catalog, service, _localizer, new AppSettings(), dialogs, new SyncInvoker(), new CountingRestarter(), readOnly: false);
    }

    private sealed class FakeApps : IAppManager
    {
        public List<(string[] Names, bool Deprovision)> Removed { get; } = [];

        public List<RemovedApp> RemovedList { get; } = [];

        public List<string> Restored { get; } = [];

        public IReadOnlyList<AppxPackageInfo> ListInstalled() =>
        [
            Pkg("Microsoft.BingNews", true),
            Pkg("Microsoft.WindowsStore", true),
            Pkg("RealtekSemiconductorCorp.RealtekAudioControl", false, "Realtek Semiconductor Corp"),
            Pkg("Hersteller.Spiel", false, "Spielefirma"),
        ];

        public IReadOnlyList<RemovedApp> Remove(IReadOnlyList<string> packageNames, bool deprovision)
        {
            Removed.Add((packageNames.ToArray(), deprovision));
            return packageNames.Select(n => new RemovedApp { PackageName = n, FamilyName = n + "_x", FullName = n + "_1_x", Ok = true }).ToList();
        }

        public IReadOnlyList<RemovedApp> RemovedApps() => RemovedList;

        public RestoreOutcome Restore(RemovedApp app)
        {
            Restored.Add(app.FamilyName);
            return RestoreOutcome.StoreOpened;
        }

        private static AppxPackageInfo Pkg(string name, bool microsoft, string publisher = "Microsoft Corporation") =>
            new(name, name + "_1_x64__x", name + "_x", publisher, microsoft, false, false, false, null);
    }
}
