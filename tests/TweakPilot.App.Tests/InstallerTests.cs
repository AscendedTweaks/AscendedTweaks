using System.IO;
using FluentAssertions;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Engine.Tests;
using TweakPilot.Ipc;
using Xunit;

namespace TweakPilot.App.Tests;

/// <summary>V2 Abschnitt 7.3 und 7.4: Installation pro Benutzer per Test-Hook in einen Temp-Ordner, Deinstallation loescht nichts ausserhalb.</summary>
public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TweakPilot.InstallTest." + Guid.NewGuid().ToString("N"));
    private readonly FakeRegistry _registry = new();
    private readonly Links _links = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Install_copies_only_the_package_writes_links_and_an_apps_entry_without_autostart()
    {
        var package = Package();
        File.WriteAllText(Path.Combine(package, "fremd.txt"), "gehoert nicht zum Paket");
        var installer = Installer();

        AppInstaller.IsPackage(package).Should().BeTrue();
        installer.Install(package, "1.1.0-rc.1");

        var program = installer.Paths.ProgramDirectory;
        Directory.EnumerateFiles(program, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(program, f)).Should().BeEquivalentTo(
            [".tweakpilot-install", "TweakPilot.exe", @"app\TweakPilot.Helper.exe", @"app\tweakpilot-cli.exe", @"Docs\Sources.md", "START HERE.txt"]);
        _links.Created.Should().BeEquivalentTo([installer.Paths.StartMenuLink, installer.Paths.DesktopLink]);
        _registry.GetValue(RegHive.HKCU, AppInstaller.UninstallKey, "UninstallString")!.Text.Should().Be($"\"{installer.Paths.InstalledExe}\" --uninstall");
        _registry.GetValue(RegHive.HKCU, AppInstaller.UninstallKey, "DisplayVersion")!.Text.Should().Be("1.1.0-rc.1");
        _registry.GetValue(RegHive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Run", "TweakPilot").Should().BeNull("kein Autostart");
        installer.IsInstalledCopy(program).Should().BeTrue();
        installer.IsInstalledCopy(package).Should().BeFalse();
    }

    [Fact]
    public void Install_never_overwrites_a_foreign_folder_and_rejects_non_packages()
    {
        var installer = Installer();
        Directory.CreateDirectory(installer.Paths.ProgramDirectory);
        File.WriteAllText(Path.Combine(installer.Paths.ProgramDirectory, "fremd.txt"), "x");

        ((Action)(() => installer.Install(Package(), "1"))).Should().Throw<TweakPilotException>();
        File.Exists(Path.Combine(installer.Paths.ProgramDirectory, "fremd.txt")).Should().BeTrue();
        ((Action)(() => Installer().Install(Path.Combine(_root, "leer"), "1"))).Should().Throw<TweakPilotException>();
    }

    [Fact]
    public void Uninstall_removes_only_its_own_folder_links_and_entry_and_keeps_data_by_default()
    {
        var installer = Installer();
        installer.Install(Package(), "1");
        var data = Path.Combine(_root, "Daten");
        Directory.CreateDirectory(data);
        var outside = Path.Combine(_root, "Programs", "Anderes", "bleibt.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "x");

        installer.Uninstall(Path.Combine(_root, "Programs"), removeData: true, [data]).Removed.Should().BeFalse("nur der eigene Installationsordner");
        var result = installer.Uninstall(installer.Paths.ProgramDirectory, removeData: false, [data]);

        result.Removed.Should().BeTrue();
        Directory.Exists(installer.Paths.ProgramDirectory).Should().BeFalse();
        _links.Deleted(installer.Paths).Should().BeTrue();
        _registry.KeyExists(RegHive.HKCU, AppInstaller.UninstallKey).Should().BeFalse();
        Directory.Exists(data).Should().BeTrue("Sicherungen und Einstellungen bleiben standardmaessig");
        File.Exists(outside).Should().BeTrue();
    }

    [Fact]
    public void Uninstall_refuses_a_folder_without_the_install_marker()
    {
        var installer = Installer();
        Directory.CreateDirectory(installer.Paths.ProgramDirectory);
        File.WriteAllText(Path.Combine(installer.Paths.ProgramDirectory, "TweakPilot.exe"), "x");

        installer.Uninstall(installer.Paths.ProgramDirectory, removeData: true, []).Removed.Should().BeFalse();
        File.Exists(Path.Combine(installer.Paths.ProgramDirectory, "TweakPilot.exe")).Should().BeTrue();
    }

    [Fact]
    public void Helper_is_found_in_the_app_folder_and_a_changed_helper_is_refused()
    {
        var package = Package();
        ElevatedHelperLauncher.DefaultHelperPath(package).Should().Be(Path.Combine(package, "app", "TweakPilot.Helper.exe"));
        ElevatedHelperLauncher.DefaultHelperPath(Path.Combine(_root, "leer")).Should().EndWith(@"leer\TweakPilot.Helper.exe");

        var helper = Path.Combine(package, "app", "TweakPilot.Helper.exe");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(helper)));
        ElevatedHelperLauncher.VerifyHash(helper, hash);
        ElevatedHelperLauncher.VerifyHash(helper, null);
        File.AppendAllText(helper, "veraendert");
        ((Action)(() => ElevatedHelperLauncher.VerifyHash(helper, hash))).Should().Throw<TweakPilotException>().WithMessage("*was modified*");
    }

    private AppInstaller Installer() => new(
        new InstallPaths(Path.Combine(_root, "Programs", "TweakPilot"), Path.Combine(_root, "StartMenu"), Path.Combine(_root, "Desktop")),
        _registry,
        _links);

    private string Package()
    {
        var package = Path.Combine(_root, "Paket");
        foreach (var file in new[] { "TweakPilot.exe", @"app\TweakPilot.Helper.exe", @"app\tweakpilot-cli.exe", @"Docs\Sources.md", "START HERE.txt", ".tweakpilot-package" })
        {
            var path = Path.Combine(package, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }

        return package;
    }

    private sealed class Links : IShortcutWriter
    {
        public List<string> Created { get; } = [];

        public void Create(string linkPath, string targetPath, string workingDirectory, string description)
        {
            Created.Add(linkPath);
            Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
            File.WriteAllText(linkPath, targetPath);
        }

        public bool Deleted(InstallPaths paths) => !File.Exists(paths.StartMenuLink) && !File.Exists(paths.DesktopLink);
    }
}
