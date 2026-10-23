using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Hardware-Fakten fuer die Gaming-Checks, nur lesend.</summary>
public interface IHardwareFacts
{
    /// <summary>Hauptbildschirm: aktuelle Rate und hoechste Rate bei derselben Aufloesung; null, wenn nicht lesbar.</summary>
    DisplayFacts? ReadDisplay();

    /// <summary>Arbeitsspeicher-Module; null, wenn nicht lesbar.</summary>
    IReadOnlyList<MemoryModule>? ReadMemoryModules();
}

/// <summary>
/// EnumDisplaySettings (Microsoft Learn: ENUM_CURRENT_SETTINGS, danach Modi ab 0 aufzaehlen) und Win32_PhysicalMemory
/// (Speed und ConfiguredClockSpeed, beide aus SMBIOS Type 17).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin Win32 and WMI wrapper; the rating lives in GamingChecks.")]
public sealed class WindowsHardwareFacts : IHardwareFacts, IDisplayAccess
{
    private const int EnumCurrentSettings = -1;
    private const int DisplayFrequencyField = 0x400000;
    private const uint ChangeUpdateRegistry = 0x01;
    private const uint ChangeTest = 0x02;
    private const int ChangeSuccessful = 0;

    public DisplayFacts? ReadDisplay() => ReadRefreshRates() is { } rates ? new DisplayFacts(rates.Current, rates.Highest) : null;

    public RefreshRates? ReadRefreshRates()
    {
        var current = NewMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current))
        {
            return null;
        }

        var offered = Offered(current);
        return new RefreshRates(current.DisplayFrequency, offered.Count == 0 ? current.DisplayFrequency : Math.Max(offered.Max(), current.DisplayFrequency));
    }

    /// <summary>
    /// Only a rate the driver lists for the current resolution and color depth, tested with CDS_TEST first, then saved
    /// (CDS_UPDATEREGISTRY) like the Settings app does. Microsoft Learn: ChangeDisplaySettingsExW, DEVMODEW dmDisplayFrequency.
    /// </summary>
    public void SetRefreshRate(int hertz)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
        {
            throw new AscendedTweaksException(ErrorKind.Unavailable, "The display mode is not readable.");
        }

        if (!Offered(mode).Contains(hertz))
        {
            throw new AscendedTweaksException(ErrorKind.Unavailable, $"The main display does not offer {hertz} Hz at its current resolution.");
        }

        mode.DisplayFrequency = hertz;
        mode.Fields = DisplayFrequencyField;
        var test = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, ChangeTest, IntPtr.Zero);
        var result = test == ChangeSuccessful ? ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, ChangeUpdateRegistry, IntPtr.Zero) : test;
        if (result != ChangeSuccessful)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"Windows refused {hertz} Hz (ChangeDisplaySettingsEx result {result}).");
        }
    }

    /// <summary>Rates the driver lists at the resolution and color depth of <paramref name="current"/>.</summary>
    private static List<int> Offered(DevMode current)
    {
        var rates = new List<int>();
        var mode = NewMode();
        for (var i = 0; EnumDisplaySettings(null, i, ref mode); i++)
        {
            // 0 and 1 mean "hardware default" (Microsoft Learn, DEVMODEW), not a real rate.
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel && mode.DisplayFrequency > 1)
            {
                rates.Add(mode.DisplayFrequency);
            }

            mode = NewMode();
        }

        return rates;
    }

    public IReadOnlyList<MemoryModule>? ReadMemoryModules()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            var modules = new List<MemoryModule>();
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    modules.Add(new MemoryModule(Positive(item["Speed"]), Positive(item["ConfiguredClockSpeed"])));
                }
            }

            return modules;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }

    private static int? Positive(object? value) =>
        value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture) is > 0 and var v ? v : null;

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DevMode devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    /// <summary>DEVMODEW fuer Anzeigegeraete (Positionsfelder statt Druckerfelder).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}
