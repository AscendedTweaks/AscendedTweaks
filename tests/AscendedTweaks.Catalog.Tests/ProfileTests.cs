using System.Text;
using AscendedTweaks.Core;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Catalog.Tests;

public sealed class ProfileTests
{
    private static readonly TweakCatalog Catalog = CatalogLoader.LoadEmbedded();
    private static readonly IReadOnlyList<TweakProfile> BuiltIn = ProfileLoader.LoadBuiltIn(new CompositeCatalog(Catalog));

    private static TweakProfile Get(string key) => BuiltIn.Single(p => p.Key == key);

    private static IEnumerable<string> Ids(string key) => Get(key).Tweaks.Select(t => t.Id);

    [Fact]
    public void Seven_built_in_presets_load_in_order_with_symbols()
    {
        BuiltIn.Select(p => p.Key).Should().Equal("safe", "gaming", "competitive", "laptop", "privacy", "cleanup", "office");
        BuiltIn.Should().OnlyContain(p => !string.IsNullOrEmpty(p.Symbol));
    }
    [Fact]
    public void Safe_preset_holds_all_safe_tweaks_without_services_privacy_updates_or_dns_choice()
    {
        var expected = Catalog.Tweaks
            .Where(t => t.Risk == RiskLevel.Safe)
            .Where(t => !t.Apply.OfType<ServiceSetStartTypeAction>().Any())
            .Where(t => t.Module is not ("PRIV" or "UPD") && t.Category != "Network.Dns")
            .Select(t => t.Id);

        Ids("safe").Should().BeEquivalentTo(expected, "Kandidaten nach Abschnitt 4.1; die Stufen-Regel waehlt daraus");
    }

    [Fact]
    public void Cleanup_preset_changes_no_settings_and_imports_may_never_delete_files()
    {
        Get("cleanup").Tweaks.Should().BeEmpty();
        Get("cleanup").Cleanup.Should().HaveCount(20);
        var imported = """{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001"}],"cleanup":["CLEAN-001"]}""";
        ((Action)(() => ProfileCodec.Parse(Encoding.UTF8.GetBytes(imported), Catalog))).Should().Throw<AscendedTweaksException>().WithMessage("*built-in presets*");
    }

    [Fact]
    public void Competitive_contains_gaming_and_no_preset_touches_security_or_expert_tweaks()
    {
        Ids("competitive").Should().Contain(Ids("gaming"));
        foreach (var preset in BuiltIn)
        {
            foreach (var entry in preset.Tweaks)
            {
                Catalog.TryGet(entry.Id, out var tweak).Should().BeTrue();
                tweak.Category.Should().NotBe("Gaming.Security", preset.Key);
                foreach (var level in Enum.GetValues<PresetLevel>())
                {
                    if (PresetLevels.Includes(level, tweak, entry, hasBattery: false) && entry.Target == ProfileTarget.Applied)
                    {
                        tweak.Risk.Should().NotBe(RiskLevel.Expert);
                        tweak.Impact.Should().NotBe(ImpactLevel.Unclear);
                    }
                }
            }
        }
    }
    [Theory]
    [InlineData("safe", 3, 8, 8)]
    [InlineData("gaming", 2, 6, 9)]
    [InlineData("competitive", 3, 7, 15)]
    [InlineData("privacy", 0, 12, 17)]
    [InlineData("office", 2, 6, 8)]
    public void Levels_pick_a_growing_number_of_entries_from_the_real_catalog(string key, int gentle, int standard, int strong)
    {
        int Count(PresetLevel level) => Get(key).Tweaks.Count(e => Catalog.TryGet(e.Id, out var t) && PresetLevels.Includes(level, t, e, hasBattery: false));

        new[] { Count(PresetLevel.Gentle), Count(PresetLevel.Standard), Count(PresetLevel.Strong) }.Should().Equal(gentle, standard, strong);
    }

    [Fact]
    public void Gaming_profile_has_no_expert_tweaks_no_services_and_power_plan_only_on_desktops()
    {
        var gaming = Get("gaming");
        foreach (var id in gaming.Tweaks.Select(t => t.Id))
        {
            Catalog.TryGet(id, out var tweak).Should().BeTrue();
            tweak.Risk.Should().NotBe(RiskLevel.Expert);
            tweak.Apply.OfType<ServiceSetStartTypeAction>().Should().BeEmpty();
        }

        gaming.Tweaks.Single(t => t.Id == "PERF-001").Condition.Should().Be(ProfileCondition.DesktopOnly);
    }

    [Fact]
    public void Laptop_profile_keeps_the_power_plan_and_network_power_saving()
    {
        var laptop = Get("laptop");
        laptop.Tweaks.Single(t => t.Id == "PERF-001").Target.Should().Be(ProfileTarget.NotApplied);
        laptop.Tweaks.Single(t => t.Id == "NET-011").Target.Should().Be(ProfileTarget.NotApplied);
    }

    [Fact]
    public void Office_and_privacy_presets_follow_section_4_1()
    {
        Ids("office").Should().Contain(["UI-007", "UPD-005", "UPD-006"]).And.NotContain(i => i.StartsWith("GAME", StringComparison.Ordinal));
        Ids("privacy").Should().Contain(["PERF-021", "PERF-022", "PERF-023", "PERF-024", "PERF-025"]);
        Catalog.Tweaks.Where(t => t.Id.StartsWith("PRIV-", StringComparison.Ordinal) && t.Risk != RiskLevel.Expert).Select(t => t.Id)
            .Should().BeSubsetOf(Ids("privacy"));
    }
    [Fact]
    public void Profile_round_trips_through_export_and_import()
    {
        var profile = new TweakProfile { Name = "Mein Profil", Description = "Test", Tweaks = [new ProfileEntry { Id = "PRIV-001" }, new ProfileEntry { Id = "NET-004/1.1.1.1", Target = ProfileTarget.NotApplied }] };

        var parsed = ProfileCodec.Parse(ProfileCodec.Serialize(profile), Catalog);

        parsed.Name.Should().Be("Mein Profil");
        parsed.Tweaks.Should().BeEquivalentTo(profile.Tweaks);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001"}],"command":"cmd.exe"}""", "unbekanntes Feld")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001","path":"C:\\x"}]}""", "unbekanntes Feld im Eintrag")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-999"}]}""", "unbekannte ID")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"GAME-007/C:\\Spiele\\x.exe"}]}""", "erzeugte ID mit Pfad")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PERF-026/HkcuRun/Programm"}]}""", "Autostart-ID")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"UPD-001/2026-10-18/7"}]}""", "Pause hat ein Datum")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001"},{"id":"PRIV-001"}]}""", "doppelt")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[]}""", "leer")]
    [InlineData("""{"schemaVersion":2,"name":"X","tweaks":[{"id":"PRIV-001"}]}""", "Version")]
    [InlineData("""{"schemaVersion":1,"name":"","tweaks":[{"id":"PRIV-001"}]}""", "Name leer")]
    [InlineData("""{"schemaVersion":1,"name":"A\u0007B","tweaks":[{"id":"PRIV-001"}]}""", "Steuerzeichen")]
    [InlineData("""{"schemaVersion":1,"key":"safe","name":"X","tweaks":[{"id":"PRIV-001"}]}""", "Schluessel nur fuer eingebaute Profile")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001","target":"Removed"}]}""", "Zielzustand")]
    [InlineData("""[1,2,3]""", "kein Objekt")]
    [InlineData("""{"schemaVersion":1,"name":"X","tweaks":[{"id":"PRIV-001"}]""", "abgeschnitten")]
    public void Import_rejects_untrusted_content(string json, string because)
    {
        var act = () => ProfileCodec.Parse(Encoding.UTF8.GetBytes(json), Catalog);

        act.Should().Throw<AscendedTweaksException>(because);
    }

    [Fact]
    public void Import_rejects_files_over_one_megabyte_and_too_many_entries()
    {
        var huge = new byte[ProfileCodec.MaxBytes + 1];
        ((Action)(() => ProfileCodec.Parse(huge, Catalog))).Should().Throw<AscendedTweaksException>().WithMessage("*1 MB*");

        var many = new TweakProfile { Name = "Viele", Tweaks = Enumerable.Range(0, ProfileCodec.MaxEntries + 1).Select(i => new ProfileEntry { Id = $"NET-004/1.1.1.{i % 250 + 1}" }).ToList() };
        ((Action)(() => ProfileCodec.Validate(many, Catalog))).Should().Throw<AscendedTweaksException>();
    }

    [Theory]
    [InlineData("Gaming", "Gaming.tpprofile")]
    [InlineData("a/b:c", "a_b_c.tpprofile")]
    [InlineData("...", "Profil.tpprofile")]
    public void Export_file_names_are_safe(string name, string expected)
    {
        ProfileCodec.FileNameFor(name).Should().Be(expected);
    }
}
