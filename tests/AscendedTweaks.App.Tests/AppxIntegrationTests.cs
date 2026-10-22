using AscendedTweaks.Appx;
using AscendedTweaks.Engine.Debloat;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

/// <summary>Echte Paketverwaltung, ohne etwas zu entfernen.</summary>
public sealed class AppxIntegrationTests
{
    [Fact]
    public void Real_package_list_is_readable_and_registration_by_family_works()
    {
        var appx = new WindowsAppxAccess();
        var installed = appx.ListForCurrentUser();

        installed.Should().Contain(p => p.Name == "Microsoft.WindowsStore" && p.IsMicrosoft, "der Store ist auf jedem unterstuetzten Windows installiert");
        installed.Should().Contain(p => p.IsFramework);

        // Wiederherstellungsweg (a): Neuregistrierung per Paketfamilie. Fuer ein bereits installiertes Paket aendert das nichts.
        var candidate = installed.FirstOrDefault(p => DebloatCatalog.Find(p.Name) is not null && !p.IsFramework && !p.IsResource);
        if (candidate is not null)
        {
            appx.RegisterByFamilyName(candidate.FamilyName).Should().Be(AppxRestoreOutcome.Registered);
            appx.ListForCurrentUser().Should().Contain(p => p.FullName == candidate.FullName);
        }
    }
}
