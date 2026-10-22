using AscendedTweaks.Core;
using AscendedTweaks.Engine.Debloat;
using AscendedTweaks.Engine.Insights;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class DebloatTests
{
    private readonly FakeAppx _appx = new();

    [Theory]
    [InlineData("Microsoft.WindowsStore", true)]
    [InlineData("Microsoft.SecHealthUI", true)]
    [InlineData("Microsoft.UI.Xaml.2.8", true)]
    [InlineData("Microsoft.VCLibs.140.00", true)]
    [InlineData("Microsoft.NET.Native.Framework.2.2", true)]
    [InlineData("Microsoft.MicrosoftEdge.Stable", true)]
    [InlineData("windows.immersivecontrolpanel", true)]
    [InlineData("Microsoft.BingNews", false)]
    [InlineData("Clipchamp.Clipchamp", false)]
    public void System_apps_are_locked(string name, bool locked)
    {
        DebloatCatalog.IsLocked(name).Should().Be(locked);
    }

    [Fact]
    public void Inventory_shows_catalog_apps_other_vendors_and_locked_system_apps()
    {
        var installed = new[]
        {
            Package("Microsoft.BingNews", microsoft: true),
            Package("MSTeams", microsoft: true),
            Package("Hersteller.Tool", microsoft: false),
            Package("Microsoft.WindowsStore", microsoft: true),
            Package("Microsoft.VCLibs.140.00", microsoft: true, framework: true),
            Package("Microsoft.Windows.Photos", microsoft: true),
            Package("Hersteller.Systemteil", microsoft: false, system: true),
        };

        var rows = AppInventory.Build(installed, 26200);

        rows.Single(r => r.Catalog?.Id == "DEB-001").Installed.Should().NotBeNull();
        rows.Single(r => r.Catalog?.Id == "DEB-017").PackageName.Should().Be("MSTeams", "die gefundene Variante zaehlt");
        rows.Single(r => r.Catalog?.Id == "DEB-004").Installed.Should().BeNull();
        rows.Should().Contain(r => r.PackageName == "Hersteller.Tool" && r.Catalog == null && !r.Locked);
        rows.Should().Contain(r => r.PackageName == "Microsoft.WindowsStore" && r.Locked);
        rows.Should().NotContain(r => r.PackageName.StartsWith("Microsoft.VCLibs", StringComparison.Ordinal), "Frameworks erscheinen nicht");
        rows.Should().NotContain(r => r.PackageName == "Microsoft.Windows.Photos", "Microsoft-Apps ausserhalb des Katalogs werden nicht angeboten");
        rows.Should().NotContain(r => r.PackageName == "Hersteller.Systemteil");
        AppInventory.Build(installed, 22631).Should().NotContain(r => r.Catalog != null && r.Catalog.Id == "DEB-021", "Copilot-App erst ab 24H2");
    }

    [Fact]
    public void Remover_removes_for_all_users_and_deprovisions_only_on_request()
    {
        _appx.All.Add(Package("Microsoft.BingNews", microsoft: true));
        _appx.Provisioned.Add("Microsoft.BingNews_8wekyb3d8bbwe");
        var remover = new AppRemover(_appx, new FakeTime());

        var first = remover.Remove(["Microsoft.BingNews"], deprovision: false);

        first.Single().Should().Match<RemovedApp>(a => a.Ok && a.WasProvisioned && !a.Deprovisioned && a.InstallLocation == @"C:\Program Files\WindowsApps\Microsoft.BingNews");
        _appx.Removed.Should().Equal("Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe");
        _appx.Deprovisioned.Should().BeEmpty("Standard: Bereitstellungspaket bleibt");

        _appx.All.Add(Package("Microsoft.BingNews", microsoft: true));
        remover.Remove(["Microsoft.BingNews"], deprovision: true).Single().Deprovisioned.Should().BeTrue();
        _appx.Deprovisioned.Should().Equal("Microsoft.BingNews_8wekyb3d8bbwe");
    }

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.Windows.Photos")]
    [InlineData("..\\evil")]
    [InlineData("a")]
    public void Remover_refuses_locked_foreign_microsoft_and_invalid_names(string name)
    {
        _appx.All.Add(Package(name, microsoft: true));

        ((Action)(() => new AppRemover(_appx, new FakeTime()).Remove([name], false))).Should().Throw<AscendedTweaksException>();
        _appx.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Remover_allows_other_vendor_apps_and_reports_failures()
    {
        _appx.All.Add(Package("Hersteller.Tool", microsoft: false));
        _appx.FailRemove = true;

        var result = new AppRemover(_appx, new FakeTime()).Remove(["Hersteller.Tool", "Nicht.Installiert"], false);

        result.Should().ContainSingle().Which.Should().Match<RemovedApp>(a => !a.Ok && a.Error != null);
    }

    [Fact]
    public void Restorer_registers_then_falls_back_to_manifest_then_store()
    {
        var opened = new List<string>();
        var app = new RemovedApp { PackageName = "Microsoft.BingNews", FamilyName = "Microsoft.BingNews_8wekyb3d8bbwe", FullName = "x", InstallLocation = @"C:\Apps\News" };

        new AppRestorer(_appx, opened.Add, _ => true).Restore(app).Should().Be(RestoreOutcome.Registered);

        _appx.RegisterFamilyFails = true;
        new AppRestorer(_appx, opened.Add, p => p == @"C:\Apps\News\AppxManifest.xml").Restore(app).Should().Be(RestoreOutcome.Registered);
        _appx.Manifests.Should().Equal(@"C:\Apps\News\AppxManifest.xml");

        new AppRestorer(_appx, opened.Add, _ => false).Restore(app).Should().Be(RestoreOutcome.StoreOpened);
        opened.Should().Equal("ms-windows-store://pdp/?PFN=Microsoft.BingNews_8wekyb3d8bbwe");

        // Auch die Store-Seite laesst sich nicht oeffnen: ehrlich "fehlgeschlagen".
        new AppRestorer(_appx, _ => throw new System.ComponentModel.Win32Exception(2), _ => false).Restore(app).Should().Be(RestoreOutcome.Failed);
    }

    [Fact]
    public void Removed_apps_store_round_trips_and_survives_corruption()
    {
        var file = Path.Combine(Path.GetTempPath(), "AscendedTweaks.Tests", Guid.NewGuid().ToString("N"), "removed-apps.json");
        var store = new RemovedAppsStore(file);
        store.Load().Should().BeEmpty();

        store.Save([new RemovedApp { PackageName = "A.B", FamilyName = "A.B_x", FullName = "A.B_1_x" }]);
        store.Load().Single().FamilyName.Should().Be("A.B_x");

        File.WriteAllText(file, "{ kaputt");
        store.Load().Should().BeEmpty();
        Directory.Delete(Path.GetDirectoryName(file)!, true);
    }

    [Fact]
    public void OneDrive_is_blocked_by_known_folder_move_online_only_files_and_untrusted_setup()
    {
        var env = new FakeOneDrive();
        new OneDriveCheck(env).Check().CanUninstall.Should().BeTrue();

        env.Known["Personal"] = @"C:\Users\max\OneDrive\Dokumente";
        var kfm = new OneDriveCheck(env).Check();
        kfm.Blocks.Should().Contain(OneDriveBlock.KnownFolderMove);
        kfm.RedirectedFolders.Should().Equal("Personal");
        env.Known["Personal"] = @"C:\Users\max\Documents";

        env.Attributes.Add(FileAttributes.Archive | (FileAttributes)0x400000);
        new OneDriveCheck(env).Check().Should().Match<OneDriveStatus>(s => s.Blocks.Contains(OneDriveBlock.OnlineOnlyFiles) && s.OnlineOnlyFiles == 1);
        env.Attributes.Clear();

        env.Signature = new SignatureInfo(SignatureStatus.Valid, "Fremdfirma");
        new OneDriveCheck(env).Check().Blocks.Should().Contain(OneDriveBlock.SetupNotTrusted);
        env.Signature = new SignatureInfo(SignatureStatus.Valid, "Microsoft Corporation");

        env.MachineWide = true;
        new OneDriveCheck(env).Check().Blocks.Should().Contain(OneDriveBlock.MachineWide);
        env.MachineWide = false;

        env.Setup = null;
        new OneDriveCheck(env).Check().Blocks.Should().Equal(OneDriveBlock.NotInstalled);
    }

    [Fact]
    public void OneDrive_uninstall_rechecks_and_refuses_when_blocked()
    {
        var env = new FakeOneDrive();
        env.Known["Desktop"] = @"C:\Users\max\OneDrive\Desktop";
        var runner = new CountingRunner();

        ((Action)(() => new OneDriveUninstaller(new OneDriveCheck(env), runner).Uninstall())).Should().Throw<AscendedTweaksException>().WithMessage("*KnownFolderMove*");
        runner.Calls.Should().Be(0, "bei Datenverlust-Gefahr wird nichts gestartet");

        env.Known["Desktop"] = @"C:\Users\max\Desktop";
        runner.OnRun = () => env.Setup = null;
        new OneDriveUninstaller(new OneDriveCheck(env), runner).Uninstall().Installed.Should().BeFalse();
        runner.Arguments.Should().Equal("/uninstall");
    }

    [Fact]
    public void OneDrive_scan_limit_blocks_instead_of_guessing()
    {
        var env = new FakeOneDrive();
        env.Attributes.AddRange(Enumerable.Repeat(FileAttributes.Archive, OneDriveCheck.MaxFiles + 1));

        new OneDriveCheck(env).Check().Blocks.Should().Contain(OneDriveBlock.ScanIncomplete);
    }

    private static AppxPackageInfo Package(string name, bool microsoft, bool framework = false, bool system = false) =>
        new(name, $"{name}_1.0.0.0_x64__8wekyb3d8bbwe", $"{name}_8wekyb3d8bbwe", microsoft ? "Microsoft Corporation" : "Hersteller", microsoft, framework, false, system, $@"C:\Program Files\WindowsApps\{name}");

    private sealed class FakeAppx : IAppxAccess
    {
        public List<AppxPackageInfo> All { get; } = [];

        public HashSet<string> Provisioned { get; } = [];

        public List<string> Removed { get; } = [];

        public List<string> Deprovisioned { get; } = [];

        public List<string> Manifests { get; } = [];

        public bool FailRemove { get; set; }

        public bool RegisterFamilyFails { get; set; }

        public IReadOnlyList<AppxPackageInfo> ListForCurrentUser() => All;

        public IReadOnlyList<AppxPackageInfo> FindForAllUsers(string name) => All.Where(p => p.Name == name).ToList();

        public bool IsProvisioned(string familyName) => Provisioned.Contains(familyName);

        public void RemoveForAllUsers(string fullName)
        {
            if (FailRemove)
            {
                throw new InvalidOperationException("0x80073CFA (Test)");
            }

            Removed.Add(fullName);
            All.RemoveAll(p => p.FullName == fullName);
        }

        public void Deprovision(string familyName) => Deprovisioned.Add(familyName);

        public AppxRestoreOutcome RegisterByFamilyName(string familyName) =>
            RegisterFamilyFails ? throw new InvalidOperationException("nicht bereitgestellt") : AppxRestoreOutcome.Registered;

        public AppxRestoreOutcome RegisterManifest(string manifestPath)
        {
            Manifests.Add(manifestPath);
            return AppxRestoreOutcome.Registered;
        }
    }

    private sealed class FakeOneDrive : IOneDriveEnvironment
    {
        public Dictionary<string, string> Known { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Desktop"] = @"C:\Users\max\Desktop",
            ["Personal"] = @"C:\Users\max\Documents",
        };

        public List<FileAttributes> Attributes { get; } = [];

        public string? Setup { get; set; } = @"C:\Users\max\AppData\Local\Microsoft\OneDrive\25.1\OneDriveSetup.exe";

        public bool MachineWide { get; set; }

        public SignatureInfo Signature { get; set; } = new(SignatureStatus.Valid, "Microsoft Corporation");

        public IReadOnlyList<string> SyncRoots() => [@"C:\Users\max\OneDrive"];

        public IReadOnlyDictionary<string, string> KnownFolders() => Known;

        public string? PerUserSetup() => Setup;

        public bool MachineWideInstalled() => MachineWide;

        public IEnumerable<FileAttributes> FileAttributesUnder(string root) => Attributes;

        public SignatureInfo Verify(string file) => Signature;
    }

    private sealed class CountingRunner : ICommandRunner
    {
        public int Calls { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Action? OnRun { get; set; }

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls++;
            Arguments = arguments;
            OnRun?.Invoke();
            return new CommandResult(0, string.Empty, string.Empty);
        }
    }
}
