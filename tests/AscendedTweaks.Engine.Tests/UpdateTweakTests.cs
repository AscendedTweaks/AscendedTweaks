using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class UpdateTweakTests : IDisposable
{
    private const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("UPD-001/2026-10-18/35", true)]
    [InlineData("UPD-001/2026-10-19/7", true)]
    [InlineData("UPD-001/2025-01-01/7", true)]
    [InlineData("UPD-001/2026-10-18/36", false)]
    [InlineData("UPD-001/2026-10-18/0", false)]
    [InlineData("UPD-001/2026-10-21/7", false)]
    [InlineData("UPD-001/2030-01-01/35", false)]
    [InlineData("UPD-001/2026-10-18/07", false)]
    [InlineData("UPD-001/18.10.2026/7", false)]
    [InlineData("UPD-001/2026-10-18/7/1", false)]
    [InlineData("UPD-007/11/25H2", true)]
    [InlineData("UPD-007/10/22H2", true)]
    [InlineData("UPD-007/12/25H2", false)]
    [InlineData("UPD-007/11/25H3", false)]
    [InlineData("UPD-007/11/..\\x", false)]
    [InlineData("UPD-002/11/25H2", false)]
    public void Generated_update_ids_are_strictly_checked(string id, bool valid)
    {
        UpdateTweaks.TryCreate(id, Now, out _).Should().Be(valid);
    }

    [Fact]
    public void Pause_never_exceeds_35_days_and_is_hidden_until_verified()
    {
        UpdateTweaks.TryCreate(UpdateTweaks.PauseIdFor(new DateOnly(2026, 10, 18), 35), Now, out var tweak).Should().BeTrue();

        tweak.Evidence.Should().Be(EvidenceLevel.Disputed);
        tweak.HiddenByDefault.Should().BeTrue();
        tweak.Scope.Should().Be(TweakScope.Machine);
        var values = tweak.Apply.Cast<RegistrySetValueAction>().ToDictionary(a => a.Name, a => a.Value.Text);
        values["PauseFeatureUpdatesStartTime"].Should().Be("2026-10-18T00:00:00Z");
        values["PauseUpdatesExpiryTime"].Should().Be("2026-11-22T00:00:00Z");
        values["PauseQualityUpdatesEndTime"].Should().Be("2026-11-22T00:00:00Z");
    }

    [Fact]
    public void Pause_applies_and_reverts_to_the_previous_values()
    {
        _h.Registry.Seed(RegHive.HKLM, UxSettings, "ActiveHoursStart", RegistryData.DWord(8));
        var id = UpdateTweaks.PauseIdFor(DateOnly.FromDateTime(DateTime.UtcNow), 14);
        var engine = new TweakEngine(new CompositeCatalog(new ListCatalog()), _h.Executors, _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());

        engine.ApplyBatch([id], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        engine.GetStatus(id).State.Should().Be(TweakState.Applied);
        _h.Registry.GetValue(RegHive.HKLM, UxSettings, "PauseUpdatesExpiryTime")!.Text.Should().EndWith("T00:00:00Z");

        engine.RevertTweak(id, _ => ConflictDecision.Cancel).Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Registry.GetValue(RegHive.HKLM, UxSettings, "PauseUpdatesExpiryTime").Should().BeNull();
        _h.Registry.GetValue(RegHive.HKLM, UxSettings, "ActiveHoursStart").Should().NotBeNull("fremde Werte bleiben");
    }

    [Fact]
    public void Target_version_is_expert_pro_only_and_limited_to_the_product()
    {
        UpdateTweaks.TryCreate("UPD-007/11/25H2", Now, out var eleven).Should().BeTrue();
        eleven.Risk.Should().Be(RiskLevel.Expert);
        eleven.Editions.Should().NotContain(WindowsEdition.Home);
        eleven.Builds.Single().Min.Should().Be(22000);
        eleven.Apply.Cast<RegistrySetValueAction>().Select(a => (a.Name, a.Value.Text ?? a.Value.Number.ToString())).Should().Equal(
            ("TargetReleaseVersion", "1"), ("TargetReleaseVersionInfo", "25H2"), ("ProductVersion", "Windows 11"));

        UpdateTweaks.TryCreate("UPD-007/10/22H2", Now, out var ten).Should().BeTrue();
        ten.Builds.Single().Max.Should().Be(19045);
    }

    [Fact]
    public void Composite_catalog_resolves_update_ids()
    {
        var catalog = new CompositeCatalog(new ListCatalog());
        catalog.TryGet(UpdateTweaks.PauseIdFor(DateOnly.FromDateTime(DateTime.UtcNow), 7), out _).Should().BeTrue();
        catalog.TryGet("UPD-007/11/24H2", out _).Should().BeTrue();
        catalog.TryGet("UPD-001/2099-01-01/7", out _).Should().BeFalse();
    }
}
