using System.Diagnostics.CodeAnalysis;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using Microsoft.Win32;

namespace AscendedTweaks.Engine.Debloat;

/// <summary>Gruende, warum OneDrive nicht deinstalliert werden darf. Datenverlust ist ein No-Go (Abschnitt 6.6).</summary>
public enum OneDriveBlock
{
    NotInstalled,
    KnownFolderMove,
    OnlineOnlyFiles,
    ScanIncomplete,
    MachineWide,
    SetupNotTrusted,
}

public sealed record OneDriveStatus(bool Installed, string? SetupPath, IReadOnlyList<OneDriveBlock> Blocks, IReadOnlyList<string> RedirectedFolders, int OnlineOnlyFiles)
{
    public bool CanUninstall => Installed && Blocks.Count == 0 && SetupPath is not null;
}

public interface IOneDriveEnvironment
{
    /// <summary>OneDrive-Wurzelordner aller angemeldeten Konten (privat und Arbeit).</summary>
    IReadOnlyList<string> SyncRoots();

    /// <summary>Bekannte Ordner (Desktop, Dokumente, Bilder, ...) mit aufgeloestem Pfad.</summary>
    IReadOnlyDictionary<string, string> KnownFolders();

    string? PerUserSetup();

    bool MachineWideInstalled();

    /// <summary>Dateiattribute aller Dateien unter einer Wurzel, ohne Reparse Points zu verfolgen; begrenzt.</summary>
    IEnumerable<FileAttributes> FileAttributesUnder(string root);

    SignatureInfo Verify(string file);
}

/// <summary>
/// DEB-028: OneDrive nur deinstallieren, wenn keine bekannten Ordner umgeleitet sind (Known Folder Move) und keine Datei
/// "nur online verfuegbar" ist. Bei Zweifel (Pruefung unvollstaendig) wird blockiert.
/// </summary>
public sealed class OneDriveCheck
{
    public const int MaxFiles = 500_000;

    // FILE_ATTRIBUTE_OFFLINE, FILE_ATTRIBUTE_RECALL_ON_OPEN, FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    private const FileAttributes OnlineOnlyMask = (FileAttributes)0x1000 | (FileAttributes)0x40000 | (FileAttributes)0x400000;
    private static readonly string[] Folders = ["Desktop", "Personal", "My Pictures", "My Music", "My Video", "{374DE290-123F-4565-9164-39C4925E467B}"];

    private readonly IOneDriveEnvironment _environment;

    public OneDriveCheck(IOneDriveEnvironment environment) => _environment = environment;

    public OneDriveStatus Check()
    {
        var setup = _environment.PerUserSetup();
        var machineWide = _environment.MachineWideInstalled();
        if (setup is null && !machineWide)
        {
            return new OneDriveStatus(false, null, [OneDriveBlock.NotInstalled], [], 0);
        }

        var blocks = new List<OneDriveBlock>();
        if (machineWide)
        {
            blocks.Add(OneDriveBlock.MachineWide);
        }

        if (setup is not null)
        {
            var signature = _environment.Verify(setup);
            if (signature.Status != SignatureStatus.Valid || signature.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) != true)
            {
                blocks.Add(OneDriveBlock.SetupNotTrusted);
            }
        }

        var roots = _environment.SyncRoots().Where(r => r.Length > 3).ToList();
        var known = _environment.KnownFolders();
        var redirected = known
            .Where(k => Folders.Contains(k.Key, StringComparer.OrdinalIgnoreCase) && roots.Any(r => IsUnder(k.Value, r)))
            .Select(k => k.Key)
            .ToList();
        if (redirected.Count > 0)
        {
            blocks.Add(OneDriveBlock.KnownFolderMove);
        }

        var onlineOnly = 0;
        var seen = 0;
        foreach (var root in roots)
        {
            foreach (var attributes in _environment.FileAttributesUnder(root))
            {
                if (++seen > MaxFiles)
                {
                    blocks.Add(OneDriveBlock.ScanIncomplete);
                    break;
                }

                if ((attributes & OnlineOnlyMask) != 0)
                {
                    onlineOnly++;
                }
            }

            if (blocks.Contains(OneDriveBlock.ScanIncomplete))
            {
                break;
            }
        }

        if (onlineOnly > 0)
        {
            blocks.Add(OneDriveBlock.OnlineOnlyFiles);
        }

        return new OneDriveStatus(true, setup, blocks, redirected, onlineOnly);
    }

    private static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Deinstalliert OneDrive pro Benutzer, nur wenn <see cref="OneDriveCheck"/> nichts blockiert.</summary>
public sealed class OneDriveUninstaller
{
    private readonly OneDriveCheck _check;
    private readonly ICommandRunner _runner;

    public OneDriveUninstaller(OneDriveCheck check, ICommandRunner runner)
    {
        _check = check;
        _runner = runner;
    }

    public OneDriveStatus Uninstall()
    {
        // Pruefung unmittelbar vor der Deinstallation wiederholen; nie auf eine aeltere Pruefung verlassen.
        var status = _check.Check();
        if (!status.CanUninstall)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"OneDrive-Deinstallation blockiert: {string.Join(", ", status.Blocks)}.");
        }

        var result = _runner.Run(status.SetupPath!, ["/uninstall"], TimeSpan.FromMinutes(10));
        if (result.ExitCode != 0)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"OneDriveSetup exited with code {result.ExitCode}.");
        }

        return _check.Check();
    }
}

[ExcludeFromCodeCoverage(Justification = "Reads only the registry and the file system.")]
public sealed class WindowsOneDriveEnvironment : IOneDriveEnvironment
{
    private readonly ISignatureVerifier _signatures;

    public WindowsOneDriveEnvironment(ISignatureVerifier signatures) => _signatures = signatures;

    public IReadOnlyList<string> SyncRoots()
    {
        var roots = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 3 } value)
            {
                roots.Add(value);
            }
        }

        using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
        foreach (var account in accounts?.GetSubKeyNames() ?? [])
        {
            using var key = accounts!.OpenSubKey(account);
            if (key?.GetValue("UserFolder") is string folder && folder.Length > 3)
            {
                roots.Add(folder);
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyDictionary<string, string> KnownFolders()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        return (key?.GetValueNames() ?? [])
            .Where(n => key!.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string)
            .ToDictionary(n => n, n => Environment.ExpandEnvironmentVariables((string)key!.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!), StringComparer.OrdinalIgnoreCase);
    }

    public string? PerUserSetup()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive");
        if (!File.Exists(Path.Combine(folder, "OneDrive.exe")))
        {
            return null;
        }

        return Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder)
                .Select(d => Path.Combine(d, "OneDriveSetup.exe"))
                .Where(File.Exists)
                .OrderByDescending(p => Version.TryParse(Path.GetFileName(Path.GetDirectoryName(p)), out var v) ? v : new Version(0, 0))
                .FirstOrDefault()
            : null;
    }

    public bool MachineWideInstalled()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return File.Exists(Path.Combine(programFiles, "Microsoft OneDrive", "OneDrive.exe"));
    }

    public IEnumerable<FileAttributes> FileAttributesUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        // Kein Oeffnen der Dateien (das wuerde einen Download ausloesen); nur Attribute der Verzeichniseintraege.
        // OneDrive-Platzhalter sind selbst Reparse Points; sie muessen gezaehlt werden. Schleifen begrenzt MaxFiles (dann blockiert).
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            yield return info.Attributes;
        }
    }

    public SignatureInfo Verify(string file) => _signatures.Verify(file);
}
