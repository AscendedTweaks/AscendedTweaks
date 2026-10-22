using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.Tests;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

/// <summary>Presets mit Stufen (V2, Abschnitt 4): Stufen-Anzahl, gruppierte Vorschau, Ergebnis-Rueckgaengig und "Preset rueckgaengig".</summary>
public sealed class PresetTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly TweakCatalog _catalog;
    private readonly MemoryOwnership _ownership = new();

    public PresetTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        var tweaks = new List<TweakDefinition>
        {
            Tweak("PERF-901", "A", RiskLevel.Safe, ImpactLevel.Noticeable),
            Tweak("PERF-902", "B", RiskLevel.Safe, ImpactLevel.Small),
            Tweak("PERF-903", "C", RiskLevel.Caution, ImpactLevel.Small),
            Tweak("PERF-904", "D", RiskLevel.Caution, ImpactLevel.Noticeable),
            Tweak("PERF-905", "E", RiskLevel.Expert, ImpactLevel.Noticeable),
            Tweak("GAME-901", "F", RiskLevel.Safe, ImpactLevel.Small),
            Tweak("GAME-902", "G", RiskLevel.Safe, ImpactLevel.Noticeable, "Gaming.Security"),
            Tweak("PERF-906", "H", RiskLevel.Caution, ImpactLevel.Unclear, hidden: true),
        };

        var de = new Dictionary<string, string>();
        foreach (var t in tweaks)
        {
            _h.Registry.Seed(RegHive.HKCU, P, ((RegistryValueEqualsCondition)t.Detect).Name, RegistryData.DWord(1));
            foreach (var key in new[] { t.NameKey, t.DescriptionKey, t.SideEffectsKey, t.ImpactNoteKey!, t.PlainDescKey! })
            {
                de[key] = "Text " + key;
            }

            de[t.PlainTitleKey!] = "Titel " + t.Id;
        }

        _catalog = new TweakCatalog(tweaks, new Dictionary<string, TextBundle>
        {
            [Localizer.German] = new(Localizer.German, de),
            [Localizer.English] = new(Localizer.English, de),
        });
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Levels_count_only_safe_proven_changes_and_never_expert_or_security()
    {
        var start = Start(new ScriptedDialogs(_ => true), Preset("gaming", "PERF-901", "PERF-902", "PERF-903", "PERF-904", "PERF-905", "GAME-902"));

        await start.LoadPresetsAsync();

        var card = start.Presets.Single();
        card.Levels.Select(l => l.Label).Should().Equal("Sanft (1)", "Standard (3)", "Stark (4)");
        card.SelectedLevel!.Level.Should().Be(PresetLevel.Standard);
        card.Plan.Select(r => r.Id).Should().BeEquivalentTo(["PERF-901", "PERF-902", "PERF-904"]);
        card.SelectedLevel = card.Levels.Single(l => l.Level == PresetLevel.Strong);
        card.Plan.Select(r => r.Id).Should().NotContain(["PERF-905", "GAME-902"]);
    }

    [Fact]
    public async Task Undoing_preset_a_keeps_what_preset_b_set_and_preview_names_the_earlier_preset()
    {
        var dialogs = new ScriptedDialogs(_ => true);
        var b = Preset("b", "PERF-902", "GAME-901");
        b = b with { Tweaks = [.. b.Tweaks, new ProfileEntry { Id = "PERF-901", Target = ProfileTarget.NotApplied }] };
        var start = Start(dialogs, Preset("a", "PERF-901", "PERF-902", "PERF-904"), b);
        await start.LoadPresetsAsync();

        await start.Presets[0].ApplyCommand.ExecuteAsync(null);
        await start.Presets[1].ApplyCommand.ExecuteAsync(null);

        var items = dialogs.Last<PreviewDialogViewModel>()!.Groups.SelectMany(g => g.Items).ToList();
        items.Should().Contain("Titel PERF-901 (vorher aus Preset „[preset.a.name]“)").And.Contain("Titel GAME-901");
        start.Presets.Should().OnlyContain(p => p.IsOwned);

        await start.Presets[0].UndoCommand.ExecuteAsync(null);

        Value("D").Should().Be(1, "nur Preset A wollte PERF-904");
        Value("B").Should().Be(0, "Preset B will PERF-902 weiterhin");
        Value("A").Should().Be(1, "Preset B hat PERF-901 zurueckgesetzt");
        Value("F").Should().Be(0);
        start.Presets[0].IsOwned.Should().BeFalse();
        start.Presets[1].IsOwned.Should().BeTrue();
    }

    [Fact]
    public async Task Simple_mode_preset_may_reset_a_hidden_tweak_and_says_when_everything_is_set()
    {
        var dialogs = new ScriptedDialogs(_ => true);
        var workspace = Workspace(dialogs);
        await workspace.RefreshAsync();
        await workspace.ApplyIdsAsync(["PERF-906", "PERF-901"]);
        Value("H").Should().Be(0);
        var laptop = new TweakProfile { Key = "laptop", Name = "laptop", Tweaks = [new ProfileEntry { Id = "PERF-906", Target = ProfileTarget.NotApplied }, new ProfileEntry { Id = "PERF-901" }] };
        var start = new SimpleStartViewModel(workspace, new FakeSystem(), () => [], [laptop], hasBattery: true, dialogs, _ownership);
        await start.LoadPresetsAsync();

        start.Presets[0].Plan.Should().ContainSingle(r => r.Action == ProfileAction.Revert && r.Id == "PERF-906", "Zuruecksetzen braucht keinen Expertenmodus");
        await start.Presets[0].ApplyCommand.ExecuteAsync(null);

        Value("H").Should().Be(1);
        start.Presets[0].Summary.Should().Be("Alles schon eingestellt, nichts zu tun.");
    }

    [Fact]
    public async Task Result_dialog_undo_takes_back_exactly_this_run()
    {
        var dialogs = new ScriptedDialogs(d =>
        {
            if (d is ResultDialogViewModel { HasUndo: true } result)
            {
                result.UndoCommand.Execute(null);
            }

            return true;
        });
        var start = Start(dialogs, Preset("a", "PERF-901", "PERF-902"));
        await start.LoadPresetsAsync();

        await start.Presets[0].ApplyCommand.ExecuteAsync(null);

        Value("A").Should().Be(1);
        Value("B").Should().Be(1);
        start.Presets[0].IsOwned.Should().BeFalse("ein zurueckgenommener Lauf gehoert keinem Preset");
        _ownership.Data.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Declined_preview_changes_nothing()
    {
        var start = Start(new ScriptedDialogs(d => d is not PreviewDialogViewModel), Preset("a", "PERF-901"));
        await start.LoadPresetsAsync();

        await start.Presets[0].ApplyCommand.ExecuteAsync(null);

        Value("A").Should().Be(1);
        start.Presets[0].IsOwned.Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_preset_scans_first_and_uses_the_recycle_bin_where_possible()
    {
        var cleanup = new FakeCleanup();
        var dialogs = new ScriptedDialogs(_ => true);
        var preset = new TweakProfile { Key = "cleanup", Name = "cleanup", Tweaks = [], Cleanup = ["CLEAN-001", "CLEAN-002", "CLEAN-006", "CLEAN-005"] };
        var start = Start(dialogs, cleanup, preset);
        await start.LoadPresetsAsync();
        var card = start.Presets.Single();
        card.IsCleanup.Should().BeTrue();
        card.Levels.Select(l => l.Label).Should().Equal("Sanft (1)", "Standard (2)", "Stark (4)");

        await card.ApplyCommand.ExecuteAsync(null);

        dialogs.Last<PreviewDialogViewModel>()!.Groups.Single().Items.Should().HaveCount(2);
        cleanup.Runs.Should().Equal(("CLEAN-001", CleanupMode.RecycleBin), ("CLEAN-002", CleanupMode.Permanent));
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(1, "Aufraeumen aendert keine Einstellung");
    }

    [Fact]
    public async Task Own_preset_is_saved_from_the_dialog_and_has_no_levels()
    {
        var store = new MemoryProfiles();
        var dialogs = new ScriptedDialogs(d =>
        {
            if (d is CustomPresetDialogViewModel custom)
            {
                custom.SaveCommand.CanExecute(null).Should().BeFalse();
                custom.Name = "Mein Preset";
                custom.Items.Single(i => i.Id == "PERF-902").IsChecked = true;
                custom.SaveCommand.Execute(null);
                return custom.Completion.Result;
            }

            return true;
        });
        var start = new SimpleStartViewModel(Workspace(dialogs), new FakeSystem(), () => [], [], hasBattery: false, dialogs, _ownership, store);
        await start.LoadAsync();

        await start.CreateOwnCommand.ExecuteAsync(null);

        var own = start.Presets.Single();
        own.Name.Should().Be("Mein Preset");
        own.HasLevels.Should().BeFalse();
        own.Levels.Should().ContainSingle();
        own.Plan.Select(r => r.Id).Should().Equal("PERF-902");
        own.OwnerKey.Should().Be("own:Mein Preset");
    }

    private int Value(string name) => (int)_h.Registry.GetValue(RegHive.HKCU, P, name)!.Number!;

    private static TweakProfile Preset(string key, params string[] ids) => new() { Key = key, Name = key, Tweaks = ids.Select(id => new ProfileEntry { Id = id }).ToList() };

    private SimpleStartViewModel Start(ScriptedDialogs dialogs, params TweakProfile[] presets) => Start(dialogs, null, presets);

    private SimpleStartViewModel Start(ScriptedDialogs dialogs, ICleanupService? cleanup, params TweakProfile[] presets)
    {
        var workspace = Workspace(dialogs);
        workspace.RefreshAsync().GetAwaiter().GetResult();
        return new SimpleStartViewModel(workspace, new FakeSystem(), () => [], presets, hasBattery: false, dialogs, _ownership, cleanup: cleanup);
    }

    private static TweakDefinition Tweak(string id, string value, RiskLevel risk, ImpactLevel impact, string? category = null, bool hidden = false) => new()
    {
        Id = id,
        Category = category ?? (id.StartsWith("PERF", StringComparison.Ordinal) ? "Performance.Test" : "Gaming.Test"),
        NameKey = $"tweak.{id}.name",
        DescriptionKey = $"tweak.{id}.desc",
        SideEffectsKey = $"tweak.{id}.side",
        Risk = risk,
        Evidence = hidden ? EvidenceLevel.Disputed : EvidenceLevel.Proven,
        HiddenByDefault = hidden,
        Builds = [new BuildRange { Min = 19045 }],
        Editions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
        Scope = TweakScope.User,
        RequiresAdmin = false,
        RequiresReboot = false,
        Impact = impact,
        ImpactNoteKey = $"tweak.{id}.impact",
        MeasuredOn = impact == ImpactLevel.Noticeable ? ["Testsystem"] : [],
        PlainTitleKey = $"tweak.{id}.plain.title",
        PlainDescKey = $"tweak.{id}.plain.desc",
        Detect = new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = value, Value = new RegLiteral { Number = 0 } },
        Apply = [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = value, Kind = RegValueKind.DWord, Value = new RegLiteral { Number = 0 } }],
        Revert = RevertSpec.RestoreFromBackup,
        Sources = ["Testquelle fuer Unit-Tests"],
    };

    private TweakWorkspace Workspace(ScriptedDialogs dialogs)
    {
        AscendedTweaks.Engine.TweakEngine Engine(string name) =>
            new(new CompositeCatalog(_catalog), _h.Executors, new AscendedTweaks.Engine.State.StateStore(Path.Combine(_h.StateRoot, name), new FakeTime()), _h.Environment, new AscendedTweaks.Engine.NoRestorePointService(), new FakeTime());
        var service = new AscendedTweaks.Ipc.TweakService(new CompositeCatalog(_catalog), Engine("u"), Engine("m"), Engine("p"), () => throw new InvalidOperationException(), new FakeTime());
        return new TweakWorkspace(_catalog, service, _localizer, new AppSettings(), dialogs, new SyncInvoker(), new CountingRestarter(), readOnly: false);
    }

    private sealed class MemoryOwnership : IPresetOwnershipStore
    {
        public PresetOwnership Data { get; private set; } = new();

        public PresetOwnership Load() => new(Data.Snapshot());

        public void Save(PresetOwnership ownership) => Data = new PresetOwnership(ownership.Snapshot());
    }

    private sealed class MemoryProfiles : IProfileStore
    {
        private readonly List<StoredProfile> _items = [];

        public IReadOnlyList<StoredProfile> List() => _items;

        public StoredProfile Save(TweakProfile profile, string? fileId = null)
        {
            var stored = new StoredProfile(fileId ?? Guid.NewGuid().ToString("N"), profile);
            _items.Add(stored);
            return stored;
        }

        public void Delete(string fileId) => _items.RemoveAll(s => s.FileId == fileId);
    }

    private sealed class FakeCleanup : ICleanupService
    {
        public List<(string Id, CleanupMode Mode)> Runs { get; } = [];

        public CleanupScan ScanCleanup(string categoryId) => categoryId == "CLEAN-005"
            ? new CleanupScan { CategoryId = categoryId }
            : new CleanupScan { CategoryId = categoryId, Files = 2, Bytes = 2048 };

        public CleanupResult RunCleanup(string categoryId, CleanupMode mode)
        {
            Runs.Add((categoryId, mode));
            return new CleanupResult { CategoryId = categoryId, Mode = mode, DeletedFiles = 2, DeletedBytes = 2048 };
        }
    }

    private sealed class FakeSystem : ISystemInfoSource
    {
        public SystemInfo Read() => new(null, [], null, [], new WindowsInfo("Windows 11 Home", "25H2", "26200.1", DateTimeOffset.UnixEpoch), new BootInfo(DateTimeOffset.UnixEpoch, null, true));

        public HealthFacts ReadHealth() => new(null, null, null, null, [], null, null);
    }
}
