using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;
using TweakPilot.Core;
using TweakPilot.Engine.Insights;

namespace TweakPilot.Engine.Windows;

/// <summary>
/// Systeminfos ueber CIM und Registry, ohne Administratorrechte (Abschnitt 6.1). Jeder Teil faengt seine Fehler selbst ab,
/// damit ein nicht lesbarer Wert nicht das ganze Dashboard leer laesst.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsSystemInfo : ISystemInfoSource
{
    private const string Storage = @"root\Microsoft\Windows\Storage";
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly ICommandRunner _commands;
    private readonly string _systemDirectory;

    public WindowsSystemInfo(ICommandRunner commands, string systemDirectory)
    {
        _commands = commands;
        _systemDirectory = systemDirectory;
    }

    public SystemInfo Read() => new(Cpu(), Gpus(), Ram(), Disks(), WindowsVersion(), Boot());

    public HealthFacts ReadHealth()
    {
        var (realtime, antivirus) = Antivirus();
        int? free = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_systemDirectory)!);
            free = (int)Math.Round(drive.AvailableFreeSpace * 100.0 / drive.TotalSize);
        }
        catch (IOException)
        {
        }

        return new HealthFacts(RebootPending(), PowerPlan(), GameMode(), realtime, antivirus, free, Memory() is { } m ? (int)m.dwMemoryLoad : null);
    }

    private static T? Safe<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidCastException or FormatException or COMException or System.Security.SecurityException or IOException or OverflowException)
        {
            return default;
        }
    }

    private static IEnumerable<ManagementObject> Query(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            {
                yield return item;
            }
        }
    }

    private static CpuInfo? Cpu() => Safe(() =>
    {
        var cpus = Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor")
            .Select(p => (Name: (p["Name"] as string)?.Trim() ?? "-", Cores: Convert.ToInt32(p["NumberOfCores"], CultureInfo.InvariantCulture), Threads: Convert.ToInt32(p["NumberOfLogicalProcessors"], CultureInfo.InvariantCulture)))
            .ToList();
        return cpus.Count == 0 ? null : new CpuInfo(cpus[0].Name, cpus.Sum(c => c.Cores), cpus.Sum(c => c.Threads));
    });

    private static IReadOnlyList<GpuInfo> Gpus() => Safe(() =>
    {
        var drivers = Query(@"root\cimv2", "SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass = 'DISPLAY'")
            .Select(d => (Name: d["DeviceName"] as string, Version: d["DriverVersion"] as string, Date: d["DriverDate"] as string))
            .Where(d => d.Name is not null)
            .ToList();
        var memory = VideoMemory();
        return (IReadOnlyList<GpuInfo>)Query(@"root\cimv2", "SELECT Name, PNPDeviceID FROM Win32_VideoController")
            .Select(v =>
            {
                var name = (v["Name"] as string)?.Trim() ?? "-";
                var driver = drivers.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                DateOnly? date = driver.Date is { Length: >= 8 } dmtf ? DateOnly.FromDateTime(ManagementDateTimeConverter.ToDateTime(dmtf)) : null;
                return new GpuInfo(HardwareNames.GpuVendor(v["PNPDeviceID"] as string, name), name, driver.Version, date, memory.TryGetValue(name, out var bytes) ? bytes : null);
            })
            .ToList();
    }) ?? [];

    /// <summary>Videospeicher aus HardwareInformation.qwMemorySize (Win32_VideoController.AdapterRAM ist bei 4 GB gedeckelt).</summary>
    private static Dictionary<string, long> VideoMemory()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (root is null)
        {
            return result;
        }

        foreach (var name in root.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
        {
            try
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string description)
                {
                    continue;
                }

                var size = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q => q,
                    byte[] { Length: 8 } b => BitConverter.ToInt64(b),
                    _ => key.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int d => (long)(uint)d,
                        byte[] { Length: 4 } b => BitConverter.ToUInt32(b),
                        _ => 0L,
                    },
                };
                if (size > 0)
                {
                    result[description] = size;
                }
            }
            catch (System.Security.SecurityException)
            {
            }
        }

        return result;
    }

    private static RamInfo? Ram()
    {
        if (Memory() is not { } m)
        {
            return null;
        }

        var type = Safe(() => Query(@"root\cimv2", "SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory")
            .Select(p => HardwareNames.MemoryType(Convert.ToUInt32(p["SMBIOSMemoryType"], CultureInfo.InvariantCulture)))
            .FirstOrDefault(t => t is not null));
        return new RamInfo((long)m.ullTotalPhys, (long)(m.ullTotalPhys - m.ullAvailPhys), (int)m.dwMemoryLoad, type);
    }

    private static IReadOnlyList<DiskInfo> Disks() => Safe(() =>
    {
        var free = new Dictionary<uint, long>();
        foreach (var partition in Query(Storage, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
        {
            var letter = Convert.ToChar(partition["DriveLetter"] ?? '\0', CultureInfo.InvariantCulture);
            if (!char.IsAsciiLetterUpper(letter))
            {
                continue;
            }

            var drive = new DriveInfo(letter + ":\\");
            if (drive.IsReady)
            {
                var number = Convert.ToUInt32(partition["DiskNumber"], CultureInfo.InvariantCulture);
                free[number] = free.GetValueOrDefault(number) + drive.AvailableFreeSpace;
            }
        }

        return (IReadOnlyList<DiskInfo>)Query(Storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk")
            .Select(d =>
            {
                var id = uint.TryParse(d["DeviceId"] as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : uint.MaxValue;
                return new DiskInfo(
                    (d["FriendlyName"] as string)?.Trim() ?? "-",
                    HardwareNames.DiskKind(Convert.ToUInt16(d["MediaType"], CultureInfo.InvariantCulture), Convert.ToUInt16(d["BusType"], CultureInfo.InvariantCulture)),
                    Convert.ToInt64(d["Size"], CultureInfo.InvariantCulture),
                    free.TryGetValue(id, out var bytes) ? bytes : null);
            })
            .Where(d => d.SizeBytes > 0)
            .OrderBy(d => d.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }) ?? [];

    private static WindowsInfo? WindowsVersion() => Safe(() =>
    {
        var caption = Query(@"root\cimv2", "SELECT Caption FROM Win32_OperatingSystem").Select(o => (o["Caption"] as string)?.Trim()).FirstOrDefault() ?? "Windows";
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = key?.GetValue("CurrentBuild") as string ?? "-";
        var ubr = key?.GetValue("UBR") is int u ? "." + u.ToString(CultureInfo.InvariantCulture) : string.Empty;
        DateTimeOffset? installed = key?.GetValue("InstallDate") is int seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds((uint)seconds) : null;
        return new WindowsInfo(caption.Replace("Microsoft ", string.Empty, StringComparison.Ordinal), key?.GetValue("DisplayVersion") as string, build + ubr, installed);
    });

    /// <summary>Letzter Start aus Win32_OperatingSystem; Startdauer aus Ereignis 100 (Diagnostics-Performance), das nur Administratoren lesen duerfen.</summary>
    private BootInfo Boot()
    {
        var last = Safe(() => Query(@"root\cimv2", "SELECT LastBootUpTime FROM Win32_OperatingSystem")
            .Select(o => o["LastBootUpTime"] as string)
            .Where(s => s is not null)
            .Select(s => (DateTimeOffset?)new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(s!)))
            .FirstOrDefault());

        var result = _commands.Run(
            Path.Combine(_systemDirectory, "wevtutil.exe"),
            ["qe", "Microsoft-Windows-Diagnostics-Performance/Operational", "/q:*[System[(EventID=100)]]", "/c:1", "/rd:true", "/f:xml"],
            TimeSpan.FromSeconds(15));
        if (result.ExitCode != 0)
        {
            return new BootInfo(last, null, DurationNeedsAdmin: result.ExitCode == 5);
        }

        try
        {
            var data = XDocument.Parse("<r>" + result.StandardOutput + "</r>").Descendants().FirstOrDefault(e => e.Name.LocalName == "Data" && (string?)e.Attribute("Name") == "BootTime");
            return new BootInfo(last, data is not null && long.TryParse(data.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? TimeSpan.FromMilliseconds(ms) : null, false);
        }
        catch (System.Xml.XmlException)
        {
            return new BootInfo(last, null, false);
        }
    }

    /// <summary>Bekannte Hinweise auf einen ausstehenden Neustart (Komponentenspeicher, Windows Update, Dateiumbenennungen).</summary>
    private static bool? RebootPending() => Safe<bool?>(() =>
    {
        using var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        using var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        using var session = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
        return cbs is not null || wu is not null || session?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 };
    });

    private static string? PowerPlan() => Safe(() =>
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }

        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(pointer);
            uint size = 0;
            if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                return PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0 ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            LocalFree(pointer);
        }
    });

    /// <summary>Spielmodus: AutoGameModeEnabled unter HKCU\Software\Microsoft\GameBar; fehlt der Wert, gilt der Windows-Standard "an".</summary>
    private static bool? GameMode() => Safe<bool?>(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
        return key?.GetValue("AutoGameModeEnabled") is not int value || value != 0;
    });

    /// <summary>Defender laut MSFT_MpComputerStatus (nur Anzeige) und die Namen aller registrierten Virenschutzprogramme.</summary>
    private static (bool? Realtime, IReadOnlyList<string> Names) Antivirus()
    {
        var realtime = Safe<bool?>(() => Query(@"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus")
            .Select(s => s["RealTimeProtectionEnabled"] as bool?)
            .FirstOrDefault());
        var names = Safe(() => Query(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct")
            .Select(p => p["displayName"] as string)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()) ?? [];
        return (realtime, names);
    }

    private static MemoryStatusEx? Memory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr powerSetting, IntPtr buffer, ref uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:Accessible fields should begin with upper-case letter", Justification = "Win32-Struktur.")]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
