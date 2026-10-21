using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using TweakPilot.Core;

namespace TweakPilot.Engine.Windows;

/// <summary>
/// Netzwerk-Einstellungen physischer Adapter: Lesen ueber Registry, WMI und CIM, Schreiben der DNS-Server ueber
/// netsh (dokumentiert, Argumente einzeln) und des Energiesparens ueber MSPower_DeviceEnable.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around Windows interfaces; the state logic lives in AdapterState and CommandExecutor.")]
public sealed class WindowsNetworkAccess : INetworkAccess
{
    private const string Tcpip4 = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\";
    private const string Tcpip6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ICommandRunner _runner;
    private readonly string _netsh;

    public WindowsNetworkAccess(ICommandRunner runner, string systemDirectory)
    {
        _runner = runner;
        _netsh = Path.Combine(systemDirectory, "netsh.exe");
    }

    public IReadOnlyList<string> PhysicalAdapters()
    {
        using var searcher = new ManagementObjectSearcher("SELECT GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND NetEnabled = TRUE");
        var result = new List<string>();
        foreach (var item in searcher.Get())
        {
            using (item)
            {
                if (item["GUID"] is string guid && AdapterState.IsAdapterId(guid))
                {
                    result.Add(guid.ToUpperInvariant());
                }
            }
        }

        return result.OrderBy(g => g, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> GetDnsServers(string adapter)
    {
        Guard(adapter);
        return Read(Tcpip4 + adapter).Concat(Read(Tcpip6 + adapter)).ToList();
    }

    public void SetDnsServers(string adapter, IReadOnlyList<string> servers)
    {
        Guard(adapter);
        var addresses = servers.Select(s => IPAddress.Parse(s)).ToList();
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => string.Equals(n.Id, adapter, StringComparison.OrdinalIgnoreCase))
            ?? throw new TweakPilotException(ErrorKind.Unavailable, $"Adapter {adapter} not found.");
        var properties = nic.GetIPProperties();
        Apply("ipv4", properties.GetIPv4Properties()?.Index, addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList());
        if (nic.Supports(NetworkInterfaceComponent.IPv6))
        {
            Apply("ipv6", properties.GetIPv6Properties()?.Index, addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList());
        }
    }

    public string? GetAutotuning()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT AutoTuningLevelLocal FROM MSFT_NetTCPSetting WHERE SettingName = 'Internet'");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    return Convert.ToInt32(item["AutoTuningLevelLocal"], CultureInfo.InvariantCulture) switch
                    {
                        0 => "disabled",
                        1 => "highlyrestricted",
                        2 => "restricted",
                        3 => "normal",
                        4 => "experimental",
                        _ => null,
                    };
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidCastException)
        {
            return null;
        }

        return null;
    }

    public bool? GetPowerSaving(string adapter)
    {
        using var device = PowerObject(adapter);
        return device?["Enable"] is bool enabled ? enabled : null;
    }

    public void SetPowerSaving(string adapter, bool enabled)
    {
        using var device = PowerObject(adapter) ?? throw new TweakPilotException(ErrorKind.Unavailable, $"Power management for adapter {adapter} is not readable.");
        device["Enable"] = enabled;
        device.Put();
    }

    /// <summary>MSFT_NetAdapterAdvancedPropertySettingData mit RegistryKeyword *EEE (Werte 0/1, Microsoft Learn: standardisierte INF-Schluesselwoerter).</summary>
    public string? GetEnergyEfficientEthernet(string adapter)
    {
        using var property = EeeObject(adapter);
        return (property?["RegistryValue"] as string[]) switch
        {
        ["0"] => "off",
        ["1"] => "on",
            _ => null,
        };
    }

    /// <summary>Aendert die Eigenschaft wie Set-NetAdapterAdvancedProperty (CIM ModifyInstance); der Adapter verbindet sich dabei kurz neu.</summary>
    public void SetEnergyEfficientEthernet(string adapter, bool enabled)
    {
        using var property = EeeObject(adapter) ?? throw new TweakPilotException(ErrorKind.Unavailable, $"Adapter {adapter} has no *EEE property.");
        property["RegistryValue"] = new[] { enabled ? "1" : "0" };
        property.Put();
    }

    private static ManagementObject? EeeObject(string adapter)
    {
        Guard(adapter);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", $"SELECT * FROM MSFT_NetAdapterAdvancedPropertySettingData WHERE InstanceID = '{adapter}::*EEE'");
            foreach (ManagementObject item in searcher.Get())
            {
                return item;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static IReadOnlyList<string> Read(string key)
    {
        using var reg = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(key);
        return reg?.GetValue("NameServer") is string text
            ? text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    private static void Guard(string adapter)
    {
        if (!AdapterState.IsAdapterId(adapter))
        {
            throw new TweakPilotException(ErrorKind.CatalogInvalid, $"Invalid adapter ID {adapter}.");
        }
    }

    /// <summary>netsh interface ipv4|ipv6 set dnsservers ... und add dnsservers fuer weitere Adressen; leere Liste = DHCP.</summary>
    private void Apply(string family, int? index, IReadOnlyList<IPAddress> addresses)
    {
        if (index is not { } i)
        {
            return;
        }

        var name = $"name={i.ToString(CultureInfo.InvariantCulture)}";
        if (addresses.Count == 0)
        {
            Run(["interface", family, "set", "dnsservers", name, "source=dhcp"]);
            return;
        }

        Run(["interface", family, "set", "dnsservers", name, "source=static", $"address={addresses[0]}", "register=primary", "validate=no"]);
        for (var n = 1; n < addresses.Count; n++)
        {
            Run(["interface", family, "add", "dnsservers", name, $"address={addresses[n]}", $"index={(n + 1).ToString(CultureInfo.InvariantCulture)}", "validate=no"]);
        }
    }

    private void Run(IReadOnlyList<string> arguments)
    {
        var result = _runner.Run(_netsh, arguments, Timeout);
        if (result.ExitCode != 0)
        {
            throw new TweakPilotException(
                result.ExitCode == 5 ? ErrorKind.PermissionDenied : ErrorKind.General,
                $"netsh exited with code {result.ExitCode}: {(result.StandardOutput + result.StandardError).Trim()}");
        }
    }

    private static ManagementObject? PowerObject(string adapter)
    {
        Guard(adapter);
        string? pnp = null;
        using (var searcher = new ManagementObjectSearcher($"SELECT PNPDeviceID FROM Win32_NetworkAdapter WHERE GUID = '{adapter}'"))
        {
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    pnp = item["PNPDeviceID"] as string;
                }
            }
        }

        if (pnp is null)
        {
            return null;
        }

        try
        {
            using var power = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSPower_DeviceEnable");
            foreach (ManagementObject item in power.Get())
            {
                if (item["InstanceName"] is string instance && instance.StartsWith(pnp, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                item.Dispose();
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
