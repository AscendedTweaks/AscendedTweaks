using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using TweakPilot.Engine.Tools;

namespace TweakPilot.Engine.Windows;

[ExcludeFromCodeCoverage(Justification = "Duenne Huelle um System.Net.NetworkInformation.Ping.")]
public sealed class WindowsPinger : IPinger
{
    public async Task<double?> PingAsync(IPAddress target, TimeSpan timeout, CancellationToken cancellation)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(target, timeout, null, null, cancellation).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException)
        {
            return null;
        }
    }
}

/// <summary>Feste Laufwerke mit Medientyp aus MSFT_Partition und MSFT_PhysicalDisk (ohne Administratorrechte lesbar).</summary>
[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsVolumeInventory : IVolumeInventory
{
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public IReadOnlyList<VolumeInfo> FixedVolumes()
    {
        var disks = new Dictionary<uint, (MediaKind Media, string? Model)>();
        var letters = new Dictionary<char, uint>();
        try
        {
            using (var searcher = new ManagementObjectSearcher(Storage, "SELECT DeviceId, MediaType, FriendlyName FROM MSFT_PhysicalDisk"))
            {
                foreach (var disk in searcher.Get())
                {
                    using (disk)
                    {
                        if (uint.TryParse(disk["DeviceId"] as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        {
                            var media = Convert.ToUInt16(disk["MediaType"], CultureInfo.InvariantCulture) switch
                            {
                                3 => MediaKind.Hdd,
                                4 => MediaKind.Ssd,
                                _ => MediaKind.Unknown,
                            };
                            disks[id] = (media, disk["FriendlyName"] as string);
                        }
                    }
                }
            }

            using (var searcher = new ManagementObjectSearcher(Storage, "SELECT DriveLetter, DiskNumber FROM MSFT_Partition"))
            {
                foreach (var partition in searcher.Get())
                {
                    using (partition)
                    {
                        var letter = Convert.ToChar(partition["DriveLetter"] ?? '\0', CultureInfo.InvariantCulture);
                        if (char.IsAsciiLetterUpper(letter))
                        {
                            letters[letter] = Convert.ToUInt32(partition["DiskNumber"], CultureInfo.InvariantCulture);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidCastException)
        {
            // Ohne Speicherverwaltung: Medientyp unbekannt, Defragmentieren bleibt dann gesperrt.
        }

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.Name.Length >= 2 && char.IsAsciiLetterUpper(d.Name[0]))
            .Select(d =>
            {
                var letter = d.Name[0];
                var info = letters.TryGetValue(letter, out var number) && disks.TryGetValue(number, out var disk) ? disk : (MediaKind.Unknown, null);
                return new VolumeInfo(letter, info.Item1, d.TotalSize, d.AvailableFreeSpace, info.Item2);
            })
            .ToList();
    }
}

/// <summary>SMART-Werte (DISK-003), nur Anzeige, nur mit Administratorrechten lesbar (laeuft im Helfer).</summary>
[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsDiskHealth : IDiskHealthSource
{
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public IReadOnlyList<DiskHealth> Read()
    {
        var result = new List<DiskHealth>();
        using var searcher = new ManagementObjectSearcher(Storage, "SELECT * FROM MSFT_PhysicalDisk");
        foreach (ManagementObject disk in searcher.Get())
        {
            using (disk)
            {
                var media = Convert.ToUInt16(disk["MediaType"], CultureInfo.InvariantCulture) switch { 3 => MediaKind.Hdd, 4 => MediaKind.Ssd, _ => MediaKind.Unknown };
                int? temperature = null, wear = null;
                long? hours = null, errors = null;
                foreach (ManagementObject counter in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                {
                    using (counter)
                    {
                        temperature = ToInt(counter["Temperature"]);
                        wear = ToInt(counter["Wear"]);
                        hours = ToLong(counter["PowerOnHours"]);
                        errors = ToLong(counter["ReadErrorsTotal"]);
                    }
                }

                result.Add(new DiskHealth(disk["FriendlyName"] as string ?? "?", media, temperature, wear, hours, errors, PredictFailure()));
            }
        }

        return result;
    }

    private static bool? PredictFailure()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            var any = false;
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    any |= item["PredictFailure"] is true;
                }
            }

            return any;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? ToInt(object? value) => value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static long? ToLong(object? value) => value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
}

[ExcludeFromCodeCoverage(Justification = "Reads only the registry.")]
public static class WindowsDoh
{
    /// <summary>Server, fuer die Windows eine DoH-Vorlage kennt (Funktionserkennung statt Versionsraten).</summary>
    public static IReadOnlyCollection<string> WellKnownServers()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohWellKnownServers");
        return key?.GetSubKeyNames() ?? [];
    }
}

/// <summary>Akku vorhanden (Notebook)? Fuer Profile mit "nur Desktop" (Abschnitt 6.10).</summary>
[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public static class BatteryInfo
{
    /// <summary>True, wenn ein Akku vorhanden ist; bei unlesbarem Zustand false.</summary>
    public static bool HasBattery() => GetSystemPowerStatus(out var status) && status.BatteryFlag != 128 && status.BatteryFlag != 255;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
