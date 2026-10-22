using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Tests;
using AscendedTweaks.Localization;
using FluentAssertions;
using Wpf.Ui.Controls;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class SimpleModeTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private readonly Harness _h = new();
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly TweakCatalog _catalog;

    public SimpleModeTests()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        var tweaks = new List<TweakDefinition>
        {
            Tweak("PERF-901", "A", ImpactLevel.Small, RiskLevel.Safe),
            Tweak("PERF-902", "B", ImpactLevel.Noticeable, RiskLevel.Caution, requiresReboot: true),
            Tweak("PERF-903", "C", ImpactLevel.Noticeable, RiskLevel.Safe),
            Tweak("PERF-904", "D", ImpactLevel.Unclear, RiskLevel.Safe, EvidenceLevel.Disputed, hidden: true),
            Tweak("GAME-901", "E", ImpactLevel.Small, RiskLevel.Safe),
        };
        for (var i = 0; i < 8; i++)
        {
            tweaks.Add(Tweak($"PRIV-9{i:00}", "P" + i, ImpactLevel.Small, RiskLevel.Safe));
        }

        foreach (var t in tweaks)
        {
            var name = ((RegistryValueEqualsCondition)t.Detect).Name;
            _h.Registry.Seed(RegHive.HKCU, P, name, RegistryData.DWord(1));
        }

        var de = new Dictionary<string, string>();
        var en = new Dictionary<string, string>();
        foreach (var t in tweaks)
        {
            foreach (var key in new[] { t.NameKey, t.DescriptionKey, t.SideEffectsKey, t.ImpactNoteKey!, t.PlainDescKey! })
            {
                de[key] = "Text " + key;
                en[key] = "Text " + key;
            }

            de[t.PlainTitleKey!] = "Titel " + t.Id;
            en[t.PlainTitleKey!] = "Title " + t.Id;
        }

        de["tweak.PERF-901.plain.title"] = "Schneller Start";
        en["tweak.PERF-901.plain.title"] = "Faster startup";
        _catalog = new TweakCatalog(tweaks, new Dictionary<string, TextBundle>
        {
            [Localizer.German] = new(Localizer.German, de),
            [Localizer.English] = new(Localizer.English, en),
        });
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Simple_page_hides_unclear_sorts_by_impact_and_searches_both_languages()
    {
        var workspace = Workspace(new ScriptedDialogs(_ => true));
        await workspace.RefreshAsync();
        var page = new SimpleTweakPageViewModel("nav.simple.faster", workspace, c => c.Tweak.Module == "PERF", "simple.faster.help");

        page.Cards.Select(c => c.Tweak.Id).Should().Equal("PERF-903", "PERF-902", "PERF-901");
        page.Cards[1].ImpactText.Should().Be("Spürbar");
        page.Cards[1].RiskPlainText.Should().Be("Mit Vorsicht");
        page.Cards[1].RiskPlainSymbol.Should().Be(SymbolRegular.Warning16);

        page.Query = "faster";
        page.Cards.Select(c => c.Tweak.Id).Should().Equal(["PERF-901"], "die Suche findet auch englische Titel");
        page.Query = "schneller";
        page.Cards.Single().PlainTitle.Should().Be("Schneller Start");
        page.Query = "gibt es nicht";
        page.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task At_most_seven_cards_until_more_is_clicked()
    {
        var workspace = Workspace(new ScriptedDialogs(_ => true));
        await workspace.RefreshAsync();
        var page = new SimpleTweakPageViewModel("nav.simple.privacy", workspace, c => c.Tweak.Module == "PRIV", "simple.privacy.help");

        page.Cards.Should().HaveCount(SimpleTweakPageViewModel.VisibleLimit);
        page.HasMore.Should().BeTrue();
        page.MoreText.Should().Be("Mehr anzeigen (1)");

        page.MoreCommand.Execute(null);
        page.Cards.Should().HaveCount(8);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Change_bar_speaks_plain_language_with_restart_hint()
    {
        var workspace = Workspace(new ScriptedDialogs(_ => true));
        await workspace.RefreshAsync();

        workspace.Card("PERF-901").IsOn = true;
        workspace.PendingText.Should().Be("1 Dinge werden geändert. Neustart nötig: nein.");
        workspace.Card("PERF-902").IsOn = true;
        workspace.PendingText.Should().Be("2 Dinge werden geändert. Neustart nötig: ja.");
    }

    [Fact]
    public void Shell_switches_between_simple_and_expert_navigation()
    {
        var page = new PlaceholderPageViewModel("nav.dashboard", false);
        NavItem Item(string key) => new(key, SymbolRegular.Home24, () => new PlaceholderPageViewModel(key, false));
        var expert = new[] { Item("nav.dashboard"), Item("nav.performance") };
        var simple = new[] { Item("nav.simple.start"), Item("nav.simple.faster") };
        var footer = new[] { Item("nav.simple.back"), Item("nav.settings") };
        var changes = new List<bool>();
        var shell = new ShellViewModel(_localizer, 26200, expert, simpleItems: simple, footerItems: footer, expertMode: false) { ModeChanged = changes.Add };

        shell.SupportsModes.Should().BeTrue();
        shell.Items.Select(i => i.Key).Should().Equal("nav.simple.start", "nav.simple.faster");
        shell.FooterItems.Should().HaveCount(2);
        shell.CurrentPage!.TitleKey.Should().Be("nav.simple.start");

        shell.SelectedFooterItem = shell.FooterItems[0];
        shell.SelectedItem.Should().BeNull();
        shell.CurrentPage!.TitleKey.Should().Be("nav.simple.back");

        shell.UseExpertCommand.Execute(null);
        shell.IsExpertMode.Should().BeTrue();
        shell.Items.Select(i => i.Key).Should().Equal("nav.dashboard", "nav.performance");
        shell.FooterItems.Should().BeEmpty();
        changes.Should().Equal(true);

        shell.UseExpertCommand.Execute(null);
        changes.Should().Equal([true], "kein doppelter Wechsel");
        shell.UseSimpleCommand.Execute(null);
        changes.Should().Equal(true, false);

        var expertOnly = new ShellViewModel(_localizer, 26200, expert);
        expertOnly.SupportsModes.Should().BeFalse();
        expertOnly.IsExpertMode.Should().BeTrue();
        page.Should().NotBeNull();
    }

    [Fact]
    public async Task Back_page_lists_own_changes_and_undoes_one()
    {
        var dialogs = new ScriptedDialogs(_ => true);
        var workspace = Workspace(dialogs);
        await workspace.RefreshAsync();
        await workspace.ApplyIdsAsync(["PERF-901", "GAME-901"]);
        var back = new SimpleBackViewModel(workspace, dialogs);

        await back.LoadAsync();
        back.Items.Select(i => i.Title).Should().BeEquivalentTo(["Schneller Start", "Titel GAME-901"]);

        await back.UndoCommand.ExecuteAsync(back.Items.Single(i => i.TweakId == "PERF-901"));
        back.Items.Should().ContainSingle(i => i.TweakId == "GAME-901");
        _h.Registry.GetValue(RegHive.HKCU, P, "A")!.Number.Should().Be(1);

        await back.UndoAllCommand.ExecuteAsync(null);
        back.HasItems.Should().BeFalse();
    }

    [Fact]
    public void First_steps_has_three_steps_and_first_start_follows_windows()
    {
        FirstStepsViewModel.Steps.Should().Be(3);
        var settings = AppSettings.FirstStart();
        settings.Theme.Should().Be(ThemeChoice.System);
        settings.ExpertMode.Should().BeFalse();
        settings.Language.Should().Be(Localizer.English, "V3: English on first start, regardless of Windows");
        new SettingsStore(Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"), "settings.json")).Load().ExpertMode.Should().BeFalse("erster Start ist Einfach");
    }

    private static TweakDefinition Tweak(string id, string value, ImpactLevel impact, RiskLevel risk, EvidenceLevel evidence = EvidenceLevel.Proven, bool hidden = false, bool requiresReboot = false) => new()
    {
        Id = id,
        Category = id.StartsWith("PERF", StringComparison.Ordinal) ? "Performance.Test" : id.StartsWith("GAME", StringComparison.Ordinal) ? "Gaming.Test" : "Privacy.Test",
        NameKey = $"tweak.{id}.name",
        DescriptionKey = $"tweak.{id}.desc",
        SideEffectsKey = $"tweak.{id}.side",
        Risk = risk,
        Evidence = evidence,
        Builds = [new BuildRange { Min = 19045 }],
        Editions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
        Scope = TweakScope.User,
        RequiresAdmin = false,
        RequiresReboot = requiresReboot,
        HiddenByDefault = hidden,
        Impact = impact,
        ImpactNoteKey = $"tweak.{id}.impact",
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
}
