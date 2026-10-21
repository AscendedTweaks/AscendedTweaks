using FluentAssertions;
using TweakPilot.Engine.Cleanup;
using TweakPilot.Engine.Tools;
using Xunit;

namespace TweakPilot.Engine.Tests;

/// <summary>V2-6: grosse Dateien mit Klassen A bis D, Schutzregeln, Cloud, Abzweigungen, Duplikaten und KeepList (Abschnitt 6).</summary>
public sealed class LargeFileTests
{
    private const long Gb = 1L << 30;
    private const string User = @"C:\Users\max";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly Fs _fs = new();

    [Fact]
    public void Every_class_follows_section_6_2()
    {
        _fs.File(User + @"\Downloads\setup.exe", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\neu.iso", Gb, Now.AddDays(-3));
        _fs.File(User + @"\Downloads\film.mkv", Gb, Now.AddDays(-120));
        _fs.File(User + @"\Desktop\alt.zip", Gb, Now.AddDays(-200));
        _fs.File(User + @"\AppData\Local\CrashDumps\app.dmp", Gb, Now.AddDays(-60));
        _fs.File(User + @"\Documents\steuer.mp4", Gb, Now.AddDays(-400));
        _fs.File(User + @"\code\app\big.bin", Gb, Now.AddDays(-400));
        _fs.Dir(User + @"\code\app\.git");
        _fs.File(@"D:\VMs\win.vhdx", Gb, Now.AddDays(-400));
        _fs.File(@"D:\Mail\archiv.pst", Gb, Now.AddDays(-400));
        _fs.File(@"D:\Sonstiges\irgendwas.bin", Gb, Now.AddDays(-400));

        var items = Scan().Items.ToDictionary(i => Path.GetFileName(i.Path));

        items["setup.exe"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.A && i.ReasonKey == "large.why.installer");
        items["setup.exe"].Args.Should().Contain(("days", (object)40));
        items["neu.iso"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.C && i.ReasonKey == "large.why.recent");
        items["film.mkv"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.B && i.ReasonKey == "large.why.oldvideo");
        items["alt.zip"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.B && i.ReasonKey == "large.why.oldarchive");
        items["app.dmp"].Class.Should().Be(LargeFileClass.A);
        items["steuer.mp4"].ReasonKey.Should().Be("large.why.library");
        items["big.bin"].ReasonKey.Should().Be("large.why.project");
        items["win.vhdx"].ReasonKey.Should().Be("large.why.keeptype");
        items["archiv.pst"].ReasonKey.Should().Be("large.why.keeptype");
        items["irgendwas.bin"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.C && i.ReasonKey == "large.why.unknown", "unbekannt heisst behalten");
        items.Values.Where(i => i.Class is LargeFileClass.C or LargeFileClass.D).Should().OnlyContain(i => !i.Selectable);
    }

    [Fact]
    public void Games_and_system_areas_are_class_d_and_listed_per_game_with_their_size()
    {
        _fs.File(@"D:\Games\Fortnite\FortniteGame\Content\Paks\pakchunk0.pak", 20 * Gb, Now.AddDays(-100));
        _fs.File(@"D:\Games\Fortnite\FortniteGame\Content\Paks\pakchunk0.ucas", 5 * Gb, Now.AddDays(-100));
        _fs.File(@"D:\Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient.exe", 100, Now.AddDays(-100));
        _fs.File(@"D:\SteamLibrary\steamapps\appmanifest_730.acf", 1, Now.AddDays(-100));
        _fs.File(@"D:\SteamLibrary\steamapps\common\Counter-Strike 2\game\csgo\pak01_000.vpk", 2 * Gb, Now.AddDays(-100));
        _fs.File(@"D:\Epic\Spiel\.egstore\x.manifest", 1, Now.AddDays(-100));
        _fs.File(@"D:\Epic\Spiel\data.bin", Gb, Now.AddDays(-100));
        _fs.File(@"C:\pagefile.sys", 8 * Gb, Now.AddDays(-100));
        _fs.File(@"C:\Program Files\Tool\daten.bin", Gb, Now.AddDays(-100));
        _fs.File(User + @"\Downloads\Archiv.pak", Gb, Now.AddDays(-100));

        var scan = Scan();
        var items = scan.Items.ToDictionary(i => Path.GetFileName(i.Path));

        items["pakchunk0.pak"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.D && i.ReasonKey == "large.why.game");
        items["pakchunk0.pak"].Args.Should().Contain(("game", (object)"FortniteGame"));
        items["pak01_000.vpk"].Class.Should().Be(LargeFileClass.D);
        items["data.bin"].Class.Should().Be(LargeFileClass.D);
        items["pagefile.sys"].Should().Match<LargeFileItem>(i => i.Class == LargeFileClass.D && i.ReasonKey == "large.why.system");
        items["daten.bin"].Class.Should().Be(LargeFileClass.D);
        items["Archiv.pak"].Class.Should().NotBe(LargeFileClass.D, "ohne ausfuehrbare Datei im selben Ordner ist es keine Spielstruktur");
        scan.Games.Select(g => (g.Name, g.SizeBytes)).Should().Contain([("FortniteGame", 25 * Gb + 100), ("Counter-Strike 2", 2 * Gb), ("Spiel", Gb + 1)]);
    }

    /// <summary>Befund aus dem echten Lauf: .dat-Dateien in App-Daten machten C:\Users zum "Spiel" und damit alles zu Klasse D.</summary>
    [Fact]
    public void App_data_and_program_folders_do_not_become_games_and_user_files_stay_visible()
    {
        _fs.File(User + @"\AppData\Local\Discord\app-1\resources.dat", 10, Now.AddDays(-100));
        _fs.File(User + @"\AppData\Local\Discord\Update.exe", 10, Now.AddDays(-100));
        _fs.File(@"C:\Users\Public\Daten\x.dat", 10, Now.AddDays(-100));
        _fs.File(@"C:\Users\Public\tool.exe", 10, Now.AddDays(-100));
        _fs.File(@"C:\Program Files\Tool\daten.dat", 10, Now.AddDays(-100));
        _fs.File(@"C:\Program Files\Tool\tool.exe", 10, Now.AddDays(-100));
        _fs.File(@"C:\$Recycle.Bin\S-1-5-21\$R1.pak", Gb, Now.AddDays(-100));
        _fs.File(User + @"\AppData\Local\Programs\Opera^.0
esources.pak", 30L << 20, Now.AddDays(-100));
        _fs.File(User + @"\AppData\Local\Programs\Opera^.0\opera.exe", 10, Now.AddDays(-100));
        _fs.File(@"C:\$Recycle.Bin\S-1-5-21\$R2.exe", 10, Now.AddDays(-100));
        _fs.File(User + @"\Downloads\setup.exe", Gb, Now.AddDays(-40));

        var scan = Scan();

        scan.Items.Single(i => i.Path.EndsWith("setup.exe", StringComparison.Ordinal)).Class.Should().Be(LargeFileClass.A);
        scan.GameRoots.Should().NotContain(r => r.Equals(@"C:\Users", StringComparison.OrdinalIgnoreCase) || r.StartsWith(@"C:\$Recycle.Bin", StringComparison.OrdinalIgnoreCase));
        scan.Games.Should().BeEmpty("Programme und App-Daten sind keine Spiele");
        scan.Folders.Should().BeGreaterThan(5, "der Ordnerzaehler laeuft auch ohne Fortschrittsanzeige");
    }

    [Fact]
    public void Cloud_placeholders_and_reparse_points_are_never_read_followed_or_deleted()
    {
        _fs.File(User + @"\OneDrive\film.mkv", Gb, Now.AddDays(-400), (FileAttributes)0x400000);
        _fs.File(User + @"\Downloads\setup.exe", Gb, Now.AddDays(-400));
        _fs.File(@"D:\Ziel\riesig.iso", Gb, Now.AddDays(-400));
        _fs.Dir(User + @"\Downloads\Link", reparse: true);
        _fs.LinkTarget(User + @"\Downloads\Link", @"D:\Ziel");
        var hasher = new CountingHasher();

        var items = Scan(hasher: hasher, roots: [@"C:\"]).Items;

        items.Single(i => i.Path.EndsWith("film.mkv", StringComparison.Ordinal)).Should().Match<LargeFileItem>(i => i.IsCloud && i.ReasonKey == "large.why.cloud" && !i.Selectable);
        items.Should().NotContain(i => i.Path.Contains("riesig", StringComparison.Ordinal), "Abzweigungen werden nicht verfolgt");
        hasher.Paths.Should().NotContain(p => p.Contains("OneDrive", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicates_form_a_group_with_an_original_and_copies_become_b()
    {
        _fs.File(User + @"\Videos\urlaub.mp4", 2 * Gb, Now.AddDays(-500));
        _fs.File(@"D:\Kopie\urlaub.mp4", 2 * Gb, Now.AddDays(-300));
        _fs.File(@"D:\Kopie\anders.mp4", 2 * Gb, Now.AddDays(-300));
        var hasher = new CountingHasher { [@"D:\Kopie\anders.mp4"] = "BBBB" };

        var items = Scan(hasher: hasher).Items.ToDictionary(i => i.Path);

        var original = items[User + @"\Videos\urlaub.mp4"];
        var copy = items[@"D:\Kopie\urlaub.mp4"];
        original.Should().Match<LargeFileItem>(i => i.IsOriginal && i.Class == LargeFileClass.C);
        copy.Should().Match<LargeFileItem>(i => !i.IsOriginal && i.Class == LargeFileClass.B && i.ReasonKey == "large.why.duplicate" && i.DuplicateGroup == original.DuplicateGroup);
        items[@"D:\Kopie\anders.mp4"].DuplicateGroup.Should().BeNull("gleiche Groesse, aber anderer Inhalt");
    }

    [Fact]
    public void Keep_list_hides_files_and_whole_folders()
    {
        _fs.File(User + @"\Downloads\setup.exe", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\Behalten\tool.zip", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\noch.msi", Gb, Now.AddDays(-40));

        var items = Scan(keep: [User + @"\Downloads\setup.exe", User + @"\Downloads\Behalten"]).Items;

        items.Select(i => Path.GetFileName(i.Path)).Should().Equal("noch.msi");
    }

    [Fact]
    public void Recycler_rechecks_everything_and_never_receives_a_class_d_path()
    {
        _fs.File(User + @"\Downloads\a.exe", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\b.exe", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\c.exe", Gb, Now.AddDays(-40));
        _fs.File(User + @"\Downloads\d.exe", Gb, Now.AddDays(-40));
        _fs.File(@"D:\Games\Spiel\content.pak", Gb, Now.AddDays(-100));
        _fs.File(@"D:\Games\Spiel\spiel.exe", 1, Now.AddDays(-100));
        var scan = Scan();
        var all = scan.Items.ToList();

        // Faelschungsversuch: ein Spielpfad wird als A ausgegeben.
        var forged = all.Single(i => i.Path.EndsWith("content.pak", StringComparison.Ordinal)) with { Class = LargeFileClass.A };
        _fs.File(User + @"\Downloads\b.exe", 2 * Gb, Now.AddDays(-1));
        _fs.Remove(User + @"\Downloads\c.exe");
        var probe = new Probe { User + @"\Downloads\d.exe" };
        var recycler = new RecordingRecycler(_fs);

        var outcome = new LargeFileRecycler(_fs, recycler, probe, [@"C:\Windows", @"C:\Program Files"])
            .Recycle([.. all.Where(i => i.Class == LargeFileClass.A), forged, all.Single(i => i.Path.EndsWith("content.pak", StringComparison.Ordinal))]);

        recycler.Received.Should().Equal(User + @"\Downloads\a.exe");
        outcome.Recycled.Should().Equal(User + @"\Downloads\a.exe");
        outcome.Skipped.Should().Contain([(User + @"\Downloads\b.exe", "large.skip.changed"), (User + @"\Downloads\c.exe", "large.skip.missing"), (User + @"\Downloads\d.exe", "large.skip.inuse")]);
        outcome.Skipped.Where(s => s.Path.EndsWith("content.pak", StringComparison.Ordinal)).Should().OnlyContain(s => s.ReasonKey == "large.skip.protected").And.HaveCount(2);
        LargeFileScanner.IsProtectedPath(@"D:\SteamLibrary\steamapps\common\x\y.bin", []).Should().BeTrue();
        LargeFileScanner.IsProtectedPath(@"C:\hiberfil.sys", []).Should().BeTrue();
    }

    [Fact]
    public void Scan_stops_at_the_time_limit_and_reports_progress()
    {
        for (var i = 0; i < 50; i++)
        {
            _fs.File($@"D:\Viele\{i}\x.bin", Gb, Now.AddDays(-400));
        }

        var progress = new List<int>();
        var scan = Scanner(new CountingHasher(), []).Scan([@"D:\"], LargeFileScanner.DefaultThreshold, CancellationToken.None, new SyncProgress(progress), TimeSpan.Zero);

        scan.TimedOut.Should().BeTrue();
        Scanner(new CountingHasher(), []).Scan([@"D:\"], LargeFileScanner.DefaultThreshold, CancellationToken.None, new SyncProgress(progress)).Folders.Should().Be(52);
        progress.Should().NotBeEmpty();
        ((Action)(() => Scanner(new CountingHasher(), []).Scan([@"D:\"], LargeFileScanner.DefaultThreshold, new CancellationToken(true)))).Should().Throw<OperationCanceledException>();
    }

    private LargeFileScan Scan(CountingHasher? hasher = null, IReadOnlyList<string>? keep = null, IReadOnlyList<string>? roots = null) =>
        Scanner(hasher ?? new CountingHasher(), keep ?? []).Scan(roots ?? [@"C:\", @"D:\"], LargeFileScanner.DefaultThreshold, CancellationToken.None);

    private LargeFileScanner Scanner(CountingHasher hasher, IReadOnlyList<string> keep) => new(_fs, hasher, new LargeFileContext
    {
        UserProfile = User,
        Downloads = User + @"\Downloads",
        Desktop = User + @"\Desktop",
        Documents = User + @"\Documents",
        Pictures = User + @"\Pictures",
        Videos = User + @"\Videos",
        Music = User + @"\Music",
        Temp = User + @"\AppData\Local\Temp",
        ProtectedRoots = [@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"],
        CacheFolders = [User + @"\AppData\Local\D3DSCache"],
        Keep = keep,
        Now = Now,
    }, [@"C:\Windows"]);

    private sealed class SyncProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }

    private sealed class CountingHasher : Dictionary<string, string>, IFileHasher
    {
        public CountingHasher()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public List<string> Paths { get; } = [];

        /// <summary>Gleicher Dateiname gleicher Inhalt, ausser ein Eintrag sagt etwas anderes.</summary>
        public string? Sha256(string path, CancellationToken cancellation)
        {
            Paths.Add(path);
            return TryGetValue(path, out var hash) ? hash : "HASH-" + Path.GetFileName(path).ToUpperInvariant() + "-0000";
        }
    }

    private sealed class Probe : HashSet<string>, IFileInUseProbe
    {
        public bool IsInUse(string path) => Contains(path);
    }

    private sealed class RecordingRecycler(Fs fs) : IRecycler
    {
        public List<string> Received { get; } = [];

        public void Recycle(IReadOnlyList<string> paths)
        {
            Received.AddRange(paths);
            foreach (var path in paths)
            {
                fs.Remove(path);
            }
        }
    }

    /// <summary>Gefaelschtes Dateisystem mit Attributen und Abzweigungen; listet einen Link als Reparse Point ohne Inhalt.</summary>
    private sealed class Fs : ICleanupFileSystem
    {
        private readonly Dictionary<string, FsEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public void Dir(string path, bool reparse = false)
        {
            AddParents(path);
            _entries[path] = new FsEntry(path, true, reparse, 0, Now);
        }

        public void File(string path, long size, DateTimeOffset time, FileAttributes attributes = 0)
        {
            AddParents(path);
            _entries[path] = new FsEntry(path, false, false, size, time, attributes);
        }

        public void LinkTarget(string link, string target) => _ = (link, target);

        public void Remove(string path) => _entries.Remove(path);

        public bool DirectoryExists(string path) => path.Length == 3 || (_entries.TryGetValue(path, out var e) && e.IsDirectory);

        public FsEntry? Stat(string path) => _entries.TryGetValue(path, out var e) ? e : null;

        public IEnumerable<FsEntry> List(string directory)
        {
            if (_entries.TryGetValue(directory, out var self) && self.IsReparsePoint)
            {
                throw new InvalidOperationException("Ein Reparse Point darf nie aufgelistet werden.");
            }

            var parent = directory.TrimEnd('\\');
            return _entries.Values.Where(e => string.Equals(Path.GetDirectoryName(e.FullPath)?.TrimEnd('\\'), parent, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public DeleteOutcome DeleteFile(string path) => throw new InvalidOperationException("Grosse Dateien gehen nur in den Papierkorb.");

        public bool DeleteEmptyDirectory(string path) => throw new InvalidOperationException();

        private void AddParents(string path)
        {
            for (var p = Path.GetDirectoryName(path); p is not null && p.Length > 3; p = Path.GetDirectoryName(p))
            {
                _entries.TryAdd(p, new FsEntry(p, true, false, 0, Now));
            }
        }
    }
}
