using System.IO;
using AscendedTweaks.App.Controls;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Localization;
using AscendedTweaks.Tools.ContrastCheck;
using FluentAssertions;
using Wpf.Ui.Controls;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"));

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

        shell.BannerText.Should().StartWith("Build 26300 has");
        localizer.SetCulture(Localizer.German);

        changed.Should().Contain(nameof(ShellViewModel.BannerText));
        shell.BannerText.Should().StartWith("Build 26300 wurde");
    }

    [Fact]
    public void Settings_are_saved_and_applied()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        var settings = store.Load();
        var localizer = new Localizer();
        var vm = new SettingsViewModel(localizer, store, settings);

        File.Exists(store.Path).Should().BeFalse("loading changes nothing");

        vm.SelectedLanguage = vm.Languages.Single(l => l.Value == Localizer.German);

        localizer.Culture.Should().Be(Localizer.German);
        var reloaded = store.Load();
        reloaded.Language.Should().Be(Localizer.German);

        // V3 2.2 (e): the next start uses the saved language.
        var nextStart = new Localizer(reloaded.Language);
        nextStart.Culture.Should().Be(Localizer.German);
        new SettingsViewModel(nextStart, store, reloaded).SelectedLanguage!.Value.Should().Be(Localizer.German);
    }

    [Fact]
    public void Settings_from_versions_with_a_light_theme_still_load()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "language": "de-DE", "theme": "Light", "expertMode": true, "onboardingCompleted": true }""");
        var store = new SettingsStore(path);

        var settings = store.Load();

        settings.Language.Should().Be(Localizer.German, "an old theme value must not reset the other settings");
        settings.ExpertMode.Should().BeTrue();
        settings.FirstStepsCompleted.Should().BeTrue();
        store.Save(settings);
        File.ReadAllText(path).Should().NotContain("theme", "the dark design is the only one; the old value is dropped");
    }

    [Fact]
    public void Broken_settings_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ kaputt");

        var settings = new SettingsStore(path).Load();

        settings.Language.Should().Be(Localizer.English);
        settings.FirstStepsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Design_catalog_texts_are_localized()
    {
        var vm = new DesignCatalogViewModel(new Localizer(Localizer.German));

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

        foreach (var theme in new[] { "Dark" })
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
            var candidate = Path.Combine(dir.FullName, "src", "AscendedTweaks.App", "Themes");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException();
    }

}
