using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Engine.Update;
using Microsoft.Win32;

namespace AscendedTweaks.App.Services;

/// <summary>Eigene Profile als einzelne Dateien in %LOCALAPPDATA%\AscendedTweaks\profiles. Jede Datei wird beim Lesen wie ein Import geprueft.</summary>
public sealed class ProfileStore : IProfileStore
{
    private readonly string _directory;
    private readonly ITweakCatalog _catalog;

    public ProfileStore(string directory, ITweakCatalog catalog)
    {
        _directory = directory;
        _catalog = catalog;
    }

    public static string DefaultDirectory =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "profiles");

    public IReadOnlyList<StoredProfile> List()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var result = new List<StoredProfile>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(id, "N", out _))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(file);
                if (info.Length > ProfileCodec.MaxBytes)
                {
                    continue;
                }

                result.Add(new StoredProfile(id, ProfileCodec.Parse(File.ReadAllBytes(file), _catalog)));
            }
            catch (Exception ex) when (ex is AscendedTweaksException or IOException or UnauthorizedAccessException)
            {
                // Beschaedigte oder veraltete Profile werden uebersprungen, nicht geloescht.
            }
        }

        return result.OrderBy(p => p.Profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public StoredProfile Save(TweakProfile profile, string? fileId = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ProfileCodec.Validate(profile, _catalog);
        var id = fileId is not null && Guid.TryParseExact(fileId, "N", out _) ? fileId : Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, id + ".json");
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ProfileCodec.Serialize(profile));
        File.Move(temp, path, overwrite: true);
        return new StoredProfile(id, profile);
    }

    public void Delete(string fileId)
    {
        if (Guid.TryParseExact(fileId, "N", out _))
        {
            File.Delete(Path.Combine(_directory, fileId + ".json"));
        }
    }
}

[ExcludeFromCodeCoverage(Justification = "Systemdialog.")]
public sealed class WindowsProfileFiles : IProfileFiles
{
    private const string Filter = "AscendedTweaks-Profil (*.tpprofile)|*.tpprofile";

    public string? PickOpen()
    {
        var dialog = new OpenFileDialog { Filter = Filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSave(string suggestedName)
    {
        var dialog = new SaveFileDialog { Filter = Filter, FileName = suggestedName, DefaultExt = ProfileCodec.Extension, AddExtension = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>Liest hoechstens 1 MB plus ein Byte; groessere Dateien lehnt die Pruefung ab, ohne sie ganz zu laden.</summary>
    public byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[Math.Min(stream.Length, ProfileCodec.MaxBytes + 1L)];
        stream.ReadExactly(buffer);
        return buffer;
    }

    public void Write(string path, byte[] data) => File.WriteAllBytes(path, data);

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

/// <summary>Messwerte: aktive Autostart-Eintraege, belegter Arbeitsspeicher, freier Platz auf dem Systemlaufwerk (zum Zeitpunkt der Messung).</summary>
[ExcludeFromCodeCoverage(Justification = "Liest Systemwerte.")]
public sealed class WindowsProfileMetrics : IProfileMetrics
{
    private readonly Func<IReadOnlyList<AutostartEntry>> _autostart;
    private readonly Func<BootInfo>? _boot;

    /// <param name="boot">Letzter Start mit Dauer (Ereignis 100 der Startleistung, nur mit Administratorrechten lesbar); optional.</param>
    public WindowsProfileMetrics(Func<IReadOnlyList<AutostartEntry>> autostart, Func<BootInfo>? boot = null)
    {
        _autostart = autostart;
        _boot = boot;
    }

    public ProfileMetrics Capture()
    {
        int? autostarts = null;
        try
        {
            autostarts = _autostart().Count(e => e.Enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AscendedTweaksException)
        {
        }

        long? ram = null;
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            ram = (long)(memory.TotalPhys - memory.AvailPhys);
        }

        long? free = null;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace;
        }
        catch (IOException)
        {
        }

        int? processes = null;
        try
        {
            var all = System.Diagnostics.Process.GetProcesses();
            processes = all.Length;
            foreach (var process in all)
            {
                process.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
        }

        TimeSpan? boot = null;
        try
        {
            boot = _boot?.Invoke().Duration;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AscendedTweaksException)
        {
        }

        return new ProfileMetrics(autostarts, ram, free, processes, boot);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}

/// <summary>Liest die Pause aus HKLM\...\WindowsUpdate\UX\Settings und die Windows-Version (nur lesen, ohne Administratorrechte).</summary>
[ExcludeFromCodeCoverage(Justification = "Reads the registry.")]
public sealed class WindowsUpdateStatusSource : IUpdateStatusSource
{
    private readonly int _build;

    public WindowsUpdateStatusSource(int build) => _build = build;

    public UpdateStatus Read()
    {
        DateTimeOffset? pausedUntil = null;
        string? release = null;
        try
        {
            using var ux = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
            if (ux?.GetValue("PauseUpdatesExpiryTime") is string expiry
                && DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                pausedUntil = parsed;
            }

            using var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            release = version?.GetValue("DisplayVersion") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return new UpdateStatus(pausedUntil, _build >= 22000 ? 11 : 10, release);
    }
}

/// <summary>Protokollordner des Benutzers (%LOCALAPPDATA%\AscendedTweaks\Logs). Protokolle des Helfers liegen unter %ProgramData% und brauchen Administratorrechte.</summary>
[ExcludeFromCodeCoverage(Justification = "File system and Explorer.")]
public sealed class UserLogFolder : ILogFolder
{
    private readonly string _directory;

    public UserLogFolder(string directory) => _directory = directory;

    public (int Files, long Bytes) Measure()
    {
        if (!Directory.Exists(_directory))
        {
            return (0, 0);
        }

        var files = Directory.EnumerateFiles(_directory, "*.jsonl").Select(f => new FileInfo(f)).ToList();
        return (files.Count, files.Sum(f => f.Length));
    }

    public int Delete()
    {
        if (!Directory.Exists(_directory))
        {
            return 0;
        }

        var failed = 0;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.jsonl"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        return failed;
    }

    public void Open()
    {
        Directory.CreateDirectory(_directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{_directory}\"") { UseShellExecute = false });
    }
}

/// <summary>Update-Pruefung mit den festen Einstellungen aus <see cref="UpdateConfig"/>; Download nach %TEMP%\AscendedTweaks\update.</summary>
[ExcludeFromCodeCoverage(Justification = "Network and process start.")]
public sealed class AppUpdateService : IUpdateService
{
    private readonly UpdateChecker _checker;

    public AppUpdateService(string currentVersion)
    {
        _checker = new UpdateChecker(new HttpUpdateTransport(), new AscendedTweaks.Engine.Windows.WindowsSignatureVerifier(), UpdateConfig.ManifestUri, UpdateConfig.PublicKey, UpdateConfig.ExpectedPublisher, currentVersion);
    }

    public Task<UpdateCheckResult> CheckAsync() => _checker.CheckAsync(CancellationToken.None);

    public Task<UpdateDownload> DownloadAsync(UpdateManifest manifest) =>
        _checker.DownloadAsync(manifest, Path.Combine(Path.GetTempPath(), ProductInfo.Id, "update"), CancellationToken.None);

    public void StartInstaller(string path) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
}
