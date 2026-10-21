using FluentAssertions;
using TweakPilot.Core;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class PresetLevelTests
{
    [Theory]
    // Sanft: nur Sicher und Spuerbar.
    [InlineData(PresetLevel.Gentle, RiskLevel.Safe, ImpactLevel.Noticeable, true)]
    [InlineData(PresetLevel.Gentle, RiskLevel.Safe, ImpactLevel.Small, false)]
    [InlineData(PresetLevel.Gentle, RiskLevel.Caution, ImpactLevel.Noticeable, false)]
    [InlineData(PresetLevel.Gentle, RiskLevel.Caution, ImpactLevel.Small, false)]
    // Standard: Sanft plus Sicher/Klein plus Vorsicht/Spuerbar.
    [InlineData(PresetLevel.Standard, RiskLevel.Safe, ImpactLevel.Noticeable, true)]
    [InlineData(PresetLevel.Standard, RiskLevel.Safe, ImpactLevel.Small, true)]
    [InlineData(PresetLevel.Standard, RiskLevel.Caution, ImpactLevel.Noticeable, true)]
    [InlineData(PresetLevel.Standard, RiskLevel.Caution, ImpactLevel.Small, false)]
    // Stark: Standard plus Vorsicht/Klein.
    [InlineData(PresetLevel.Strong, RiskLevel.Safe, ImpactLevel.Small, true)]
    [InlineData(PresetLevel.Strong, RiskLevel.Caution, ImpactLevel.Small, true)]
    // Nie Experte, nie Unklar.
    [InlineData(PresetLevel.Strong, RiskLevel.Expert, ImpactLevel.Noticeable, false)]
    [InlineData(PresetLevel.Strong, RiskLevel.Safe, ImpactLevel.Unclear, false)]
    [InlineData(PresetLevel.Gentle, RiskLevel.Expert, ImpactLevel.Noticeable, false)]
    [InlineData(PresetLevel.Standard, RiskLevel.Caution, ImpactLevel.Unclear, false)]
    public void Level_rule_table(PresetLevel level, RiskLevel risk, ImpactLevel impact, bool included)
    {
        PresetLevels.Includes(level, risk, impact).Should().Be(included);
    }

    [Fact]
    public void Levels_are_nested()
    {
        foreach (var risk in Enum.GetValues<RiskLevel>())
        {
            foreach (var impact in Enum.GetValues<ImpactLevel>())
            {
                if (PresetLevels.Includes(PresetLevel.Gentle, risk, impact))
                {
                    PresetLevels.Includes(PresetLevel.Standard, risk, impact).Should().BeTrue();
                }

                if (PresetLevels.Includes(PresetLevel.Standard, risk, impact))
                {
                    PresetLevels.Includes(PresetLevel.Strong, risk, impact).Should().BeTrue();
                }
            }
        }
    }

    [Fact]
    public void Desktop_only_energy_tweaks_need_strong_and_no_battery_security_and_hidden_never()
    {
        var power = Tweak("PERF-950", RiskLevel.Safe, ImpactLevel.Small);
        var desktop = new ProfileEntry { Id = power.Id, Condition = ProfileCondition.DesktopOnly };

        PresetLevels.Includes(PresetLevel.Standard, power, desktop, hasBattery: false).Should().BeFalse();
        PresetLevels.Includes(PresetLevel.Strong, power, desktop, hasBattery: true).Should().BeFalse();
        PresetLevels.Includes(PresetLevel.Strong, power, desktop, hasBattery: false).Should().BeTrue();

        var security = Tweak("GAME-950", RiskLevel.Safe, ImpactLevel.Noticeable, "Gaming.Security");
        PresetLevels.Includes(PresetLevel.Strong, security, new ProfileEntry { Id = security.Id }, false).Should().BeFalse();

        var hidden = Tweak("PERF-951", RiskLevel.Safe, ImpactLevel.Noticeable, hidden: true);
        PresetLevels.Includes(PresetLevel.Strong, hidden, new ProfileEntry { Id = hidden.Id }, false).Should().BeFalse();

        var revert = new ProfileEntry { Id = hidden.Id, Target = ProfileTarget.NotApplied };
        PresetLevels.Includes(PresetLevel.Gentle, hidden, revert, true).Should().BeTrue("eigene Aenderungen zuruecksetzen ist immer erlaubt");
    }

    [Fact]
    public void Cleanup_levels_grow_from_user_areas_to_recycle_bin_and_caution()
    {
        var gentle = CleanupCategories.All.Where(c => PresetLevels.IncludesCleanup(PresetLevel.Gentle, c)).ToList();
        var standard = CleanupCategories.All.Where(c => PresetLevels.IncludesCleanup(PresetLevel.Standard, c)).ToList();
        var strong = CleanupCategories.All.Where(c => PresetLevels.IncludesCleanup(PresetLevel.Strong, c)).ToList();

        gentle.Should().OnlyContain(c => c.Scope == TweakScope.User && c.Risk == RiskLevel.Safe && c.Handler != CleanupHandler.RecycleBin);
        standard.Should().Contain(gentle).And.Contain(c => c.Scope == TweakScope.Machine).And.NotContain(c => c.Handler == CleanupHandler.RecycleBin);
        strong.Should().Contain(standard).And.Contain(c => c.Handler == CleanupHandler.RecycleBin).And.Contain(c => c.Risk == RiskLevel.Caution);
    }

    [Fact]
    public void Undoing_preset_a_keeps_everything_preset_b_set_or_wants()
    {
        var ownership = new PresetOwnership();
        ownership.Claim("a", ["T1", "T2"]);
        ownership.Claim("b", ["T2", "T3"]);

        ownership.Release("a").Should().Equal("T1");
        ownership.OwnersOf("T2").Should().Equal("b");
        ownership.Owns("a").Should().BeFalse();

        ownership.Release("b").Should().Equal("T2", "T3");
        ownership.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Later_preset_wins_and_reverting_drops_earlier_claims()
    {
        var ownership = new PresetOwnership();
        ownership.Claim("gaming", ["PERF-001"]);
        ownership.Drop(["PERF-001"]);
        ownership.Claim("laptop", []);

        ownership.OwnersOf("PERF-001").Should().BeEmpty("Laptop hat den Energieplan zurueckgesetzt");
        ownership.Release("gaming").Should().BeEmpty();

        var copy = new PresetOwnership(new Dictionary<string, IReadOnlyList<string>> { ["X"] = ["a", "b"] });
        copy.OwnersOf("X").Should().Equal("a", "b");
    }

    private static TweakDefinition Tweak(string id, RiskLevel risk, ImpactLevel impact, string category = "Performance.Test", bool hidden = false)
    {
        var t = TestTweaks.Dword(id, TestTweaks.Base, "X", 0, risk: risk);
        return new TweakDefinition
        {
            Id = t.Id,
            Category = category,
            NameKey = t.NameKey,
            DescriptionKey = t.DescriptionKey,
            SideEffectsKey = t.SideEffectsKey,
            Risk = risk,
            Evidence = hidden ? EvidenceLevel.Disputed : EvidenceLevel.Proven,
            Builds = t.Builds,
            Editions = t.Editions,
            Scope = t.Scope,
            RequiresAdmin = t.RequiresAdmin,
            RequiresReboot = false,
            Detect = t.Detect,
            Apply = t.Apply,
            Revert = t.Revert,
            Sources = t.Sources,
            HiddenByDefault = hidden,
            Impact = impact,
        };
    }
}
