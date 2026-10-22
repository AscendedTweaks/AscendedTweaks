using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using AscendedTweaks.Core;

namespace AscendedTweaks.App.Services;

/// <summary>Zielorte der Installation pro Benutzer (V2 Abschnitt 7.3); Tests setzen eigene Ordner (Test-Hook).</summary>
public sealed record InstallPaths(string ProgramDirectory, string StartMenuDirectory, string DesktopDirectory)
{
    public const string ExeName = "AscendedTweaks.exe";

    public static InstallPaths ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductInfo.Id),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string StartMenuLink => Path.Combine(StartMenuDirectory, ProductInfo.Name + ".lnk");

    public string DesktopLink => Path.Combine(DesktopDirectory, ProductInfo.Name + ".lnk");

    public string InstalledExe => Path.Combine(ProgramDirectory, ExeName);
}

public interface IShortcutWriter
{
    void Create(string linkPath, string targetPath, string workingDirectory, string description);
}

public sealed record UninstallResult(bool Removed, IReadOnlyList<string> Problems);

/// <summary>
/// Installation ohne Zusatzprogramm und ohne Administratorrechte: Paketordner nach %LOCALAPPDATA%\Programs\AscendedTweaks kopieren,
/// Startmenue- und Desktop-Verknuepfung, Eintrag in "Apps" unter HKCU. Kein Autostart, kein Dienst, keine Aufgabe.
/// Deinstallieren loescht nur einen Ordner, der die eigene Installationsmarkierung traegt.
/// </summary>
public sealed class AppInstaller
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AscendedTweaks";
    public const string PackageMarker = ".ascendedtweaks-package";
    public const string InstallMarker = ".ascendedtweaks-install";

    /// <summary>Nur diese Teile des Paketordners werden kopiert.</summary>
    private static readonly string[] PackageEntries = [InstallPaths.ExeName, "app", "Docs", "START HERE.txt"];

    private readonly InstallPaths _paths;
    private readonly IRegistryAccess _registry;
    private readonly IShortcutWriter _shortcuts;

    public AppInstaller(InstallPaths paths, IRegistryAccess registry, IShortcutWriter shortcuts)
    {
        _paths = paths;
        _registry = registry;
        _shortcuts = shortcuts;
    }

    public InstallPaths Paths => _paths;

    /// <summary>Paketordner erkennen: Markierung, Programm oben und Helfer in "app".</summary>
    public static bool IsPackage(string directory) =>
        File.Exists(Path.Combine(directory, PackageMarker))
        && File.Exists(Path.Combine(directory, InstallPaths.ExeName))
        && File.Exists(Path.Combine(directory, "app", "AscendedTweaks.Helper.exe"));

    public bool IsInstalledCopy(string directory) => SamePath(directory, _paths.ProgramDirectory);

    public void Install(string packageDirectory, string version)
    {
        if (!IsPackage(packageDirectory))
        {
            throw new AscendedTweaksException(ErrorKind.General, "Kein AscendedTweaks-Paketordner.");
        }

        var target = _paths.ProgramDirectory;
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !File.Exists(Path.Combine(target, InstallMarker)))
        {
            // Fremder Ordner gleichen Namens: nichts ueberschreiben.
            throw new AscendedTweaksException(ErrorKind.General, $"The folder {target} does not belong to Ascended Tweaks; nothing was changed.");
        }

        Directory.CreateDirectory(target);
        foreach (var entry in PackageEntries)
        {
            var source = Path.Combine(packageDirectory, entry);
            var destination = Path.Combine(target, entry);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, destination);
            }
            else if (File.Exists(source))
            {
                File.Copy(source, destination, overwrite: true);
            }
        }

        File.WriteAllText(Path.Combine(target, InstallMarker), version);
        Directory.CreateDirectory(_paths.StartMenuDirectory);
        _shortcuts.Create(_paths.StartMenuLink, _paths.InstalledExe, target, ProductInfo.Name);
        _shortcuts.Create(_paths.DesktopLink, _paths.InstalledExe, target, ProductInfo.Name);

        var sizeKb = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
        _registry.SetValue(RegHive.HKCU, UninstallKey, "DisplayName", RegistryData.String(ProductInfo.Name));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "DisplayVersion", RegistryData.String(version));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "Publisher", RegistryData.String(ProductInfo.Name));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "InstallLocation", RegistryData.String(target));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "DisplayIcon", RegistryData.String(_paths.InstalledExe));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "UninstallString", RegistryData.String($"\"{_paths.InstalledExe}\" --uninstall"));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "EstimatedSize", RegistryData.DWord((uint)Math.Min(uint.MaxValue, sizeKb)));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "NoModify", RegistryData.DWord(1u));
        _registry.SetValue(RegHive.HKCU, UninstallKey, "NoRepair", RegistryData.DWord(1u));
    }

    /// <summary>
    /// Entfernt Programmordner, Verknuepfungen und den Eintrag in "Apps". Vorher: Der Ordner muss genau der eigene
    /// Installationsordner sein und die Installationsmarkierung tragen. Daten (Sicherungen, Einstellungen) nur, wenn gewuenscht.
    /// </summary>
    public UninstallResult Uninstall(string installDirectory, bool removeData, IReadOnlyList<string> dataDirectories)
    {
        if (!IsInstalledCopy(installDirectory) || !File.Exists(Path.Combine(installDirectory, InstallMarker)))
        {
            return new UninstallResult(false, [$"{installDirectory} is not the Ascended Tweaks installation; nothing was deleted."]);
        }

        var problems = new List<string>();
        foreach (var link in new[] { _paths.StartMenuLink, _paths.DesktopLink })
        {
            Try(() => File.Delete(link), problems);
        }

        Try(() => Directory.Delete(installDirectory, recursive: true), problems);
        Try(() => _registry.DeleteKeyTree(RegHive.HKCU, UninstallKey), problems);
        if (removeData)
        {
            foreach (var data in dataDirectories.Where(Directory.Exists))
            {
                Try(() => Directory.Delete(data, recursive: true), problems);
            }
        }

        return new UninstallResult(!Directory.Exists(installDirectory), problems);
    }

    private static void Try(Action action, List<string> problems)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AscendedTweaksException)
        {
            problems.Add(ex.Message);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Verknuepfung ueber die Shell-Link-API (IShellLinkW, IPersistFile).</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne COM-Huelle.")]
public sealed class ShellShortcutWriter : IShortcutWriter
{
    public void Create(string linkPath, string targetPath, string workingDirectory, string description)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);
            link.SetIconLocation(targetPath, 0);
            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr findData, int flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int iconPathLength, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, int reserved);

        void Resolve(IntPtr hwnd, int flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
