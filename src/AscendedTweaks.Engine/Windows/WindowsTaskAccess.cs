using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Windows;

/// <summary>
/// Aufgabenplanung ueber schtasks.exe aus dem Systemverzeichnis. Gelesen wird die XML-Definition
/// (sprachunabhaengig), geschrieben nur mit /ENABLE oder /DISABLE.
/// </summary>
public sealed class WindowsScheduledTaskAccess : IScheduledTaskAccess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly ICommandRunner _runner;
    private readonly string _schtasks;

    public WindowsScheduledTaskAccess(ICommandRunner runner, string systemDirectory)
    {
        _runner = runner;
        _schtasks = Path.Combine(systemDirectory, "schtasks.exe");
    }

    /// <summary>Wertet die XML-Ausgabe von "schtasks /Query /XML" aus; fehlt Settings/Enabled, ist die Aufgabe aktiv.</summary>
    public static bool? ParseEnabled(string xml)
    {
        try
        {
            var settings = XDocument.Parse(xml).Root?.Element(TaskNamespace + "Settings");
            if (settings is null)
            {
                return null;
            }

            var enabled = settings.Element(TaskNamespace + "Enabled")?.Value.Trim();
            return enabled is null || !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public bool? GetEnabled(string taskPath)
    {
        Guard(taskPath);
        var result = _runner.Run(_schtasks, ["/Query", "/TN", taskPath, "/XML"], Timeout);
        return result.ExitCode == 0 ? ParseEnabled(result.StandardOutput) : null;
    }

    public void SetEnabled(string taskPath, bool enabled)
    {
        Guard(taskPath);
        var result = _runner.Run(_schtasks, ["/Change", "/TN", taskPath, enabled ? "/ENABLE" : "/DISABLE"], Timeout);
        if (result.ExitCode != 0)
        {
            var message = (result.StandardError + result.StandardOutput).Trim();
            throw new AscendedTweaksException(ErrorKind.General, $"schtasks exited with code {result.ExitCode}: {message}");
        }
    }

    private static void Guard(string taskPath)
    {
        if (!ScheduledTaskPaths.IsAllowed(taskPath))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Task path {taskPath} is not allowed.");
        }
    }
}

/// <summary>SystemParametersInfoW fuer die Parameter der Whitelist; Schreiben immer dauerhaft mit Benachrichtigung.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around the Win32 function; the logic lives in the executor.")]
public sealed class WindowsSystemParametersAccess : ISystemParametersAccess
{
    private const uint UpdateIniFile = 0x01;
    private const uint SendChange = 0x02;

    public uint Get(string parameter)
    {
        var definition = SystemParameterWhitelist.Get(parameter);
        switch (definition.Kind)
        {
            case SystemParameterKind.AnimationInfo:
                var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
                Check(Native.SystemParametersInfoW(definition.GetAction, info.Size, ref info, 0), parameter);
                return info.MinAnimate != 0 ? 1u : 0u;
            case SystemParameterKind.MouseTriple:
                var mouse = new int[3];
                Check(Native.SystemParametersInfoW(definition.GetAction, 0, mouse, 0), parameter);
                return (uint)(mouse[0] & 0xFF) | (uint)(mouse[1] & 0xFF) << 8 | (uint)(mouse[2] & 0xFF) << 16;
            case SystemParameterKind.AccessibilityHotkey:
                var keys = ReadAccessibility(definition);
                return (keys.Flags & HotkeyActive) != 0 ? 1u : 0u;
            default:
                uint value = 0;
                Check(Native.SystemParametersInfoW(definition.GetAction, 0, ref value, 0), parameter);
                return definition.Kind == SystemParameterKind.BoolInPointer ? (value != 0 ? 1u : 0u) : value;
        }
    }

    public void Set(string parameter, uint value)
    {
        var definition = SystemParameterWhitelist.Get(parameter);
        switch (definition.Kind)
        {
            case SystemParameterKind.AnimationInfo:
                var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = (int)value };
                Check(Native.SystemParametersInfoW(definition.SetAction, info.Size, ref info, UpdateIniFile | SendChange), parameter);
                break;
            case SystemParameterKind.BoolInPointer:
                Check(Native.SystemParametersInfoW(definition.SetAction, 0, (IntPtr)value, UpdateIniFile | SendChange), parameter);
                break;
            case SystemParameterKind.MouseTriple:
                var mouse = new[] { (int)(value & 0xFF), (int)((value >> 8) & 0xFF), (int)((value >> 16) & 0xFF) };
                Check(Native.SystemParametersInfoW(definition.SetAction, 0, mouse, UpdateIniFile | SendChange), parameter);
                break;
            case SystemParameterKind.AccessibilityHotkey:
                // Nur das Kuerzel-Bit aendern; alle anderen Einstellungen der Eingabehilfe bleiben erhalten.
                var keys = ReadAccessibility(definition);
                keys.Flags = value == 0 ? keys.Flags & ~HotkeyActive : keys.Flags | HotkeyActive;
                Check(Native.SystemParametersInfoW(definition.SetAction, keys.Size, ref keys, UpdateIniFile | SendChange), parameter);
                break;
            default:
                Check(Native.SystemParametersInfoW(definition.SetAction, value, IntPtr.Zero, UpdateIniFile | SendChange), parameter);
                break;
        }
    }

    private const uint HotkeyActive = 0x4;

    /// <summary>STICKYKEYS und TOGGLEKEYS sind 8 Byte gross, FILTERKEYS 24 Byte; die ersten beiden Felder sind gleich.</summary>
    private static AccessibilityKeys ReadAccessibility(SystemParameterDefinition definition)
    {
        var keys = new AccessibilityKeys { Size = definition.Id == "FilterKeysHotkey" ? 24u : 8u };
        Check(Native.SystemParametersInfoW(definition.GetAction, keys.Size, ref keys, 0), definition.Id);
        return keys;
    }

    private static void Check(bool ok, string parameter)
    {
        if (!ok)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"SystemParametersInfo for {parameter} failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibilityKeys
    {
        public uint Size;
        public uint Flags;
        public uint Wait;
        public uint Delay;
        public uint Repeat;
        public uint Bounce;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint action, uint uiParam, [In, Out] int[] pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint action, uint uiParam, ref AccessibilityKeys pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint action, uint uiParam, ref AnimationInfo pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint action, uint uiParam, ref uint pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint action, uint uiParam, IntPtr pvParam, uint winIni);
    }
}
