using System.IO;
using System.Text;
using FluentAssertions;
using TweakPilot.App.Services;
using TweakPilot.App.ViewModels;
using TweakPilot.Catalog;
using TweakPilot.Core;
using TweakPilot.Engine.Tests;
using TweakPilot.Localization;
using Xunit;

namespace TweakPilot.App.Tests;

public sealed class Phase11PageTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly TweakCatalog _catalog;
    private readonly string _profiles = Path.Combine(Path.GetTempPath(), "TweakPilot.Tests", "profiles-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFiles _files = new();

    public Phase11PageTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        _h.Registry.Seed(RegHive.HKCU, P, "A", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "B", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "C", RegistryData.DWord(0));
        _h.Registry.Seed(RegHive.HKCU, P, "D", RegistryData.DWord(1));
        _h.Registry.Seed(RegHive.HKCU, P, "E", RegistryData.DWord(0));
        TweakDefinition[] tweaks =
        [
            TestTweaks.Dword("PRIV-901", P, "A", 0),
            TestTweaks.Create(
                "PRIV-902",
                new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = "B", Value = new RegLiteral { Number = 0 } },
                [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = "B", Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
                evidence: EvidenceLevel.Disputed,
                hidden: true),
            TestTweaks.Dword("PRIV-903", P, "C", 0),
            TestTweaks.Dword("PERF-905", P, "D", 0),
            TestTweaks.Dword("PRIV-904", P, "E", 0),
        ];
        var texts = tweaks.SelectMany(t => new[] { t.NameKey, t.DescriptionKey, t.SideEffectsKey }).ToDictionary(k => k, k => "Text " + k);
        _catalog = new TweakCatalog(tweaks, new Dictionary<string, TextBundle>
        {
            [Localizer.German] = new(Localizer.German, texts),
            [Localizer.English] = new(Localizer.English, texts),
        });
    }

    public void Dispose()
    {
        _h.Dispose();
        if (Directory.Exists(_profiles))
        {
            Directory.Delete(_profiles, recursive: true);
        }
    }

    [Fact]
    public async Task Planner_applies_skips_hidden_desktop_only_and_foreign_changes()
    {
        var workspace = Workspace(Dialogs());
        await workspace.RefreshAsync();
        var profile = new TweakProfile
        {
            Name = "Test",
            Tweaks =
            [
                new ProfileEntry { Id = "PRIV-901" },
                new ProfileEntry { Id = "PRIV-902" },
                new ProfileEntry { Id = "PRIV-903" },
                new ProfileEntry { Id = "PERF-905", Condition = ProfileCondition.DesktopOnly },
                new ProfileEntry { Id = "PRIV-904", Target = ProfileTarget.NotApplied },
            ],
        };

        var rows = ProfilePlanner.Plan(profile, workspace.Catalog, workspace.Service.GetStatus, workspace.NameOf, workspace.Text, _localizer, expertMode: false, hasBattery: true);

        rows.Select(r => (r.Id, r.Action)).Should().Equal(
            ("PRIV-901", ProfileAction.Apply),
            ("PRIV-902", ProfileAction.Skip),
            ("PRIV-903", ProfileAction.Unchanged),
            ("PERF-905", ProfileAction.Skip),
            ("PRIV-904", ProfileAction.Skip));
        rows[1].Text.Should().Contain("Expertenmodus");
        rows[3].Text.Should().Contain("Akku");
        rows[4].Text.Should().Contain("nicht von TweakPilot");
        rows[0].SideEffects.Should().Be("Text tweak.PRIV-901.side", "Nebenwirkungen stehen vor dem Anwenden in der Liste");

        var expert = ProfilePlanner.Plan(profile, workspace.Catalog, workspace.Service.GetStatus, workspace.NameOf, workspace.Text, _localizer, expertMode: true, hasBattery: false);
        expert.Select(r => r.Action).Should().Equal(ProfileAction.Apply, ProfileAction.Apply, ProfileAction.Unchanged, ProfileAction.Apply, ProfileAction.Skip);
    }

    [Fact]
    public async Task Applying_a_profile_shows_preview_and_a_before_after_comparison_then_reverts_own_changes()
    {
        var dialogs = Dialogs();
        var apply = new TweakProfile { Key = "test", Tweaks = [new ProfileEntry { Id = "PRIV-901" }] };
        var page = await Page(dialogs, apply);

        page.CanApply.Should().BeTrue();
        await page.ApplyCommand.ExecuteAsync(null);

        dialogs.Shown.OfType<PreviewDialogViewModel>().Should().ContainSingle("Vorschau vor dem Anwenden");
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(0);
        page.Comparison.Should().ContainSingle();
        (page.Comparison[0].Before, page.Comparison[0].After).Should().Be(("Aus", "Aktiv"));
        page.Metrics.Select(m => m.Before).Should().Equal("9", "1 KB", "2 KB");
        page.HasComparison.Should().BeTrue();
        page.Plan.Single().Action.Should().Be(ProfileAction.Unchanged);

        var revert = new TweakProfile { Key = "test", Tweaks = [new ProfileEntry { Id = "PRIV-901", Target = ProfileTarget.NotApplied }] };
        var second = await Page(Dialogs(), revert);
        second.Plan.Single().Action.Should().Be(ProfileAction.Revert);
        await second.ApplyCommand.ExecuteAsync(null);
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(1);
    }

    [Fact]
    public async Task Import_checks_the_file_and_only_previews()
    {
        var page = await Page(Dialogs());
        _files.Content = Encoding.UTF8.GetBytes("""{"schemaVersion":1,"name":"Fremd","tweaks":[{"id":"PRIV-901"}],"command":"cmd /c del"}""");

        page.ImportCommand.Execute(null);
        page.Message.Should().StartWith("Import abgelehnt.");
        page.Profiles.Should().NotContain(p => p.IsOwn);

        _files.Content = Encoding.UTF8.GetBytes("""{"schemaVersion":1,"name":"Fremd","tweaks":[{"id":"PRIV-901"}]}""");
        page.ImportCommand.Execute(null);
        page.Selected!.Name.Should().Be("Fremd");
        page.Selected.IsOwn.Should().BeTrue();
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(1, "Import wendet nichts an");
    }

    [Fact]
    public async Task Own_profiles_can_be_saved_renamed_exported_and_deleted()
    {
        var dialogs = Dialogs("Mein Profil");
        var page = await Page(dialogs);

        await page.SaveCurrentCommand.ExecuteAsync(null);
        page.Selected!.Name.Should().Be("Mein Profil");
        page.Selected.Profile.Tweaks.Select(t => t.Id).Should().Equal(["PRIV-903", "PRIV-904"], "nur aktive, sichtbare Tweaks");

        var renamed = await Page(Dialogs("Neuer Name"));
        renamed.Selected = renamed.Profiles.Single(p => p.IsOwn);
        await renamed.RenameCommand.ExecuteAsync(null);
        renamed.Selected!.Name.Should().Be("Neuer Name");

        renamed.ExportCommand.Execute(null);
        _files.SuggestedName.Should().Be("Neuer Name.tpprofile");
        ProfileCodec.Parse(_files.Written!, _catalog).Name.Should().Be("Neuer Name");

        await renamed.DeleteCommand.ExecuteAsync(null);
        renamed.Profiles.Should().NotContain(p => p.IsOwn);
        Directory.EnumerateFiles(_profiles).Should().BeEmpty();
    }

    [Fact]
    public async Task Script_export_writes_apply_and_reset_files_with_the_current_values_as_original()
    {
        var page = await Page(Dialogs(), new TweakProfile { Key = "test", Tweaks = [new ProfileEntry { Id = "PRIV-901" }, new ProfileEntry { Id = "PRIV-903" }] });

        await page.ExportScriptsCommand.ExecuteAsync(null);

        _files.Files.Keys.Select(Path.GetFileName).Should().BeEquivalentTo(["[preset.test.name].ps1", "[preset.test.name]-revert.ps1", "[preset.test.name].reg", "[preset.test.name]-revert.reg"]);
        var apply = Encoding.UTF8.GetString(_files.Files.Single(f => f.Key.EndsWith("name].ps1", StringComparison.Ordinal)).Value);
        apply.Should().Contain("# ---- PRIV-901").And.Contain("-Name 'A' -PropertyType DWord -Value 0").And.Contain("# Original value: HKEY_CURRENT_USER\\" + P + "\\A = 1 (DWord)");
        apply.Should().NotContain("IsInRole(", "nur Benutzer-Tweaks");
        var reset = Encoding.Unicode.GetString(_files.Files.Single(f => f.Key.EndsWith("revert.reg", StringComparison.Ordinal)).Value);
        reset.Should().Contain("\"A\"=dword:00000001").And.Contain("\"C\"=dword:00000000");
        page.Message.Should().Contain("2 Tweaks");
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(1, "Export aendert nichts");
    }

    [Fact]
    public void Name_dialog_accepts_only_valid_names()
    {
        var dialog = new TextInputDialogViewModel("T", "Name", string.Empty, "OK", "Abbrechen", ProfileCodec.IsValidName, "ungueltig");

        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();
        dialog.Text = new string('x', 61);
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();
        dialog.Error.Should().Be("ungueltig");
        dialog.Text = "Abend";
        dialog.ConfirmCommand.CanExecute(null).Should().BeTrue();
        dialog.Error.Should().BeNull();
    }

    [Fact]
    public async Task Updates_overview_explains_and_offers_direct_pause_only_in_expert_mode()
    {
        var dialogs = Dialogs();
        var workspace = Workspace(dialogs);
        var opened = new List<string>();
        var overview = new UpdatesOverviewViewModel(workspace, new FixedUpdates(new UpdateStatus(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), 11, "25H2")), opened.Add, () => new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));

        await overview.LoadAsync();
        overview.PauseText.Should().Contain("pausiert bis");
        overview.VersionText.Should().Contain("25H2");
        overview.ExpertMode.Should().BeFalse();
        overview.CanTarget.Should().BeFalse();

        await overview.PauseCommand.ExecuteAsync(null);
        dialogs.Shown.Should().BeEmpty("ohne Expertenmodus wird nichts angewendet");
        overview.OpenWindowsUpdateCommand.Execute(null);
        opened.Should().Equal("ms-settings:windowsupdate");
    }

    private async Task<ProfilesPageViewModel> Page(ScriptedDialogs dialogs, params TweakProfile[] builtIn)
    {
        var workspace = Workspace(dialogs);
        await workspace.RefreshAsync();
        var page = new ProfilesPageViewModel(workspace, workspace.Catalog, builtIn, new ProfileStore(_profiles, workspace.Catalog), _files, new FixedMetrics(), dialogs, hasBattery: false);
        await page.BuildPlanAsync();
        return page;
    }

    private static ScriptedDialogs Dialogs(string? name = null) => new(d => d switch
    {
        TextInputDialogViewModel => name,
        _ => true,
    });

    private TweakWorkspace Workspace(ScriptedDialogs dialogs)
    {
        TweakPilot.Engine.TweakEngine Engine(string name) =>
            new(new CompositeCatalog(_catalog), _h.Executors, new TweakPilot.Engine.State.StateStore(Path.Combine(_h.StateRoot, name), new FakeTime()), _h.Environment, new TweakPilot.Engine.NoRestorePointService(), new FakeTime());
        var service = new TweakPilot.Ipc.TweakService(new CompositeCatalog(_catalog), Engine("u"), Engine("m"), Engine("p"), () => throw new InvalidOperationException(), new FakeTime());
        return new TweakWorkspace(_catalog, service, _localizer, new AppSettings(), dialogs, new SyncInvoker(), new CountingRestarter(), readOnly: false);
    }

    private sealed class FixedMetrics : IProfileMetrics
    {
        public ProfileMetrics Capture() => new(9, 1024, 2048);
    }

    private sealed class FixedUpdates(UpdateStatus status) : IUpdateStatusSource
    {
        public UpdateStatus Read() => status;
    }

    private sealed class FakeFiles : IProfileFiles
    {
        public byte[] Content { get; set; } = [];

        public byte[]? Written { get; private set; }

        public string? SuggestedName { get; private set; }

        public string? PickOpen() => "profil.tpprofile";

        public string? PickSave(string suggestedName)
        {
            SuggestedName = suggestedName;
            return "export.tpprofile";
        }

        public byte[] Read(string path) => Content;

        public void Write(string path, byte[] data)
        {
            Written = data;
            Files[path] = data;
        }

        public Dictionary<string, byte[]> Files { get; } = [];

        public string? PickFolder() => @"C:\Export";
    }
}
