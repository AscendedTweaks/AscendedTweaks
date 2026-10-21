using System.IO;
using FluentAssertions;
using TweakPilot.App.Controls;
using TweakPilot.App.Services;
using TweakPilot.App.ViewModels;
using TweakPilot.Localization;
using TweakPilot.Tools.ContrastCheck;
using Wpf.Ui.Controls;
using Xunit;

namespace TweakPilot.App.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TweakPilot.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Pages_are_created_lazily_and_cached()
    {
        var created = 0;
        var items = new[]
        {
            new NavItem("nav.dashboard", SymbolRegular.Home24, () => { created++; return new PlaceholderPageViewModel("nav.dashboard", true); }),
            new NavItem("nav.settings", SymbolRegular.Settings24, () => { created++; return new PlaceholderPageViewModel("nav.settings", false); }),
        };
        var shell = new ShellViewModel(new Localizer(), 26100, items);

        created.Should().Be(1);
        var first = shell.CurrentPage;
        shell.SelectedItem = items[1];
        shell.SelectedItem = items[0];

        created.Should().Be(2);
        shell.CurrentPage.Should().BeSameAs(first);
        shell.CurrentPage!.ShowHonestyBanner.Should().BeTrue();
    }

    [Fact]
    public void Navigation_collapses_below_1000_and_can_be_toggled()
    {
        var shell = new ShellViewModel(new Localizer(), 26100, []);

        shell.UpdateWindowWidth(1280);
        shell.NavWidth.Should().Be(240);
        shell.UpdateWindowWidth(999);
        shell.IsNavCollapsed.Should().BeTrue();
        shell.NavWidth.Should().Be(56);
        shell.UpdateWindowWidth(1200);
        shell.IsNavCollapsed.Should().BeFalse();

        shell.ToggleNavCommand.Execute(null);
        shell.IsNavCollapsed.Should().BeTrue();
        shell.UpdateWindowWidth(1400);
        shell.IsNavCollapsed.Should().BeTrue("manuell eingeklappt bleibt eingeklappt");
        shell.ToggleNavCommand.Execute(null);
        shell.IsNavCollapsed.Should().BeFalse();
    }

    [Theory]
    [InlineData(19044, SupportBannerKind.Danger, BadgeTone.Danger, true)]
    [InlineData(19045, SupportBannerKind.Info, BadgeTone.Accent, false)]
    [InlineData(22621, SupportBannerKind.Danger, BadgeTone.Danger, true)]
    [InlineData(26100, SupportBannerKind.None, BadgeTone.Accent, false)]
    [InlineData(26300, SupportBannerKind.Warning, BadgeTone.Warning, false)]
    public void Version_check_sets_banner_and_read_only_mode(int build, SupportBannerKind kind, BadgeTone tone, bool readOnly)
    {
        var shell = new ShellViewModel(new Localizer(), build, []);

        shell.BannerKind.Should().Be(kind);
        shell.BannerTone.Should().Be(tone);
        shell.IsReadOnly.Should().Be(readOnly);
        if (kind != SupportBannerKind.None)
        {
            shell.BannerText.Should().NotBeEmpty();
            shell.BannerBadgeText.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void Banner_text_follows_language()
    {
        var localizer = new Localizer();
        var shell = new ShellViewModel(localizer, 26300, []);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.BannerText.Should().StartWith("Build 26300 wurde");
        localizer.SetCulture(Localizer.English);

        changed.Should().Contain(nameof(ShellViewModel.BannerText));
        shell.BannerText.Should().StartWith("Build 26300 has");
    }

    [Fact]
    public void Settings_are_saved_and_applied()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = store.Load();
        var theme = new FakeTheme();
        var localizer = new Localizer();
        var vm = new SettingsViewModel(localizer, theme, store, settings);

        theme.Applied.Should().BeEmpty("das Laden darf nichts aendern");
        File.Exists(store.Path).Should().BeFalse();

        vm.SelectedTheme = vm.Themes.Single(t => t.Value == ThemeChoice.Light);
        vm.SelectedLanguage = vm.Languages.Single(l => l.Value == Localizer.English);

        theme.Applied.Should().Equal(ThemeChoice.Light);
        localizer.Culture.Should().Be(Localizer.English);
        var reloaded = store.Load();
        reloaded.Theme.Should().Be(ThemeChoice.Light);
        reloaded.Language.Should().Be(Localizer.English);
    }

    [Fact]
    public void Broken_settings_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ kaputt");

        var settings = new SettingsStore(path).Load();

        settings.Theme.Should().Be(ThemeChoice.Dark);
        settings.Language.Should().Be(Localizer.German);
        settings.FirstStepsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Design_catalog_texts_are_localized()
    {
        var vm = new DesignCatalogViewModel(new Localizer());

        vm.UnavailableTooltip.Should().Be("Auf deinem System nicht verfügbar: Dienst Fax ist nicht installiert.");
        vm.PendingText.Should().Be("3 Änderungen ausstehend");
        vm.ListItems.Should().HaveCount(4);
        vm.ToggleRunningCommand.Execute(null);
        vm.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Contrast_formula_and_token_files_pass()
    {
        Program.Ratio(Program.Parse("000000"), Program.Parse("FFFFFF")).Should().BeApproximately(21, 0.01);
        Program.Ratio(Program.Parse("777777"), Program.Parse("FFFFFF")).Should().BeApproximately(4.48, 0.01);

        foreach (var theme in new[] { "Dark", "Light" })
        {
            var tokens = Program.ReadTokens(Path.Combine(FindThemes(), $"Tokens.{theme}.xaml"));
            tokens.Should().ContainKeys("Background", "Surface", "Elevated", "Border", "Accent", "OnAccent", "Success", "Warning", "Danger", "TextPrimary", "TextSecondary");
            Program.Checks(tokens).Should().OnlyContain(c => Program.Ratio(c.Foreground, c.Background) >= c.Minimum, theme);
        }
    }

    private static string FindThemes()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "TweakPilot.App", "Themes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException();
    }

    private sealed class FakeTheme : IThemeService
    {
        public List<ThemeChoice> Applied { get; } = [];

        public ThemeChoice Current { get; private set; }

        public void Apply(ThemeChoice choice)
        {
            Current = choice;
            Applied.Add(choice);
        }
    }
}
