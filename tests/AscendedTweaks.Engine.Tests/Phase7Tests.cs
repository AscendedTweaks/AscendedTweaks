using AscendedTweaks.Core;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class Phase7Tests : IDisposable
{
    private const string Dx = GameTweaks.GpuPreferencesPath;
    private const string Exe = @"C:\Games\Spiel\spiel.exe";
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable", "1", "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=1;", "SwapEffectUpgradeEnable", "1", "VRROptimizeEnable=1;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;VRROptimizeEnable=1;", "SwapEffectUpgradeEnable", "1", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;", "SwapEffectUpgradeEnable", null, "VRROptimizeEnable=1;")]
    [InlineData("GpuPreference=2;", "GpuPreference", null, "")]
    public void Key_value_lists_merge_instead_of_overwrite(string? text, string key, string? value, string expected)
    {
        RegistryList.Set(text, ListEntryFormat.KeyValueSemicolon, key, value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, true, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("~ RUNASADMIN", true, "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE")]
    [InlineData("$ ~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", false, "$ ~ RUNASADMIN")]
    [InlineData("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", false, "")]
    public void Compat_layer_flags_keep_other_flags(string? text, bool set, string expected)
    {
        RegistryList.Set(text, ListEntryFormat.CompatLayers, GameTweaks.DisableFullscreenOptimizationsFlag, set ? string.Empty : null).Should().Be(expected);
        RegistryList.Get(expected, ListEntryFormat.CompatLayers, GameTweaks.DisableFullscreenOptimizationsFlag).Should().Be(set ? string.Empty : null);
    }

    [Fact]
    public void Two_tweaks_sharing_one_value_revert_independently()
    {
        var swap = Entry("GAME-909", "SwapEffectUpgradeEnable");
        var vrr = Entry("GAME-910", "VRROptimizeEnable");
        var before = _h.Registry.Snapshot();
        var engine = _h.Engine(swap, vrr);

        engine.ApplyBatch(["GAME-909"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        engine.ApplyBatch(["GAME-910"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        Text().Should().Be("SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;");

        engine.RevertTweak("GAME-909", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted, "nur der eigene Eintrag zaehlt fuer Konflikte");
        Text().Should().Be("VRROptimizeEnable=1;");
        engine.GetStatus("GAME-910").State.Should().Be(TweakState.Applied);

        engine.RevertTweak("GAME-910", _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        Text().Should().BeNull("der Wert fehlte vorher und enthaelt keine Eintraege mehr");

        // Leere Schluessel bleiben, wenn in Anwendungsreihenfolge zurueckgesetzt wird (wie bei normalen Registry-Werten).
        _h.Registry.GetValueNames(RegHive.HKCU, Dx).Should().BeEmpty();

        // In umgekehrter Reihenfolge (wie "Alles rueckgaengig") ist der Zustand danach exakt wie vorher.
        before = _h.Registry.Snapshot();
        engine.ApplyBatch(["GAME-909"], new ApplyOptions());
        engine.ApplyBatch(["GAME-910"], new ApplyOptions());
        engine.RevertAll(_ => ConflictDecision.Cancel).Should().OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Existing_foreign_entries_survive_apply_and_revert()
    {
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String("AutoHDREnable=1;"));
        var before = _h.Registry.Snapshot();
        var engine = _h.Engine(Entry("GAME-909", "SwapEffectUpgradeEnable"));

        engine.ApplyBatch(["GAME-909"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        Text().Should().Be("AutoHDREnable=1;SwapEffectUpgradeEnable=1;");
        engine.RevertTweak("GAME-909", _ => ConflictDecision.Cancel);

        _h.Registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Non_string_value_makes_list_entry_unavailable()
    {
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.DWord(1));
        var engine = _h.Engine(Entry("GAME-909", "SwapEffectUpgradeEnable"));

        engine.ApplyBatch(["GAME-909"], new ApplyOptions()).Outcome.Should().NotBe(BatchOutcome.Committed);
        _h.Registry.GetValue(RegHive.HKCU, Dx, "DirectXUserGlobalSettings").Should().Be(RegistryData.DWord(1));
    }

    [Fact]
    public void Per_game_tweaks_are_generated_and_reverted()
    {
        _h.Registry.Seed(RegHive.HKCU, GameTweaks.LayersPath, Exe, RegistryData.String("~ RUNASADMIN"));
        var before = _h.Registry.Snapshot();
        var fso = GameTweaks.FullscreenId(Exe);
        var gpu = GameTweaks.GpuId(2, Exe);
        var engine = new TweakEngine(new CompositeCatalog(new ListCatalog()), _h.Executors, _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());

        engine.ApplyBatch([fso, gpu], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Registry.GetValue(RegHive.HKCU, GameTweaks.LayersPath, Exe)!.Text.Should().Be("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE");
        _h.Registry.GetValue(RegHive.HKCU, Dx, Exe)!.Text.Should().Be("GpuPreference=2;");
        GameTweaks.TargetOf(gpu).Should().Be("spiel.exe");

        engine.RevertAll(_ => ConflictDecision.Cancel).Should().OnlyContain(r => r.Outcome == RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("GAME-007/C:\\Spiele\\a.exe", true)]
    [InlineData("GAME-007/relativ\\a.exe", false)]
    [InlineData("GAME-007/C:\\Spiele\\..\\a.exe", false)]
    [InlineData("GAME-007/C:\\Spiele\\a.bat", false)]
    [InlineData("GAME-008/2/C:\\Spiele\\a.exe", true)]
    [InlineData("GAME-008/3/C:\\Spiele\\a.exe", false)]
    [InlineData("GAME-016/FortniteClient-Win64-Shipping.exe", true)]
    [InlineData("GAME-016/..\\evil.exe", false)]
    [InlineData("GAME-016/x.exe\\Debugger", false)]
    public void Per_game_ids_are_validated(string id, bool valid)
    {
        GameTweaks.TryCreate(id, out _).Should().Be(valid);
    }

    [Fact]
    public void Permanent_priority_only_writes_perf_options_and_is_expert()
    {
        GameTweaks.TryCreate(GameTweaks.PriorityId("spiel.exe"), out var tweak).Should().BeTrue();

        tweak.Risk.Should().Be(RiskLevel.Expert);
        tweak.Scope.Should().Be(TweakScope.Machine);
        tweak.Apply.Should().ContainSingle().Which.Should().BeOfType<RegistrySetValueAction>()
            .Which.Path.Should().EndWith(@"Image File Execution Options\spiel.exe\PerfOptions");
    }

    [Fact]
    public void Accessibility_hotkey_and_mouse_parameters_restore_original_values()
    {
        _h.Parameters.Values["MouseAcceleration"] = 6 | 10 << 8 | 1 << 16;
        var engine = _h.Engine(
            TestTweaks.Create("GAME-911", new SystemParameterEqualsCondition { Parameter = "MouseAcceleration", Value = 0 }, [new SystemParametersInfoAction { Parameter = "MouseAcceleration", Value = 0 }]),
            TestTweaks.Create("GAME-912", new SystemParameterEqualsCondition { Parameter = "StickyKeysHotkey", Value = 0 }, [new SystemParametersInfoAction { Parameter = "StickyKeysHotkey", Value = 0 }]));

        engine.ApplyBatch(["GAME-911", "GAME-912"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        _h.Parameters.Values["MouseAcceleration"].Should().Be(0);
        engine.RevertAll(_ => ConflictDecision.Cancel);

        _h.Parameters.Values["MouseAcceleration"].Should().Be(6u | 10u << 8 | 1u << 16);
        _h.Parameters.Values["StickyKeysHotkey"].Should().Be(1);
    }

    [Fact]
    public void List_entry_revert_keeps_a_previously_empty_value_and_removes_a_value_it_created()
    {
        // Vorher leer: nach dem Ruecksetzen wieder genau die leere Zeichenkette.
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String(string.Empty));
        var engine = _h.Engine(Entry("GAME-909", "SwapEffectUpgradeEnable"));
        engine.ApplyBatch(["GAME-909"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        Text().Should().Be("SwapEffectUpgradeEnable=1;");
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String("SwapEffectUpgradeEnable=0;"));
        engine.RevertTweak("GAME-909", _ => ConflictDecision.RestoreOriginal).Outcome.Should().Be(RevertOutcome.Reverted);
        Text().Should().Be(string.Empty);
    }

    [Fact]
    public void List_entry_revert_after_a_foreign_change_deletes_the_value_it_created()
    {
        var engine = _h.Engine(Entry("GAME-908", "SwapEffectUpgradeEnable"));
        engine.ApplyBatch(["GAME-908"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);

        // Ein anderes Programm aendert den Eintrag; nach Bestaetigung wird der Ausgangszustand (Wert fehlt) hergestellt.
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String("SwapEffectUpgradeEnable=0;"));
        engine.RevertTweak("GAME-908", _ => ConflictDecision.RestoreOriginal).Outcome.Should().Be(RevertOutcome.Reverted);

        _h.Registry.GetValue(RegHive.HKCU, Dx, "DirectXUserGlobalSettings").Should().BeNull();
    }

    [Fact]
    public void List_entry_removed_by_hand_is_left_alone_on_revert()
    {
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String("VRROptimizeEnable=0;"));
        var engine = _h.Engine(Entry("GAME-907", "SwapEffectUpgradeEnable"));
        engine.ApplyBatch(["GAME-907"], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);

        // Der Nutzer hat den Eintrag selbst wieder entfernt: Ruecksetzen darf den fremden Eintrag nicht anfassen.
        _h.Registry.Seed(RegHive.HKCU, Dx, "DirectXUserGlobalSettings", RegistryData.String("VRROptimizeEnable=0;"));
        engine.RevertTweak("GAME-907", _ => ConflictDecision.RestoreOriginal).Outcome.Should().BeOneOf(RevertOutcome.Reverted, RevertOutcome.NotApplied);

        Text().Should().Be("VRROptimizeEnable=0;");
    }

    private string? Text() => _h.Registry.GetValue(RegHive.HKCU, Dx, "DirectXUserGlobalSettings")?.Text;

    private static TweakDefinition Entry(string id, string key)
    {
        return TestTweaks.Create(
            id,
            new RegistryListEntryEqualsCondition { Hive = RegHive.HKCU, Path = Dx, Name = "DirectXUserGlobalSettings", Format = ListEntryFormat.KeyValueSemicolon, Key = key, Value = "1" },
            [new RegistryListEntryAction { Hive = RegHive.HKCU, Path = Dx, Name = "DirectXUserGlobalSettings", Format = ListEntryFormat.KeyValueSemicolon, Key = key, Value = "1" }]);
    }
}
