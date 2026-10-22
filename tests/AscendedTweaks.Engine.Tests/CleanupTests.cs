using AscendedTweaks.Core;
using AscendedTweaks.Engine.Cleanup;
using AscendedTweaks.Engine.Windows;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class CleanupTests
{
    private const string Temp = @"C:\Users\max\AppData\Local\Temp";
    private const string Local = @"C:\Users\max\AppData\Local";
    private const string Windows = @"C:\Windows";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeFs _fs = new();
    private readonly FakeRecycler _recycler;
    private readonly FakeProcesses _processes = new();
    private readonly FakeServices _services = new();
    private readonly FakeSpecials _specials = new();

    public CleanupTests() => _recycler = new FakeRecycler(_fs);

    [Theory]
    [InlineData(@"C:\Windows\Temp", @"C:\Windows\Temp")]
    [InlineData(@"C:\Windows\Temp\", @"C:\Windows\Temp")]
    [InlineData(@"C:\Windows\..\Users", null)]
    [InlineData(@"\\server\share\x", null)]
    [InlineData(@"\\?\C:\Windows\Temp", null)]
    [InlineData(@"\\.\PhysicalDrive0", null)]
    [InlineData(@"C:\Windows\Temp\datei.txt:versteckt", null)]
    [InlineData(@"relativ\pfad", null)]
    [InlineData(@"C:\Windows\Te*p", null)]
    [InlineData(@"C:/Windows/Temp", null)]
    public void Paths_are_canonical_or_rejected(string path, string? expected)
    {
        PathGuard.Canonical(path).Should().Be(expected);
    }

    [Fact]
    public void Protected_folders_are_never_roots()
    {
        var protectedFolders = PathGuard.ProtectedFolders(Windows, @"C:\Users\max");

        PathGuard.IsProtected(@"C:\Windows\Prefetch\x.pf", protectedFolders).Should().BeTrue();
        PathGuard.IsProtected(@"C:\Windows\WinSxS", protectedFolders).Should().BeTrue();
        PathGuard.IsProtected(@"C:\Windows\Installer\abc.msp", protectedFolders).Should().BeTrue();
        PathGuard.IsProtected(@"C:\Users\max\Downloads\setup.exe", protectedFolders).Should().BeTrue();
        PathGuard.IsProtected(@"C:\Users\max", protectedFolders).Should().BeTrue("ein Ordner, der geschuetzte Ordner enthaelt, ist ebenfalls tabu");
        PathGuard.IsProtected(@"C:\Windows\Temp\x.tmp", protectedFolders).Should().BeFalse();
    }

    [Theory]
    [InlineData("thumbcache_1024.db", "thumbcache_*.db", true)]
    [InlineData("iconcache_16.db", "thumbcache_*.db", false)]
    [InlineData("Mini101826-01.dmp", "*.dmp", true)]
    [InlineData("report.wer", "*.dmp", false)]
    [InlineData("MEMORY.DMP", "MEMORY.DMP", true)]
    [InlineData("MEMORY.DMP.bak", "MEMORY.DMP", false)]
    public void File_patterns_match(string name, string pattern, bool expected)
    {
        CleanupEngine.Matches(name, pattern).Should().Be(expected);
    }

    [Fact]
    public void Scan_counts_old_files_and_never_follows_junctions()
    {
        _fs.Dir(Temp);
        _fs.File($@"{Temp}\alt.tmp", 100, Now.AddDays(-3));
        _fs.File($@"{Temp}\neu.tmp", 50, Now.AddHours(-2));
        _fs.Dir($@"{Temp}\sub");
        _fs.File($@"{Temp}\sub\alt2.log", 200, Now.AddDays(-10));
        _fs.Dir($@"{Temp}\falle", reparse: true);
        _fs.File($@"{Temp}\falle\dokument.docx", 999, Now.AddDays(-100));
        var writes = _fs.Deletes.Count;

        var scan = User().Scan("CLEAN-001");

        scan.Files.Should().Be(2);
        scan.Bytes.Should().Be(300);
        scan.SkippedReparsePoints.Should().Be(1);
        _fs.Deletes.Count.Should().Be(writes, "ein Scan aendert nichts");
    }

    [Fact]
    public void Permanent_run_deletes_counts_in_use_and_keeps_root_and_junction_targets()
    {
        _fs.Dir(Temp);
        _fs.File($@"{Temp}\alt.tmp", 100, Now.AddDays(-3));
        _fs.File($@"{Temp}\offen.tmp", 10, Now.AddDays(-3), inUse: true);
        _fs.Dir($@"{Temp}\leer");
        _fs.File($@"{Temp}\leer\x.tmp", 5, Now.AddDays(-3));
        _fs.Dir($@"{Temp}\falle", reparse: true);
        _fs.File($@"{Temp}\falle\dokument.docx", 999, Now.AddDays(-100));

        var result = User().Run("CLEAN-001", CleanupMode.Permanent);

        result.DeletedFiles.Should().Be(2);
        result.DeletedBytes.Should().Be(105);
        result.SkippedInUse.Should().Be(1);
        _fs.Exists($@"{Temp}\leer").Should().BeFalse("leere Unterordner werden entfernt");
        _fs.Exists(Temp).Should().BeTrue("die Wurzel bleibt");
        _fs.Exists($@"{Temp}\falle\dokument.docx").Should().BeTrue("Abzweigungen werden nie verfolgt");
        _fs.Deletes.Should().NotContain(d => d.Contains("falle", StringComparison.Ordinal));
    }

    [Fact]
    public void File_swapped_for_a_link_after_the_scan_is_not_deleted()
    {
        _fs.Dir(Temp);
        _fs.File($@"{Temp}\alt.tmp", 100, Now.AddDays(-3));
        _fs.OnDelete = path => _fs.MakeReparse(path);

        var result = User().Run("CLEAN-001", CleanupMode.Permanent);

        result.DeletedFiles.Should().Be(0);
        result.SkippedReparsePoints.Should().Be(1);
        _fs.Exists($@"{Temp}\alt.tmp").Should().BeTrue();
    }

    [Fact]
    public void Recycle_mode_uses_the_recycle_bin()
    {
        _fs.Dir(Temp);
        _fs.File($@"{Temp}\alt.tmp", 100, Now.AddDays(-3));
        _fs.File($@"{Temp}\offen.tmp", 10, Now.AddDays(-3), inUse: true);

        var result = User().Run("CLEAN-001", CleanupMode.RecycleBin);

        _recycler.Recycled.Should().BeEquivalentTo($@"{Temp}\alt.tmp", $@"{Temp}\offen.tmp");
        result.DeletedFiles.Should().Be(1);
        result.SkippedInUse.Should().Be(1);
        _fs.Deletes.Should().BeEmpty("im Papierkorb-Modus wird nichts endgueltig geloescht");
    }

    [Fact]
    public void Machine_folders_only_allow_permanent_and_only_in_the_helper()
    {
        ((Action)(() => Machine().Run("CLEAN-002", CleanupMode.RecycleBin))).Should().Throw<AscendedTweaksException>().WithMessage("*Recycle Bin*");
        ((Action)(() => User().Scan("CLEAN-002"))).Should().Throw<AscendedTweaksException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        ((Action)(() => Machine().Scan("CLEAN-001"))).Should().Throw<AscendedTweaksException>().Which.Kind.Should().Be(ErrorKind.PermissionDenied);
        ((Action)(() => User().Scan("CLEAN-999"))).Should().Throw<AscendedTweaksException>();
    }

    [Fact]
    public void Open_browser_blocks_scan_and_run()
    {
        _fs.Dir($@"{Local}\Google\Chrome\User Data\Default\Cache");
        _fs.File($@"{Local}\Google\Chrome\User Data\Default\Cache\data_1", 1000, Now);
        _processes.Running.Add("chrome");

        User().Scan("CLEAN-010").Blocked.Should().Be("browser-open:chrome");
        User().Run("CLEAN-010", CleanupMode.Permanent).Blocked.Should().Be("browser-open:chrome");
        _fs.Exists($@"{Local}\Google\Chrome\User Data\Default\Cache\data_1").Should().BeTrue();
    }

    [Fact]
    public void Browser_cache_covers_all_profiles_but_never_cookies()
    {
        foreach (var profile in new[] { "Default", "Profile 1" })
        {
            _fs.Dir($@"{Local}\Google\Chrome\User Data\{profile}\Cache");
            _fs.File($@"{Local}\Google\Chrome\User Data\{profile}\Cache\data_1", 10, Now);
            _fs.File($@"{Local}\Google\Chrome\User Data\{profile}\Cookies", 10, Now);
            _fs.File($@"{Local}\Google\Chrome\User Data\{profile}\Login Data", 10, Now);
        }

        User().Run("CLEAN-010", CleanupMode.Permanent).DeletedFiles.Should().Be(2);

        _fs.Exists($@"{Local}\Google\Chrome\User Data\Default\Cookies").Should().BeTrue();
        _fs.Exists($@"{Local}\Google\Chrome\User Data\Profile 1\Login Data").Should().BeTrue();
    }

    [Fact]
    public void Crash_dumps_include_single_memory_dump_file()
    {
        _fs.Dir(Windows);
        _fs.File($@"{Windows}\MEMORY.DMP", 5000, Now);
        _fs.File($@"{Windows}\explorer.exe", 5000, Now);
        _fs.Dir($@"{Windows}\Minidump");
        _fs.File($@"{Windows}\Minidump\a.dmp", 100, Now);

        var result = Machine().Run("CLEAN-009", CleanupMode.Permanent);

        result.DeletedFiles.Should().Be(2);
        _fs.Exists($@"{Windows}\explorer.exe").Should().BeTrue();
    }

    [Fact]
    public void Windows_update_services_are_restored_exactly()
    {
        _services.Services["wuauserv"] = new ServiceConfig(ServiceStartType.Manual, false, true);
        _services.Services["bits"] = new ServiceConfig(ServiceStartType.Automatic, true, false);
        _fs.Dir($@"{Windows}\SoftwareDistribution\Download");
        _fs.File($@"{Windows}\SoftwareDistribution\Download\paket.cab", 1000, Now);

        var result = Machine().Run("CLEAN-003", CleanupMode.Permanent);

        result.DeletedFiles.Should().Be(1);
        _services.Services["wuauserv"].Should().Be(new ServiceConfig(ServiceStartType.Manual, false, true), "laufender Dienst laeuft danach wieder");
        _services.Services["bits"].Should().Be(new ServiceConfig(ServiceStartType.Automatic, true, false), "gestoppter Dienst bleibt gestoppt");
    }

    [Fact]
    public void Special_handlers_are_routed()
    {
        _specials.Scan = new CleanupScan { CategoryId = string.Empty, Files = 3, Bytes = 300 };

        User().Scan("CLEAN-006").Should().Match<CleanupScan>(s => s.CategoryId == "CLEAN-006" && s.Files == 3);
        User().Run("CLEAN-006", CleanupMode.RecycleBin).Mode.Should().Be(CleanupMode.Permanent);
        Machine().Scan("CLEAN-014").SizeUnknown.Should().BeTrue();
        Machine().Run("CLEAN-014", CleanupMode.Permanent);
        Machine().Run("CLEAN-005", CleanupMode.Permanent);
        _specials.Calls.Should().Equal("empty", "dism", "previous");
    }

    [Fact]
    public void Previous_installations_runner_restores_all_state_flags()
    {
        var registry = new FakeRegistry();
        const string vc = PreviousInstallationsRunner.VolumeCaches;
        registry.SeedKey(RegHive.HKLM, $@"{vc}\Previous Installations");
        registry.Seed(RegHive.HKLM, $@"{vc}\Temporary Files", PreviousInstallationsRunner.FlagName, RegistryData.DWord(2));
        registry.SeedKey(RegHive.HKLM, $@"{vc}\Thumbnail Cache");
        var before = registry.Snapshot();
        var commands = new RecordingCommands(registry);

        var result = new PreviousInstallationsRunner(registry, commands, @"C:\Windows\System32").Run();

        result.Failed.Should().Be(0);
        commands.FlagsDuringRun.Should().Equal(["Previous Installations"], "waehrend des Laufs ist nur dieser Handler markiert");
        commands.Args.Should().Equal("/sagerun:42");
        registry.Snapshot().Should().Be(before);
    }

    [Fact]
    public void Launcher_caches_take_only_cache_folders_never_cookies_settings_or_other_saved_folders()
    {
        const string epic = Local + @"\EpicGamesLauncher\Saved";
        _fs.File(epic + @"\webcache_4430\Cache\Cache_Data\f_000001", 100, Now.AddDays(-3));
        _fs.File(epic + @"\webcache_4430\Cookies", 10, Now.AddDays(-3));
        _fs.File(epic + @"\webcache_4430\Local Storage\leveldb\000003.log", 10, Now.AddDays(-3));
        _fs.File(epic + @"\Config\Cache\Settings.ini", 10, Now.AddDays(-3));
        _fs.File(@"C:\Users\max\AppData\Roaming\discord\Cache\Cache_Data\data_0", 200, Now.AddDays(-3));
        _fs.File(@"C:\Users\max\AppData\Roaming\discord\Local Storage\leveldb\000005.ldb", 20, Now.AddDays(-3));

        var epicScan = User().Scan("CLEAN-017");
        epicScan.Files.Should().Be(1, "nur webcache_*\\Cache, nicht Config\\Cache, Cookies oder Local Storage");
        epicScan.Bytes.Should().Be(100);
        User().Scan("CLEAN-015").Bytes.Should().Be(200, "Discord: nur Cache-Ordner unter %APPDATA%");

        foreach (var category in CleanupCategories.All.Where(c => string.CompareOrdinal(c.Id, "CLEAN-015") >= 0))
        {
            category.Scope.Should().Be(TweakScope.User);
            category.Roots.Should().NotContain(r => r.RelativePath.Contains("Local Storage", StringComparison.OrdinalIgnoreCase) || r.RelativePath.Contains("Cookies", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Running_launcher_blocks_its_cleanup()
    {
        _fs.File(@"C:\Users\max\AppData\Roaming\discord\Cache\Cache_Data\data_0", 200, Now.AddDays(-3));
        _processes.Running.Add("discord");

        User().Scan("CLEAN-015").Blocked.Should().NotBeNull();
    }

    private CleanupEngine User() => new(_fs, _recycler, _processes, _specials, _services,
        new Dictionary<CleanupBase, string> { [CleanupBase.UserTemp] = Temp, [CleanupBase.LocalAppData] = Local, [CleanupBase.LocalAppDataLow] = @"C:\Users\max\AppData\LocalLow", [CleanupBase.RoamingAppData] = @"C:\Users\max\AppData\Roaming" },
        PathGuard.ProtectedFolders(Windows, @"C:\Users\max"), TweakScope.User, new FixedTime());

    private CleanupEngine Machine() => new(_fs, _recycler, _processes, _specials, _services,
        new Dictionary<CleanupBase, string> { [CleanupBase.Windows] = Windows, [CleanupBase.ProgramData] = @"C:\ProgramData", [CleanupBase.SystemDrive] = @"C:\" },
        PathGuard.ProtectedFolders(Windows, null), TweakScope.Machine, new FixedTime());

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeFs : ICleanupFileSystem
    {
        private readonly Dictionary<string, (bool Dir, bool Reparse, long Size, DateTimeOffset Time, bool InUse)> _entries = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Deletes { get; } = [];

        public Action<string>? OnDelete { get; set; }

        public void Dir(string path, bool reparse = false)
        {
            foreach (var parent in Parents(path))
            {
                _entries.TryAdd(parent, (true, false, 0, Now, false));
            }

            _entries[path] = (true, reparse, 0, Now, false);
        }

        public void File(string path, long size, DateTimeOffset time, bool inUse = false)
        {
            foreach (var parent in Parents(path))
            {
                _entries.TryAdd(parent, (true, false, 0, Now, false));
            }

            _entries[path] = (false, false, size, time, inUse);
        }

        public void MakeReparse(string path) => _entries[path] = _entries[path] with { Reparse = true };

        public bool Exists(string path) => _entries.ContainsKey(path);

        public bool DirectoryExists(string path) => _entries.TryGetValue(path, out var e) && e.Dir;

        public FsEntry? Stat(string path) => _entries.TryGetValue(path, out var e) ? new FsEntry(path, e.Dir, e.Reparse, e.Size, e.Time) : null;

        public IEnumerable<FsEntry> List(string directory) =>
            _entries.Where(kv => string.Equals(Path.GetDirectoryName(kv.Key), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !string.Equals(kv.Key, directory, StringComparison.OrdinalIgnoreCase))
                .Select(kv => new FsEntry(kv.Key, kv.Value.Dir, kv.Value.Reparse, kv.Value.Size, kv.Value.Time))
                .ToList();

        public DeleteOutcome DeleteFile(string path)
        {
            OnDelete?.Invoke(path);
            if (!_entries.TryGetValue(path, out var e))
            {
                return DeleteOutcome.Missing;
            }

            if (e.Reparse)
            {
                return DeleteOutcome.ReparsePoint;
            }

            if (e.InUse)
            {
                return DeleteOutcome.InUse;
            }

            Deletes.Add(path);
            _entries.Remove(path);
            return DeleteOutcome.Deleted;
        }

        public bool DeleteEmptyDirectory(string path)
        {
            if (!_entries.TryGetValue(path, out var e) || e.Reparse || List(path).Any())
            {
                return false;
            }

            _entries.Remove(path);
            return true;
        }

        public void RemoveForRecycle(string path)
        {
            if (_entries.TryGetValue(path, out var e) && !e.InUse)
            {
                _entries.Remove(path);
            }
        }

        private static IEnumerable<string> Parents(string path)
        {
            for (var p = Path.GetDirectoryName(path); p is not null && p.Length > 3; p = Path.GetDirectoryName(p))
            {
                yield return p;
            }
        }
    }

    private sealed class FakeRecycler(FakeFs fs) : IRecycler
    {
        public List<string> Recycled { get; } = [];

        public void Recycle(IReadOnlyList<string> paths)
        {
            Recycled.AddRange(paths);
            foreach (var path in paths)
            {
                fs.RemoveForRecycle(path);
            }
        }
    }

    private sealed class FakeProcesses : IProcessProbe
    {
        public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsRunning(string processName) => Running.Contains(processName);
    }

    private sealed class FakeSpecials : ICleanupSpecials
    {
        public CleanupScan Scan { get; set; } = new() { CategoryId = string.Empty };

        public List<string> Calls { get; } = [];

        public CleanupScan ScanRecycleBin() => Scan;

        public CleanupResult EmptyRecycleBin() => Call("empty");

        public CleanupResult RunPreviousInstallations() => Call("previous");

        public CleanupResult RunComponentStoreCleanup() => Call("dism");

        private CleanupResult Call(string name)
        {
            Calls.Add(name);
            return new CleanupResult { CategoryId = string.Empty, Mode = CleanupMode.Permanent };
        }
    }

    private sealed class RecordingCommands(FakeRegistry registry) : ICommandRunner
    {
        public List<string> FlagsDuringRun { get; } = [];

        public IReadOnlyList<string> Args { get; private set; } = [];

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Args = arguments;
            foreach (var handler in registry.GetSubKeyNames(RegHive.HKLM, PreviousInstallationsRunner.VolumeCaches))
            {
                if (registry.GetValue(RegHive.HKLM, $@"{PreviousInstallationsRunner.VolumeCaches}\{handler}", PreviousInstallationsRunner.FlagName) is not null)
                {
                    FlagsDuringRun.Add(handler);
                }
            }

            return new CommandResult(0, string.Empty, string.Empty);
        }
    }
}
