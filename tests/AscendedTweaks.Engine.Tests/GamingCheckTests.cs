using AscendedTweaks.Engine.Insights;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

/// <summary>V2-5: die zehn Gaming-Checks mit Fake-Daten (Abschnitt 5.3). Checks lesen nur und aendern nie etwas.</summary>
public sealed class GamingCheckTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void Healthy_desktop_passes_every_check_in_a_fixed_order()
    {
        var checks = GamingChecks.Evaluate(new GamingFacts
        {
            Display = new DisplayFacts(144, 144),
            ActivePowerPlan = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            Gpus = [new GpuInfo("NVIDIA", "RTX", "1", Today.AddDays(-30), null)],
            MemoryModules = [new MemoryModule(6000, 6000), new MemoryModule(6000, 6000)],
            SystemDriveFreePercent = 40,
            GameMode = true,
            Autostarts = 5,
            Security = new SecurityFacts(true, 2, true, true, false, false),
            Today = Today,
        });

        checks.Select(c => c.Id).Should().Equal("refresh", "power", "hybrid", "ramspeed", "ramcount", "disk", "gamemode", "driver", "background", "anticheat");
        checks.Where(c => c.Id != "background").Should().OnlyContain(c => c.State == CheckState.Good);
    }

    [Fact]
    public void Problems_give_one_sentence_a_fix_and_a_link_or_jump()
    {
        var checks = GamingChecks.Evaluate(new GamingFacts
        {
            Display = new DisplayFacts(60, 165),
            ActivePowerPlan = GamingChecks.PowerSaverPlan,
            HasBattery = true,
            Gpus = [new GpuInfo("Intel", "Iris", "1", Today.AddDays(-20), null), new GpuInfo("NVIDIA", "RTX", "1", Today.AddDays(-500), null)],
            MemoryModules = [new MemoryModule(6000, 4800)],
            SystemDriveFreePercent = 9,
            GameMode = false,
            Autostarts = 14,
            TopProcess = new ProcessGroup("Discord", 3, 400L * 1024 * 1024, 0),
            Security = new SecurityFacts(false, 2, true, true, false, false),
            Today = Today,
        }).ToDictionary(c => c.Id);

        checks["refresh"].Should().Match<GamingCheck>(c => c.State == CheckState.Attention && c.Link == "ms-settings:display-advanced" && c.HowToKey != null);
        checks["refresh"].Args.Should().Contain([("current", (object)60), ("max", (object)165)]);
        checks["power"].State.Should().Be(CheckState.Attention);
        checks["hybrid"].Should().Match<GamingCheck>(c => c.State == CheckState.Info && c.Link == "ms-settings:display-advancedgraphics" && c.Jump == GamingJump.GameProfiles);
        checks["ramspeed"].TextKey.Should().Be("check.ramspeed.low");
        checks["ramcount"].TextKey.Should().Be("check.ramcount.one");
        checks["disk"].Should().Match<GamingCheck>(c => c.State == CheckState.Attention && c.Jump == GamingJump.Cleanup);
        checks["gamemode"].TextKey.Should().Be("check.gamemode.off");
        checks["driver"].Should().Match<GamingCheck>(c => c.TextKey == "check.driver.old" && c.Link == GamingChecks.VendorPage("NVIDIA"));
        checks["background"].Args.Should().Contain(("name", (object)"Discord"));
        checks["anticheat"].Should().Match<GamingCheck>(c => c.State == CheckState.Attention && c.Jump == GamingJump.AntiCheat);
        checks.Values.Where(c => c.Link is not null).Should().OnlyContain(c => c.Link!.StartsWith("ms-settings:") || c.Link.StartsWith("https://"));
    }

    [Fact]
    public void Missing_or_ambiguous_values_are_unknown_never_a_guess()
    {
        var checks = GamingChecks.Evaluate(new GamingFacts
        {
            MemoryModules = [new MemoryModule(6000, 4800), new MemoryModule(5600, 5600)],
            Gpus = [new GpuInfo("Microsoft", "Basic Display", null, null, null)],
            Today = Today,
        }).ToDictionary(c => c.Id);

        checks["ramspeed"].State.Should().Be(CheckState.Unknown, "Module mit unterschiedlichen Werten sind kein eindeutiger Messwert");
        checks["ramcount"].State.Should().Be(CheckState.Good);
        foreach (var id in new[] { "refresh", "power", "disk", "gamemode", "driver", "background", "anticheat" })
        {
            checks[id].State.Should().Be(CheckState.Unknown, id);
        }

        GamingChecks.Evaluate(new GamingFacts { MemoryModules = [new MemoryModule(null, 4800)], Today = Today })
            .Single(c => c.Id == "ramspeed").State.Should().Be(CheckState.Unknown);
    }
}
