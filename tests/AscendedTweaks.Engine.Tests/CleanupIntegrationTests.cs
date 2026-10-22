using System.Diagnostics;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Cleanup;
using AscendedTweaks.Engine.Windows;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

/// <summary>
/// Echtes Windows-Dateisystem in einem eigenen Sandbox-Ordner: Loeschen ueber Handle, echte Junction, geoeffnete Datei,
/// schreibgeschuetzte Datei. Beruehrt keine Daten ausserhalb der Sandbox.
/// </summary>
public sealed class CleanupIntegrationTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "AscendedTweaks.CleanupTest." + Guid.NewGuid().ToString("N"));
    private readonly string _temp;
    private readonly string _outside;

    public CleanupIntegrationTests()
    {
        _temp = Path.Combine(_sandbox, "temp");
        _outside = Path.Combine(_sandbox, "ausserhalb");
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        var junction = Path.Combine(_temp, "falle");
        if (Directory.Exists(junction))
        {
            Directory.Delete(junction); // entfernt nur die Junction, nicht das Ziel
        }

        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [Fact]
    public void Real_file_system_deletes_old_files_but_not_through_junctions_or_open_files()
    {
        var old = DateTime.UtcNow.AddDays(-3);
        var oldFile = Write(Path.Combine(_temp, "alt.tmp"), old);
        var readOnly = Write(Path.Combine(_temp, "schreibgeschuetzt.tmp"), old);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        var fresh = Write(Path.Combine(_temp, "neu.tmp"), DateTime.UtcNow);
        var openFile = Write(Path.Combine(_temp, "offen.tmp"), old);
        Directory.CreateDirectory(Path.Combine(_temp, "sub"));
        var nested = Write(Path.Combine(_temp, "sub", "alt2.tmp"), old);
        var precious = Write(Path.Combine(_outside, "wichtig.docx"), old);
        CreateJunction(Path.Combine(_temp, "falle"), _outside);

        var engine = new CleanupEngine(
            new WindowsCleanupFileSystem(),
            new WindowsRecycler(),
            new NoProcesses(),
            new NoSpecials(),
            new FakeServices(),
            new Dictionary<CleanupBase, string> { [CleanupBase.UserTemp] = _temp },
            PathGuard.ProtectedFolders(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            TweakScope.User,
            TimeProvider.System);

        var scan = engine.Scan("CLEAN-001");
        scan.Files.Should().Be(4);
        scan.SkippedReparsePoints.Should().Be(1);

        CleanupResult result;
        using (new FileStream(openFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = engine.Run("CLEAN-001", CleanupMode.Permanent);
        }

        result.DeletedFiles.Should().Be(3);
        result.SkippedInUse.Should().Be(1);
        File.Exists(oldFile).Should().BeFalse();
        File.Exists(readOnly).Should().BeFalse("schreibgeschuetzte Temp-Dateien werden ebenfalls entfernt");
        File.Exists(nested).Should().BeFalse();
        Directory.Exists(Path.Combine(_temp, "sub")).Should().BeFalse("leerer Unterordner wird entfernt");
        File.Exists(fresh).Should().BeTrue("juenger als ein Tag");
        File.Exists(openFile).Should().BeTrue("in Benutzung");
        File.Exists(precious).Should().BeTrue("hinter der Junction wird nie geloescht");
        Directory.Exists(Path.Combine(_temp, "falle")).Should().BeTrue("die Junction selbst bleibt");
    }

    [Fact]
    public void Handle_based_delete_refuses_a_junction()
    {
        var junction = Path.Combine(_temp, "falle");
        CreateJunction(junction, _outside);

        new WindowsCleanupFileSystem().DeleteFile(junction).Should().Be(DeleteOutcome.ReparsePoint);
        Directory.Exists(_outside).Should().BeTrue();
    }

    private static string Write(string path, DateTime lastWriteUtc)
    {
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static void CreateJunction(string link, string target)
    {
        // mklink /J braucht keine Administratorrechte.
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit(10000);
        Directory.Exists(link).Should().BeTrue("die Junction muss fuer den Test existieren");
    }

    private sealed class NoProcesses : IProcessProbe
    {
        public bool IsRunning(string processName) => false;
    }

    private sealed class NoSpecials : ICleanupSpecials
    {
        public CleanupScan ScanRecycleBin() => throw new NotSupportedException();

        public CleanupResult EmptyRecycleBin() => throw new NotSupportedException();

        public CleanupResult RunPreviousInstallations() => throw new NotSupportedException();

        public CleanupResult RunComponentStoreCleanup() => throw new NotSupportedException();
    }
}
