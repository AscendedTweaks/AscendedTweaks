using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Tools;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class Phase10PageTests
{
    private readonly Localizer _localizer = new(Localizer.German);
    private readonly FakeNetwork _network = new();
    private readonly FakeDisks _disks = new();

    [Fact]
    public void Section_page_creates_sections_lazily_and_keeps_them()
    {
        var created = 0;
        var page = new SectionPageViewModel("nav.network",
        [
            (new SectionItem("a", "perf.section.tweaks"), () => new object()),
            (new SectionItem("b", "net.section.tools"), () => { created++; return new object(); }),
        ]);

        created.Should().Be(0, "nur der erste Bereich wird sofort angelegt");
        page.SelectedSection = page.Sections[1];
        var first = page.CurrentSection;
        page.SelectedSection = page.Sections[0];
        page.SelectedSection = page.Sections[1];

        created.Should().Be(1);
        page.CurrentSection.Should().BeSameAs(first);
    }

    [Theory]
    [InlineData("1.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("1.1.1.1%5")]
    [InlineData("")]
    public async Task Custom_dns_rejects_invalid_or_unusable_input_without_any_dialog(string input)
    {
        using var h = new AscendedTweaks.Engine.Tests.Harness();
        var dialogs = new ScriptedDialogs(_ => true);
        var vm = new CustomDnsViewModel(Workspace(h, dialogs), dialogs) { Input = input };

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.Error.Should().NotBeNull();
        dialogs.Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task Custom_dns_asks_before_private_addresses_and_does_nothing_on_cancel()
    {
        using var h = new AscendedTweaks.Engine.Tests.Harness();
        var dialogs = new ScriptedDialogs(_ => false);
        var vm = new CustomDnsViewModel(Workspace(h, dialogs), dialogs) { Input = "192.168.1.1; 1.1.1.1" };

        await vm.ApplyCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        dialogs.Shown.Should().ContainSingle();
        dialogs.Last<ConfirmDialogViewModel>()!.IsDanger.Should().BeTrue();
    }

    [Fact]
    public async Task Ping_shows_summary_and_doh_text()
    {
        var vm = new NetworkToolsViewModel(_network, new ScriptedDialogs(_ => true), _localizer);

        await vm.LoadAsync();
        await vm.PingCommand.ExecuteAsync(null);

        vm.DohText.Should().Contain("1.1.1.1");
        vm.Results.Should().ContainSingle();
        vm.Results[0].Summary.Should().Contain("49").And.Contain("50");
        _network.Pinged.Should().Equal("1.1.1.1");
    }

    [Fact]
    public async Task Invalid_ping_target_is_reported_not_thrown()
    {
        var vm = new NetworkToolsViewModel(_network, new ScriptedDialogs(_ => true), _localizer) { Target = "kein ziel" };

        await vm.PingCommand.ExecuteAsync(null);

        vm.Results.Single().Summary.Should().NotBeNullOrEmpty();
        vm.IsPinging.Should().BeFalse();
    }

    [Fact]
    public async Task Network_reset_needs_confirmation()
    {
        var no = new NetworkToolsViewModel(_network, new ScriptedDialogs(_ => false), _localizer);
        await no.NetworkResetCommand.ExecuteAsync(null);
        _network.Resets.Should().Be(0);

        var dialogs = new ScriptedDialogs(_ => true);
        var yes = new NetworkToolsViewModel(_network, dialogs, _localizer);
        await yes.NetworkResetCommand.ExecuteAsync(null);
        _network.Resets.Should().Be(1);
        dialogs.Last<ConfirmDialogViewModel>()!.IsDanger.Should().BeTrue();
        dialogs.Last<ResultDialogViewModel>()!.RebootRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Drives_offer_retrim_only_for_ssd_and_defrag_only_for_hdd()
    {
        var vm = new DrivesViewModel(_disks, new ScriptedDialogs(_ => true), _localizer, readOnly: false);

        await vm.LoadAsync();

        var ssd = vm.Volumes.Single(v => v.Volume.Drive == 'C');
        var hdd = vm.Volumes.Single(v => v.Volume.Drive == 'D');
        var unknown = vm.Volumes.Single(v => v.Volume.Drive == 'E');
        (ssd.CanReTrim, ssd.CanDefrag).Should().Be((true, false));
        (hdd.CanReTrim, hdd.CanDefrag).Should().Be((false, true));
        (unknown.CanReTrim, unknown.CanDefrag).Should().Be((false, false));
        ssd.DefragNote.Should().NotBeNull();
        hdd.DefragNote.Should().BeNull();

        await vm.ReTrimCommand.ExecuteAsync(ssd);
        _disks.Runs.Should().Equal((MaintenanceAction.ReTrim, 'C'));
    }

    [Fact]
    public async Task Drives_in_read_only_mode_offer_no_maintenance()
    {
        var vm = new DrivesViewModel(_disks, new ScriptedDialogs(_ => true), _localizer, readOnly: true);

        await vm.LoadAsync();

        vm.Volumes.Should().OnlyContain(v => !v.CanReTrim && !v.CanDefrag);
    }

    [Fact]
    public async Task Health_shows_values_and_failure_warning()
    {
        var vm = new DrivesViewModel(_disks, new ScriptedDialogs(_ => true), _localizer, readOnly: false);

        await vm.ReadHealthCommand.ExecuteAsync(null);

        vm.Health.Should().ContainSingle();
        vm.Health[0].Title.Should().Be("Testplatte");
        vm.Health[0].Detail.Should().Contain("41").And.Contain("1234");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Large_files_are_grouped_nothing_is_preselected_and_protected_rows_cannot_be_selected()
    {
        var keep = new MemoryKeep();
        var dialogs = new ScriptedDialogs(_ => true);
        var vm = new LargeFilesViewModel(_disks, _localizer, dialogs, keep);

        await vm.ScanCommand.ExecuteAsync(null);

        vm.GroupA.Select(r => r.Name).Should().Equal("setup.iso", "alt.zip");
        vm.GroupB.Select(r => r.Name).Should().Equal("film.mkv", "kopie.mkv");
        vm.Protected.Select(r => r.Name).Should().Equal("pakchunk0.pak", "steuer.pdf");
        vm.GroupA.Concat(vm.GroupB).Should().OnlyContain(r => !r.IsSelected, "nie vorausgewaehlt");
        vm.GroupA[0].Why.Should().Be("Installations- oder Archivdatei im Downloads-Ordner, seit 112 Tagen unverändert.");
        vm.GroupB[1].Duplicate.Should().Be("Kopie");
        vm.Status.Should().Be("Fertig: 2 kann meist weg, 2 bitte prüfen, 2 geschützt.");
        vm.Games.Single().Size.Should().Contain("GB");

        vm.Protected[0].IsSelected = true;
        vm.Protected[0].IsSelected.Should().BeFalse("Klasse D ist nie auswaehlbar");
        vm.SelectAllACommand.Execute(null);
        vm.GroupA.Should().OnlyContain(r => r.IsSelected);
        vm.GroupB.Should().OnlyContain(r => !r.IsSelected, "Gruppe B wird nie gesammelt ausgewaehlt");
        vm.RecycleText.Should().Be("In den Papierkorb (2, 8,0 GB)");

        await vm.RecycleCommand.ExecuteAsync(null);

        dialogs.Last<ConfirmDialogViewModel>()!.Body.Should().Be("2 Dateien, zusammen 8,0 GB. Die größten: setup.iso, alt.zip. Wirklich in den Papierkorb verschieben?");
        _disks.Recycled.Should().Equal(@"C:\Users\a\Downloads\setup.iso", @"C:\Users\a\Downloads\alt.zip");
        _disks.GameRootsSeen.Should().Equal(@"D:\Games\Fortnite");
        vm.GroupA.Should().ContainSingle(r => r.Name == "alt.zip", "uebersprungene Dateien bleiben stehen");
        dialogs.Last<ResultDialogViewModel>()!.Rows.Should().Contain(r => !r.Ok && r.Message == "Übersprungen: Datei ist gerade geöffnet.");

        vm.KeepFolderCommand.Execute(vm.GroupB[0]);
        keep.Items.Should().Equal(@"C:\Users\a\Desktop");
        vm.GroupB.Should().ContainSingle(r => r.Name == "kopie.mkv");
        vm.ShowCommand.Execute(vm.Protected[0]);
        _disks.Shown.Should().Equal(@"D:\Games\Fortnite\Paks\pakchunk0.pak");
    }

    [Fact]
    public void Keep_list_entries_can_be_removed_in_the_settings()
    {
        var keep = new MemoryKeep();
        keep.Add(@"C:\Users\a\Desktop");
        keep.Add(@"C:\Users\a\Downloads\x.iso");
        var vm = new KeepListViewModel(keep);

        vm.RemoveCommand.Execute(@"C:\Users\a\Desktop");

        vm.Entries.Should().Equal(@"C:\Users\a\Downloads\x.iso");
        vm.IsEmpty.Should().BeFalse();
    }

    private sealed class MemoryKeep : IKeepListStore
    {
        public List<string> Items { get; } = [];

        public IReadOnlyList<string> All() => Items.ToList();

        public void Add(string path) => Items.Add(path);

        public void Remove(string path) => Items.Remove(path);
    }

    private TweakWorkspace Workspace(AscendedTweaks.Engine.Tests.Harness h, ScriptedDialogs dialogs)
    {
        var catalog = new AscendedTweaks.Catalog.TweakCatalog([], new Dictionary<string, AscendedTweaks.Catalog.TextBundle>
        {
            [Localizer.German] = new(Localizer.German, new Dictionary<string, string>()),
            [Localizer.English] = new(Localizer.English, new Dictionary<string, string>()),
        });
        AscendedTweaks.Engine.TweakEngine Engine(string name) =>
            new(catalog, h.Executors, new AscendedTweaks.Engine.State.StateStore(Path.Combine(h.StateRoot, name), new AscendedTweaks.Engine.Tests.FakeTime()), h.Environment, new AscendedTweaks.Engine.NoRestorePointService(), new AscendedTweaks.Engine.Tests.FakeTime());
        var service = new AscendedTweaks.Ipc.TweakService(catalog, Engine("u"), Engine("m"), Engine("p"), () => throw new InvalidOperationException(), new AscendedTweaks.Engine.Tests.FakeTime());
        return new TweakWorkspace(catalog, service, _localizer, new AppSettings(), dialogs, new SyncInvoker(), new CountingRestarter(), readOnly: false);
    }

    private sealed class FakeNetwork : INetworkTools
    {
        public List<string> Pinged { get; } = [];

        public int Resets { get; private set; }

        public Task<PingStats> PingAsync(string target, IProgress<int>? progress, CancellationToken cancellation)
        {
            if (target.Contains(' ', StringComparison.Ordinal))
            {
                throw new AscendedTweaksException(ErrorKind.General, "Ungueltiges Ziel");
            }

            Pinged.Add(target);
            return Task.FromResult(new PingStats(target, 50, 49, 10, 12, 20, 1.5));
        }

        public bool FlushDnsCache() => true;

        public DohStatus Doh() => new(true, ["1.1.1.1"], ["1.1.1.1"]);

        public MaintenanceResult NetworkReset()
        {
            Resets++;
            return new MaintenanceResult(true, true, null);
        }

        public void OpenDnsSettings()
        {
        }
    }

    private sealed class FakeDisks : IDiskTools
    {
        public List<(MaintenanceAction, char)> Runs { get; } = [];

        public List<string> Shown { get; } = [];

        public IReadOnlyList<VolumeInfo> Volumes() =>
        [
            new('C', MediaKind.Ssd, 500_000_000_000, 100_000_000_000, "SSD"),
            new('D', MediaKind.Hdd, 2_000_000_000_000, 1_000_000_000_000, "HDD"),
            new('E', MediaKind.Unknown, 64_000_000_000, 1_000_000_000, null),
        ];

        public MaintenanceResult Run(MaintenanceAction action, char drive)
        {
            Runs.Add((action, drive));
            return new MaintenanceResult(true, false, null);
        }

        public IReadOnlyList<DiskHealth> Health() => [new("Testplatte", MediaKind.Ssd, 41, 3, 1234, 0, false)];

        public List<string> Recycled { get; } = [];

        public List<string> GameRootsSeen { get; } = [];

        public LargeFileScan ScanLargeFiles(long threshold, IReadOnlyList<string> keep, IProgress<int> progress, CancellationToken cancellation)
        {
            const long gb = 1L << 30;
            var old = DateTimeOffset.UnixEpoch;
            return new LargeFileScan(
            [
                new(@"C:\Users\a\Downloads\setup.iso", 5 * gb, old, LargeFileClass.A, "large.why.installer", [("days", 112)]),
                new(@"C:\Users\a\Downloads\alt.zip", 3 * gb, old, LargeFileClass.A, "large.why.installer", [("days", 400)]),
                new(@"C:\Users\a\Desktop\film.mkv", 2 * gb, old, LargeFileClass.B, "large.why.oldvideo", [("days", 200)]),
                new(@"D:\Kopien\kopie.mkv", 2 * gb, old, LargeFileClass.B, "large.why.duplicate", [("original", "film.mkv")], DuplicateGroup: "ABC", IsOriginal: false),
                new(@"D:\Games\Fortnite\Paks\pakchunk0.pak", 20 * gb, old, LargeFileClass.D, "large.why.game", [("game", "Fortnite")]),
                new(@"C:\Users\a\Documents\steuer.pdf", gb, old, LargeFileClass.C, "large.why.library", []),
            ],
            [new GameFolder(@"D:\Games\Fortnite", "Fortnite", 90 * gb)],
            100,
            false,
            false,
            [@"D:\Games\Fortnite"]);
        }

        public RecycleOutcome RecycleLargeFiles(IReadOnlyList<LargeFileItem> items, IReadOnlyList<string> gameRoots)
        {
            Recycled.AddRange(items.Select(i => i.Path));
            GameRootsSeen.AddRange(gameRoots);
            return new RecycleOutcome([items[0].Path], items.Skip(1).Select(i => (i.Path, "large.skip.inuse")).ToList());
        }

        public void ShowInExplorer(string path) => Shown.Add(path);
    }
}
