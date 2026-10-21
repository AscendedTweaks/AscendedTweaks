using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TweakPilot.Core;
using TweakPilot.Engine.Cleanup;

namespace TweakPilot.Engine.Windows;

/// <summary>Dateisystem fuer die Bereinigung. Loeschen ueber ein Handle mit FILE_FLAG_OPEN_REPARSE_POINT (Abschnitt 9.4).</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around Win32; the logic lives in CleanupEngine.")]
public sealed class WindowsCleanupFileSystem : ICleanupFileSystem
{
    private const uint Delete = 0x00010000;
    private const uint FileReadAttributes = 0x80;
    private const uint FileWriteAttributes = 0x100;
    private const uint FileShareRead = 0x1;
    private const uint OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000;
    private const uint FlagBackupSemantics = 0x02000000;
    private const int FileBasicInfo = 0;
    private const int FileDispositionInfo = 4;
    private const uint AttributeReadOnly = 0x1;
    private const uint AttributeDirectory = 0x10;
    private const uint AttributeReparsePoint = 0x400;

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FsEntry? Stat(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists && !Directory.Exists(path))
        {
            return null;
        }

        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        return new FsEntry(path, isDirectory, (attributes & FileAttributes.ReparsePoint) != 0, isDirectory ? 0 : info.Length, File.GetLastWriteTimeUtc(path), attributes);
    }

    public IEnumerable<FsEntry> List(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false };
        foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
        {
            var isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
            yield return new FsEntry(
                item.FullName,
                isDirectory,
                (item.Attributes & FileAttributes.ReparsePoint) != 0,
                isDirectory ? 0 : ((FileInfo)item).Length,
                item.LastWriteTimeUtc,
                item.Attributes);
        }
    }

    public DeleteOutcome DeleteFile(string path)
    {
        using var handle = CreateFileW(path, Delete | FileReadAttributes | FileWriteAttributes, FileShareRead, IntPtr.Zero, OpenExisting, FlagOpenReparsePoint | FlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Marshal.GetLastWin32Error() switch
            {
                2 or 3 => DeleteOutcome.Missing,
                32 or 33 or 5 => DeleteOutcome.InUse,
                _ => DeleteOutcome.Failed,
            };
        }

        var basic = new BasicInfo();
        if (!GetFileInformationByHandleEx(handle, FileBasicInfo, ref basic, (uint)Marshal.SizeOf<BasicInfo>()))
        {
            return DeleteOutcome.Failed;
        }

        // Pruefung am geoeffneten Handle: kein Reparse Point, kein Ordner (Schutz vor Tausch zwischen Scan und Loeschen).
        if ((basic.FileAttributes & AttributeReparsePoint) != 0)
        {
            return DeleteOutcome.ReparsePoint;
        }

        if ((basic.FileAttributes & AttributeDirectory) != 0)
        {
            return DeleteOutcome.Failed;
        }

        if ((basic.FileAttributes & AttributeReadOnly) != 0)
        {
            basic.FileAttributes &= ~AttributeReadOnly;
            if (basic.FileAttributes == 0)
            {
                basic.FileAttributes = 0x80; // FILE_ATTRIBUTE_NORMAL
            }

            SetFileInformationByHandle(handle, FileBasicInfo, ref basic, (uint)Marshal.SizeOf<BasicInfo>());
        }

        var disposition = new DispositionInfo { DeleteFile = 1 };
        return SetFileInformationByHandle(handle, FileDispositionInfo, ref disposition, (uint)Marshal.SizeOf<DispositionInfo>())
            ? DeleteOutcome.Deleted
            : Marshal.GetLastWin32Error() is 5 or 32 ? DeleteOutcome.InUse : DeleteOutcome.Failed;
    }

    public bool DeleteEmptyDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) == 0 && RemoveDirectoryW(path);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass, ref BasicInfo info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass, ref BasicInfo info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass, ref DispositionInfo info, uint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string path);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispositionInfo
    {
        public byte DeleteFile;
    }
}

/// <summary>Papierkorb ueber IFileOperation (FOFX_RECYCLEONDELETE, Rueckgaengig ueber den Explorer).</summary>
[ExcludeFromCodeCoverage(Justification = "COM-Huelle.")]
public sealed class WindowsRecycler : IRecycler
{
    // FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_WANTNUKEWARNING | FOFX_RECYCLEONDELETE.
    // FOF_WANTNUKEWARNING warnt, falls eine Datei nicht in den Papierkorb passt und endgueltig geloescht wuerde.
    private const uint Flags = 0x4 | 0x10 | 0x40 | 0x400 | 0x4000 | 0x80000;

    public void Recycle(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"))!)!;
        try
        {
            operation.SetOperationFlags(Flags);
            foreach (var path in paths)
            {
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) == 0)
                {
                    operation.DeleteItem(item, IntPtr.Zero);
                    Marshal.ReleaseComObject(item);
                }
            }

            try
            {
                operation.PerformOperations();
            }
            catch (COMException)
            {
                // Einzelne Fehler (zum Beispiel Datei in Benutzung) zaehlt die Engine anhand der verbleibenden Dateien.
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(IntPtr sink, out uint cookie);

        void Unadvise(uint cookie);

        void SetOperationFlags(uint flags);

        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

        void SetProgressDialog(IntPtr dialog);

        void SetProperties(IntPtr properties);

        void SetOwnerWindow(IntPtr owner);

        void ApplyPropertiesToItem(IntPtr item);

        void ApplyPropertiesToItems(IntPtr items);

        void RenameItem(IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);

        void RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);

        void MoveItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);

        void MoveItems(IntPtr items, IntPtr destination);

        void CopyItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string copyName, IntPtr sink);

        void CopyItems(IntPtr items, IntPtr destination);

        void DeleteItem(IShellItem item, IntPtr sink);

        void DeleteItems(IntPtr items);

        void NewItem(IntPtr destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string template, IntPtr sink);

        void PerformOperations();

        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }
}

[ExcludeFromCodeCoverage(Justification = "Reads only the process list.")]
public sealed class WindowsProcessProbe : IProcessProbe
{
    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}

/// <summary>Sonderablaeufe ueber offizielle Windows-Wege (Abschnitt 6.5).</summary>
[ExcludeFromCodeCoverage(Justification = "Calls Windows tools; the flow rules are tested with fakes in CleanupSpecialsTests.")]
public sealed class WindowsCleanupSpecials : ICleanupSpecials
{
    private readonly PreviousInstallationsRunner _previous;
    private readonly ICommandRunner _commands;
    private readonly string _systemDirectory;

    public WindowsCleanupSpecials(IRegistryAccess registry, ICommandRunner commands, string systemDirectory)
    {
        _previous = new PreviousInstallationsRunner(registry, commands, systemDirectory);
        _commands = commands;
        _systemDirectory = systemDirectory;
    }

    public CleanupScan ScanRecycleBin()
    {
        var info = new RecycleBinInfo { Size = (uint)Marshal.SizeOf<RecycleBinInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0
            ? new CleanupScan { CategoryId = string.Empty, Bytes = info.Bytes, Files = (int)Math.Min(int.MaxValue, info.Items) }
            : new CleanupScan { CategoryId = string.Empty, SizeUnknown = true };
    }

    public CleanupResult EmptyRecycleBin()
    {
        var before = ScanRecycleBin();
        if (before.Files == 0 && !before.SizeUnknown)
        {
            return new CleanupResult { CategoryId = string.Empty, Mode = CleanupMode.Permanent };
        }

        // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND; die Bestaetigung kam vorher in der App.
        var result = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
        return result == 0
            ? new CleanupResult { CategoryId = string.Empty, Mode = CleanupMode.Permanent, DeletedFiles = before.Files, DeletedBytes = before.Bytes }
            : new CleanupResult { CategoryId = string.Empty, Mode = CleanupMode.Permanent, Failed = 1, Message = new Win32Exception(result).Message };
    }

    public CleanupResult RunPreviousInstallations() => _previous.Run();

    public CleanupResult RunComponentStoreCleanup()
    {
        // Nur /StartComponentCleanup, nie /ResetBase (Abschnitt 6.5).
        var result = _commands.Run(Path.Combine(_systemDirectory, "dism.exe"), ["/Online", "/Cleanup-Image", "/StartComponentCleanup"], TimeSpan.FromMinutes(60));
        return new CleanupResult
        {
            CategoryId = string.Empty,
            Mode = CleanupMode.Permanent,
            Failed = result.ExitCode == 0 ? 0 : 1,
            Message = result.ExitCode == 0 ? null : $"DISM exited with code {result.ExitCode}.",
        };
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref RecycleBinInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr window, string? rootPath, uint flags);

    /// <summary>SHQUERYRBINFO; unter 64 Bit mit 8-Byte-Ausrichtung (24 Byte).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct RecycleBinInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }
}

/// <summary>
/// CLEAN-005 ueber den offiziellen Handler: StateFlags fuer genau "Previous Installations" setzen, cleanmgr /sagerun
/// ausfuehren und alle StateFlags-Werte danach exakt wiederherstellen (auch die anderer Handler, die vorher gesetzt waren).
/// </summary>
public sealed class PreviousInstallationsRunner
{
    public const string VolumeCaches = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";
    public const string Handler = "Previous Installations";
    public const string FlagName = "StateFlags0042";

    private readonly IRegistryAccess _registry;
    private readonly ICommandRunner _commands;
    private readonly string _systemDirectory;

    public PreviousInstallationsRunner(IRegistryAccess registry, ICommandRunner commands, string systemDirectory)
    {
        _registry = registry;
        _commands = commands;
        _systemDirectory = systemDirectory;
    }

    public CleanupResult Run()
    {
        if (!_registry.KeyExists(RegHive.HKLM, $@"{VolumeCaches}\{Handler}"))
        {
            return new CleanupResult { CategoryId = string.Empty, Mode = CleanupMode.Permanent, Message = "handler-missing" };
        }

        var saved = _registry.GetSubKeyNames(RegHive.HKLM, VolumeCaches)
            .ToDictionary(h => h, h => _registry.GetValue(RegHive.HKLM, $@"{VolumeCaches}\{h}", FlagName), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (handler, value) in saved.Where(s => s.Value is not null))
            {
                _registry.DeleteValue(RegHive.HKLM, $@"{VolumeCaches}\{handler}", FlagName);
            }

            _registry.SetValue(RegHive.HKLM, $@"{VolumeCaches}\{Handler}", FlagName, RegistryData.DWord(2));
            var result = _commands.Run(Path.Combine(_systemDirectory, "cleanmgr.exe"), ["/sagerun:42"], TimeSpan.FromMinutes(60));
            return new CleanupResult
            {
                CategoryId = string.Empty,
                Mode = CleanupMode.Permanent,
                Failed = result.ExitCode == 0 ? 0 : 1,
                Message = result.ExitCode == 0 ? null : $"cleanmgr exited with code {result.ExitCode}.",
            };
        }
        finally
        {
            foreach (var (handler, value) in saved)
            {
                var path = $@"{VolumeCaches}\{handler}";
                if (value is null)
                {
                    if (_registry.GetValue(RegHive.HKLM, path, FlagName) is not null)
                    {
                        _registry.DeleteValue(RegHive.HKLM, path, FlagName);
                    }
                }
                else
                {
                    _registry.SetValue(RegHive.HKLM, path, FlagName, value);
                }
            }
        }
    }
}

/// <summary>Basisordner fuer die Bereinigung; der Helfer bekommt nur die maschinenweiten.</summary>
[ExcludeFromCodeCoverage(Justification = "Reads environment values only.")]
public static class CleanupBases
{
    public static IReadOnlyDictionary<CleanupBase, string> Machine()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new Dictionary<CleanupBase, string>
        {
            [CleanupBase.Windows] = windows,
            [CleanupBase.ProgramData] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            [CleanupBase.SystemDrive] = Path.GetPathRoot(windows)!,
        };
    }

    public static IReadOnlyDictionary<CleanupBase, string> User()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new Dictionary<CleanupBase, string>
        {
            [CleanupBase.UserTemp] = Path.GetTempPath().TrimEnd('\\'),
            [CleanupBase.LocalAppData] = local,
            [CleanupBase.LocalAppDataLow] = Path.Combine(Path.GetDirectoryName(local)!, "LocalLow"),
            [CleanupBase.RoamingAppData] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };
    }

    public static IReadOnlyList<string> Protected() =>
        PathGuard.ProtectedFolders(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}
