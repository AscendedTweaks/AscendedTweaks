using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AscendedTweaks.App.Services;
using AscendedTweaks.Catalog;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class LocalizationTests
{
    /// <summary>Verbotene Formulierungen (Abschnitt 8.5): Angst, Superlative ohne Beleg, Prozentversprechen.</summary>
    private static readonly Regex[] Forbidden =
    [
        new("in Gefahr", RegexOptions.IgnoreCase),
        new("Kritische Fehler gefunden", RegexOptions.IgnoreCase),
        new(@"\bat risk\b", RegexOptions.IgnoreCase),
        new(@"\d+\s*%", RegexOptions.None),
        new(@"\b(boost|turbo|maximale Leistung|beste Leistung aller)\b", RegexOptions.IgnoreCase),
    ];

    /// <summary>V3 2.2 (c): short German stop words that never appear in natural English UI text.</summary>
    private static readonly Regex GermanWords = new(
        @"\b(der|die|das|und|nicht|oder|mit|fuer|für|ist|wird|werden|bitte|auch|noch|Änderung|Aenderung|Datei|Dateien|Einstellungen|Neustart|zurück)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var de = Localizer.Bundle(Localizer.German);
        var en = Localizer.Bundle(Localizer.English);

        de.Keys.Should().BeEquivalentTo(en.Keys);
    }

    [Fact]
    public void No_text_is_empty_and_placeholders_match()
    {
        var de = Localizer.Bundle(Localizer.German);
        var en = Localizer.Bundle(Localizer.English);

        foreach (var (key, text) in de)
        {
            text.Should().NotBeNullOrWhiteSpace(key);
            en[key].Should().NotBeNullOrWhiteSpace(key);
            Localizer.Placeholders(text).Should().BeEquivalentTo(Localizer.Placeholders(en[key]), key);
        }
    }

    [Fact]
    public void Texts_avoid_forbidden_phrases()
    {
        foreach (var culture in Localizer.SupportedCultures)
        {
            foreach (var (key, text) in Localizer.Bundle(culture))
            {
                foreach (var pattern in Forbidden)
                {
                    pattern.IsMatch(text).Should().BeFalse($"{culture}/{key}: \"{text}\" enthaelt \"{pattern}\"");
                }
            }
        }
    }

    [Fact]
    public void Required_texts_from_the_brief_are_exact()
    {
        var de = new Localizer(Localizer.German);
        var en = new Localizer(Localizer.English);

        de["banner.honesty"].Should().Be("Viele Tweaks bringen wenig, manche nichts. Hier steht, was belegt ist.");
        en["banner.honesty"].Should().Be("Many tweaks help little and some do nothing. Here is what is proven.");
        de.Format("changebar.pending", ("n", 3)).Should().Be("3 Änderungen ausstehend");
        en.Format("unverified.build", ("build", 26300)).Should().Be("Build 26300 has not been tested yet. Expert tweaks are locked.");
        de["expert.stage3"].Should().Be("Tippe VERSTANDEN, um fortzufahren.");
    }

    [Fact]
    public void English_is_the_default_even_when_windows_is_german()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            new Localizer().Culture.Should().Be(Localizer.English);
            new AppSettings().Language.Should().Be(Localizer.English);
            AppSettings.FirstStart().Language.Should().Be(Localizer.English);
            var missing = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"), "settings.json");
            new SettingsStore(missing).Load().Language.Should().Be(Localizer.English);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void English_texts_contain_no_german()
    {
        var bundles = new List<(string Source, IReadOnlyDictionary<string, string> Texts)> { ("ui", Localizer.Bundle(Localizer.English)) };
        bundles.Add(("catalog", CatalogLoader.LoadEmbedded().Texts[Localizer.English].Texts));

        foreach (var (source, texts) in bundles)
        {
            foreach (var (key, text) in texts)
            {
                Regex.IsMatch(text, "[äöüÄÖÜß]").Should().BeFalse($"{source}/{key}: \"{text}\" has umlauts");
                GermanWords.IsMatch(text).Should().BeFalse($"{source}/{key}: \"{text}\" looks German");
            }
        }
    }

    [Fact]
    public void Culture_switch_notifies_bindings_and_falls_back()
    {
        var localizer = new Localizer("fr-FR");
        var changed = new List<string?>();
        localizer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localizer.Culture.Should().Be(Localizer.English);
        localizer.SetCulture(Localizer.German);
        localizer.SetCulture(Localizer.German);
        localizer.SetCulture("xx-XX");

        changed.Should().Equal("Culture", "Item[]");
        localizer.Culture.Should().Be(Localizer.German);
        localizer.SetCulture(Localizer.English);
        localizer["nav.disk"].Should().Be("Storage");
        localizer["does.not.exist"].Should().Be("[does.not.exist]");
    }
}
