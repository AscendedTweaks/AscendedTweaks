using FluentAssertions;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;
using TweakPilot.Engine.Windows;
using Xunit;

namespace TweakPilot.Engine.Tests;

public sealed class AutostartTests : IDisposable
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --tray", @"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Tools\tool.exe /min", @"C:\Tools\tool.exe")]
    [InlineData(@"C:\Program Files\Ohne Anfuehrung\prog.EXE -x", @"C:\Program Files\Ohne Anfuehrung\prog.EXE")]
    [InlineData("rundll32 shell32.dll", "rundll32")]
    [InlineData("   ", null)]
    [InlineData("\"kaputt", null)]
    public void Executable_path_is_extracted_from_command_line(string command, string? expected)
    {
        AutostartInventory.ExtractPath(command).Should().Be(expected);
    }

    [Fact]
    public void Approval_follows_task_manager_convention()
    {
        AutostartInventory.IsApproved(null).Should().BeTrue();
        AutostartInventory.IsApproved(RegistryData.Binary([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])).Should().BeTrue();
        AutostartInventory.IsApproved(RegistryData.Binary([3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8])).Should().BeFalse();
        AutostartInventory.IsApproved(RegistryData.Binary([7])).Should().BeFalse();
    }

    [Theory]
    [InlineData("Realtek HD Audio", "Realtek Semiconductor Corp.", SignatureStatus.Valid, AutostartRating.Keep, "autostart.reason.driver")]
    [InlineData("SecurityHealth", "Microsoft Windows", SignatureStatus.Valid, AutostartRating.Keep, "autostart.reason.security")]
    [InlineData("Bitdefender Agent", "Bitdefender SRL", SignatureStatus.Valid, AutostartRating.Keep, "autostart.reason.security")]
    [InlineData("GoogleUpdater", "Google LLC", SignatureStatus.Valid, AutostartRating.Optional, "autostart.reason.updater")]
    [InlineData("OneDrive", "Microsoft Corporation", SignatureStatus.Valid, AutostartRating.Optional, "autostart.reason.microsoft")]
    [InlineData("Steam", "Valve Corp.", SignatureStatus.Valid, AutostartRating.Optional, "autostart.reason.known")]
    [InlineData("Unbekannt", null, SignatureStatus.Unsigned, AutostartRating.Unknown, "autostart.reason.unsigned")]
    [InlineData("Manipuliert", "Valve Corp.", SignatureStatus.Invalid, AutostartRating.Unknown, "autostart.reason.unsigned")]
    [InlineData("Verwaist", null, SignatureStatus.FileMissing, AutostartRating.Unknown, "autostart.reason.missing")]
    public void Entries_are_rated_with_reason(string name, string? publisher, SignatureStatus status, AutostartRating rating, string reason)
    {
        AutostartInventory.Rate(name, @"C:\x\" + name + ".exe", new SignatureInfo(status, publisher)).Should().Be((rating, reason));
    }

    [Fact]
    public void Catalog_signed_windows_files_are_kept()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "SecurityHealthSystray.exe");

        AutostartInventory.Rate("SecurityHealth", path, new SignatureInfo(SignatureStatus.Unsigned, null))
            .Should().Be((AutostartRating.Keep, "autostart.reason.windows"));
        AutostartInventory.Rate("Weg", path, new SignatureInfo(SignatureStatus.FileMissing, null)).Rating.Should().Be(AutostartRating.Unknown);
    }

    [Fact]
    public void Inventory_reads_run_keys_folders_and_tasks_without_writing()
    {
        _h.Registry.Seed(RegHive.HKCU, Run, "Steam", RegistryData.String("\"C:\\Steam\\steam.exe\" -silent"));
        _h.Registry.Seed(RegHive.HKCU, Run, "Fremd", RegistryData.String(@"C:\Temp\fremd.exe"));
        _h.Registry.Seed(RegHive.HKCU, Approved, "Fremd", RegistryData.Binary([3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        _h.Registry.Seed(RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "SecurityHealth", RegistryData.ExpandString(@"%windir%\system32\SecurityHealthSystray.exe"));
        _h.Registry.Seed(RegHive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "Einmal", RegistryData.String(@"C:\x\setup.exe"));
        var sources = new FakeSources
        {
            Files = [new StartupFile("Discord.lnk", @"C:\Discord\Update.exe", "--processStart Discord.exe", false)],
            Tasks = [new LogonTask(@"\Hersteller\Tray", "\"C:\\Hersteller\\tray.exe\"", true)],
        };
        var signatures = new FakeSignatures
        {
            [@"C:\Steam\steam.exe"] = new SignatureInfo(SignatureStatus.Valid, "Valve Corp."),
            [@"C:\Temp\fremd.exe"] = new SignatureInfo(SignatureStatus.Unsigned, null),
            [@"C:\Discord\Update.exe"] = new SignatureInfo(SignatureStatus.Valid, "Discord Inc."),
        };
        var writes = _h.Registry.WriteCount;

        var entries = new AutostartInventory(_h.Registry, sources, signatures).Read();

        _h.Registry.WriteCount.Should().Be(writes);
        entries.Select(e => e.Name).Should().BeEquivalentTo("Steam", "Fremd", "SecurityHealth", "Einmal", "Discord.lnk", @"\Hersteller\Tray");
        var steam = entries.Single(e => e.Name == "Steam");
        steam.Enabled.Should().BeTrue();
        steam.TweakId.Should().Be("PERF-026/HKCU-RUN/Steam");
        steam.CanDisable.Should().BeTrue();
        var fremd = entries.Single(e => e.Name == "Fremd");
        fremd.Enabled.Should().BeFalse();
        fremd.Rating.Should().Be(AutostartRating.Unknown);
        fremd.CanDisable.Should().BeFalse();
        entries.Single(e => e.Name == "SecurityHealth").Should().Match<AutostartEntry>(e => e.MachineWide && e.TweakId == "PERF-026/HKLM-RUN/SecurityHealth" && !e.CanDisable);
        entries.Single(e => e.Name == "Einmal").TweakId.Should().BeNull("RunOnce kennt keine Freigabe");
        entries.Single(e => e.Name == "Discord.lnk").Should().Match<AutostartEntry>(e => e.TweakId == "PERF-026/USER-STARTUP/Discord.lnk" && e.ReasonKey == "autostart.known.AUTO-003" && e.Known!.Id == "AUTO-003");
        entries.Single(e => e.Source == AutostartSource.LogonTask).TweakId.Should().BeNull("Aufgaben von Drittanbietern sind nur Anzeige");
    }

    [Fact]
    public void Generated_tweak_disables_and_restores_the_original_approval()
    {
        _h.Registry.Seed(RegHive.HKCU, Run, "Steam", RegistryData.String(@"C:\Steam\steam.exe"));
        _h.Registry.Seed(RegHive.HKCU, Approved, "Steam", RegistryData.Binary([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        var before = _h.Registry.Snapshot();
        var id = AutostartTweaks.IdFor(AutostartLocation.UserRun, "Steam");
        var engine = new TweakEngine(new CompositeCatalog(new ListCatalog()), _h.Executors, _h.Store, _h.Environment, _h.RestorePoints, new FakeTime());

        engine.ApplyBatch([id], new ApplyOptions()).Outcome.Should().Be(BatchOutcome.Committed);
        AutostartInventory.IsApproved(_h.Registry.GetValue(RegHive.HKCU, Approved, "Steam")).Should().BeFalse();
        _h.Registry.GetValue(RegHive.HKCU, Run, "Steam").Should().NotBeNull("der Eintrag selbst bleibt");
        engine.GetAppliedTweaks().Single().TweakId.Should().Be(id);

        engine.RevertAll(_ => ConflictDecision.Cancel).Single().Outcome.Should().Be(RevertOutcome.Reverted);
        _h.Registry.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("PERF-026/HKCU-RUN/Steam", true)]
    [InlineData("PERF-026/HKLM-RUN32/Tool mit Leerzeichen", true)]
    [InlineData("PERF-026/HKCU-RUNONCE/Steam", false)]
    [InlineData("PERF-026/HKCU-RUN/", false)]
    [InlineData("PERF-026/HKCU-RUN/Zeile\nUmbruch", false)]
    [InlineData("PERF-027/HKCU-RUN/Steam", false)]
    public void Autostart_ids_are_parsed_strictly(string id, bool valid)
    {
        AutostartTweaks.TryCreate(id, out var tweak).Should().Be(valid);
        if (valid)
        {
            tweak.Id.Should().Be(id);
            tweak.Module.Should().Be("PERF");
        }
    }

    [Fact]
    public void Machine_entries_need_the_helper()
    {
        AutostartTweaks.TryCreate("PERF-026/HKLM-RUN/X", out var machine).Should().BeTrue();
        machine.Scope.Should().Be(TweakScope.Machine);
        machine.RequiresAdmin.Should().BeTrue();
        AutostartTweaks.TryCreate("PERF-026/USER-STARTUP/X.lnk", out var user).Should().BeTrue();
        user.Scope.Should().Be(TweakScope.User);
        ((Action)(() => AutostartTweaks.IdFor(AutostartLocation.UserRun, new string('x', 300)))).Should().Throw<TweakPilotException>();
    }

    [Fact]
    public void Logon_tasks_are_parsed_from_the_schtasks_dump_without_windows_tasks()
    {
        const string ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var dump = $"""
            <Tasks>
            <!-- \Hersteller\Tray -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="{ns}"><Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers><Settings><Enabled>false</Enabled></Settings><Actions><Exec><Command>C:\Hersteller\tray.exe</Command><Arguments>/hidden</Arguments></Exec></Actions></Task>

            <!-- \Hersteller\Nachts -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="{ns}"><Triggers><CalendarTrigger /></Triggers><Actions><Exec><Command>C:\x.exe</Command></Exec></Actions></Task>

            <!-- \Microsoft\Windows\Intern -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="{ns}"><Triggers><LogonTrigger /></Triggers><Actions><Exec><Command>C:\y.exe</Command></Exec></Actions></Task>

            <!-- \Kaputt -->
            <?xml version="1.0" encoding="UTF-16"?>
            <Task xmlns="{ns}"><Triggers>
            </Tasks>
            """;

        var tasks = WindowsAutostartSources.ParseLogonTasks(dump);

        tasks.Should().ContainSingle().Which.Should().Be(new LogonTask(@"\Hersteller\Tray", "\"C:\\Hersteller\\tray.exe\" /hidden", false));
    }

    [Fact]
    public async Task Process_overview_groups_by_name_and_normalizes_cpu()
    {
        var first = new[]
        {
            new ProcessSample(1, "chrome", TimeSpan.FromMilliseconds(100), 100),
            new ProcessSample(2, "chrome", TimeSpan.FromMilliseconds(100), 200),
            new ProcessSample(3, "game", TimeSpan.FromMilliseconds(0), 1000),
            new ProcessSample(4, "geschuetzt", null, 50),
        };
        var second = new[]
        {
            new ProcessSample(1, "chrome", TimeSpan.FromMilliseconds(300), 100),
            new ProcessSample(2, "chrome", TimeSpan.FromMilliseconds(300), 200),
            new ProcessSample(3, "game", TimeSpan.FromMilliseconds(1600), 1000),
            new ProcessSample(4, "geschuetzt", null, 50),
            new ProcessSample(5, "neu", TimeSpan.FromMilliseconds(500), 10),
            new ProcessSample(0, "Idle", TimeSpan.FromHours(1), 0),
        };

        var result = ProcessOverview.Compute(first, second, TimeSpan.FromSeconds(1), processorCount: 2);

        result.ByMemory.Select(g => g.Name).Should().Equal("game", "chrome", "geschuetzt", "neu");
        result.ByMemory[1].Count.Should().Be(2);
        result.ByCpu[0].Name.Should().Be("game");
        result.ByCpu[0].CpuPercent.Should().BeApproximately(80, 0.001);
        result.ByCpu[1].CpuPercent.Should().BeApproximately(20, 0.001);
        result.ByCpu.Single(g => g.Name == "neu").CpuPercent.Should().Be(0, "ohne erste Messung kein Anteil");
        result.TotalWorkingSetBytes.Should().Be(1360);

        var sampler = new SequenceSampler(first, second);
        var measured = await new ProcessOverview(sampler, _ => Task.CompletedTask, 2).MeasureAsync(TimeSpan.FromSeconds(1));
        measured.ByMemory.Should().HaveCount(4);
    }

    private sealed class FakeSources : IAutostartSources
    {
        public IReadOnlyList<StartupFile> Files { get; init; } = [];

        public IReadOnlyList<LogonTask> Tasks { get; init; } = [];

        public IReadOnlyList<StartupFile> StartupFolderFiles() => Files;

        public IReadOnlyList<LogonTask> LogonTasks() => Tasks;
    }

    private sealed class FakeSignatures : Dictionary<string, SignatureInfo>, ISignatureVerifier
    {
        public FakeSignatures()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public SignatureInfo Verify(string filePath) => TryGetValue(Environment.ExpandEnvironmentVariables(filePath), out var info)
            ? info
            : new SignatureInfo(SignatureStatus.Valid, "Microsoft Windows");
    }

    private sealed class SequenceSampler(params IReadOnlyList<ProcessSample>[] samples) : IProcessSampler
    {
        private int _index;

        public IReadOnlyList<ProcessSample> Sample() => samples[_index++];
    }
}
