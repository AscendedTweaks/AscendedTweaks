using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.Export;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class ExportTests : IDisposable
{
    private const string P = TestTweaks.Base;
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public void Export_reads_original_values_and_writes_matching_reset_steps()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        _h.Registry.Seed(RegHive.HKCU, P, "A", RegistryData.DWord(1));
        _h.Services.Services["Fax"] = new ServiceConfig(ServiceStartType.Automatic, true, false);
        _h.Tasks.Tasks[@"\Microsoft\Windows\Test\Aufgabe"] = true;
        var active = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        var high = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        _h.Power.Active = active;
        _h.Power.Schemes.AddRange([active, high]);
        var export = Exporter(
            TestTweaks.Dword("PRIV-901", P, "A", 0),
            TestTweaks.Dword("PRIV-902", P + @"\Neu\Tiefer", "B", 5),
            TestTweaks.Service("PERF-916", "Fax", ServiceStartSetting.Disabled),
            TestTweaks.Create("PERF-921", new ScheduledTaskEnabledEqualsCondition { TaskPath = @"\Microsoft\Windows\Test\Aufgabe", Value = false }, [new ScheduledTaskSetEnabledAction { TaskPath = @"\Microsoft\Windows\Test\Aufgabe", Enabled = false }], TweakScope.Machine),
            TestTweaks.Power("PERF-901", high)).Export(["PRIV-901", "PRIV-902", "PERF-916", "PERF-921", "PERF-901"], id => "Name " + id, "Test", Now);

        export.Exported.Should().Equal("PRIV-901", "PRIV-902", "PERF-916", "PERF-921", "PERF-901");
        export.ApplyPs1.Should().Contain("IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)", "Maschinen-Tweaks: Pruefung statt Rechteanforderung");
        export.ApplyPs1.Should().NotContainAny("Start-Process", "-Verb RunAs", "Invoke-WebRequest", "Invoke-Expression", "-EncodedCommand", "DownloadString");
        export.ApplyPs1.Should().Contain("New-ItemProperty -LiteralPath 'HKCU:\\" + P + "' -Name 'A' -PropertyType DWord -Value 0 -Force");
        export.ApplyPs1.Should().Contain("Set-Service -Name 'Fax' -StartupType Disabled").And.Contain("Stop-Service -Name 'Fax'");
        export.ApplyPs1.Should().Contain(@"Disable-ScheduledTask -TaskPath '\Microsoft\Windows\Test\' -TaskName 'Aufgabe'");
        export.ApplyPs1.Should().Contain("/setactive " + high.ToString("D"));

        export.RevertPs1.Should().Contain("-Name 'A' -PropertyType DWord -Value 1 -Force");
        export.RevertPs1.Should().Contain("sc.exe\" config 'Fax' start= delayed-auto", "Originalwert automatisch (verzoegert)");
        export.RevertPs1.Should().Contain(@"Enable-ScheduledTask -TaskPath '\Microsoft\Windows\Test\' -TaskName 'Aufgabe'");
        export.RevertPs1.Should().Contain("/setactive " + active.ToString("D"));
        export.RevertPs1.Should().Contain(@"Get-Item -LiteralPath 'HKCU:\" + P + @"\Neu\Tiefer'").And.Contain(@"Get-Item -LiteralPath 'HKCU:\" + P + @"\Neu'", "neu angelegte Elternschluessel werden leer wieder entfernt");
        export.RevertPs1.IndexOf("PERF-901", StringComparison.Ordinal).Should().BeLessThan(export.RevertPs1.IndexOf("PRIV-901", StringComparison.Ordinal), "Zuruecksetzen in umgekehrter Reihenfolge");

        export.ApplyReg.Should().Contain("\"A\"=dword:00000000").And.NotContain("Fax", ".reg nur fuer reine Registry-Tweaks");
        export.RevertReg.Should().Contain("\"A\"=dword:00000001").And.Contain("\"B\"=-");
        _h.TotalWrites.Should().Be(0, "Export liest nur");
    }

    [Fact]
    public void Tweaks_that_need_the_app_are_skipped_completely_and_named_in_the_header()
    {
        var spi = TestTweaks.Create("PERF-905", new SystemParameterEqualsCondition { Parameter = "MenuShowDelay", Value = 100 }, [new SystemParametersInfoAction { Parameter = "MenuShowDelay", Value = 100 }]);

        var export = Exporter(spi).Export(["PERF-905", "PRIV-999"], id => "Name " + id, "Test", Now);

        export.Exported.Should().BeEmpty();
        export.Skipped.Select(s => s.Id).Should().Equal("PERF-905", "PRIV-999");
        export.ApplyPs1.Should().Contain("# Nicht exportiert: PERF-905").And.NotContain("# ---- PERF-905");
        export.ApplyReg.Should().BeNull();
    }

    [Fact]
    public void Texts_cannot_break_out_of_strings_or_comments()
    {
        _h.Registry.SeedKey(RegHive.HKCU, "Software");
        var tweak = TestTweaks.Create(
            "PRIV-907",
            new RegistryValueEqualsCondition { Hive = RegHive.HKCU, Path = P, Name = "it's", Value = new RegLiteral { Text = "a'b\"c" } },
            [new RegistrySetValueAction { Hive = RegHive.HKCU, Path = P, Name = "it's", Kind = RegValueKind.String, Value = new RegLiteral { Text = "a'b\"c" } }]);

        var export = Exporter(tweak).Export(["PRIV-907"], _ => "Zeile1\r\nRemove-Item C:\\ -Recurse", "Titel\nWrite-Host x", Now);

        export.ApplyPs1.Should().Contain("-Name 'it''s'").And.Contain("-Value 'a''b\"c'");
        export.ApplyPs1.Split('\n').Should().NotContain(l => l.TrimStart().StartsWith("Remove-Item C:", StringComparison.Ordinal) || l.TrimStart().StartsWith("Write-Host", StringComparison.Ordinal));
        export.ApplyReg.Should().Contain("\"it's\"=\"a'b\\\"c\"");
    }

    private ScriptExporter Exporter(params TweakDefinition[] tweaks) => new(_h.Executors, new ListCatalog(tweaks));
}
