namespace AscendedTweaks.Engine.Insights;

/// <summary>Systeminfos fuer das Dashboard (Abschnitt 6.1), nur lesend. Nicht lesbare Werte sind null.</summary>
public sealed record SystemInfo(
    CpuInfo? Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    RamInfo? Ram,
    IReadOnlyList<DiskInfo> Disks,
    WindowsInfo? Windows,
    BootInfo Boot);

public sealed record CpuInfo(string Name, int Cores, int Threads);

public sealed record GpuInfo(string Vendor, string Name, string? DriverVersion, DateOnly? DriverDate, long? MemoryBytes);

public sealed record RamInfo(long TotalBytes, long UsedBytes, int LoadPercent, string? ModuleType);

public enum DiskKind
{
    Unknown,
    Hdd,
    Ssd,
    Nvme,
}

public sealed record DiskInfo(string Model, DiskKind Kind, long SizeBytes, long? FreeBytes);

public sealed record WindowsInfo(string Edition, string? Version, string Build, DateTimeOffset? InstalledUtc);

/// <summary>Letzter Start; die Startdauer steht im Ereignisprotokoll und ist nur mit Administratorrechten lesbar.</summary>
public sealed record BootInfo(DateTimeOffset? LastBootUtc, TimeSpan? Duration, bool DurationNeedsAdmin);

/// <summary>Messbare Zustandsanzeigen ohne Gesamtwert (Abschnitt 6.1).</summary>
public sealed record HealthFacts(
    bool? RebootPending,
    string? PowerPlan,
    bool? GameMode,
    bool? DefenderRealtime,
    IReadOnlyList<string> Antivirus,
    int? SystemDriveFreePercent,
    int? RamLoadPercent);

public interface ISystemInfoSource
{
    SystemInfo Read();

    HealthFacts ReadHealth();
}

public static class HardwareNames
{
    /// <summary>Hersteller aus der PCI-Hersteller-ID im Geraetepfad (VEN_xxxx).</summary>
    public static string GpuVendor(string? pnpDeviceId, string name)
    {
        var id = pnpDeviceId?.ToUpperInvariant() ?? string.Empty;
        if (id.Contains("VEN_1002", StringComparison.Ordinal))
        {
            return "AMD";
        }

        if (id.Contains("VEN_10DE", StringComparison.Ordinal))
        {
            return "NVIDIA";
        }

        if (id.Contains("VEN_8086", StringComparison.Ordinal))
        {
            return "Intel";
        }

        return name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ? "Microsoft" : "-";
    }

    /// <summary>Speichertyp laut SMBIOS (Win32_PhysicalMemory.SMBIOSMemoryType); unbekannte Werte ergeben null.</summary>
    public static string? MemoryType(uint smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    /// <summary>MSFT_PhysicalDisk: BusType 17 ist NVMe, MediaType 3 HDD, 4 SSD.</summary>
    public static DiskKind DiskKind(ushort mediaType, ushort busType) => busType == 17
        ? Insights.DiskKind.Nvme
        : mediaType switch
        {
            3 => Insights.DiskKind.Hdd,
            4 => Insights.DiskKind.Ssd,
            _ => Insights.DiskKind.Unknown,
        };
}
