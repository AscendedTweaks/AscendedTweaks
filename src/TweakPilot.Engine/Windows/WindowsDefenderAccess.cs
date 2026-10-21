using System.Diagnostics.CodeAnalysis;
using System.Management;
using TweakPilot.Core;

namespace TweakPilot.Engine.Windows;

/// <summary>
/// DEFN-001: nur ScanAvgCPULoadFactor ueber die Defender-WMI-Klasse MSFT_MpPreference (Namespace root\Microsoft\Windows\Defender,
/// Methode Set; dieselbe Schnittstelle wie Set-MpPreference). Ohne PowerShell, damit keine Ausfuehrungsrichtlinie noetig ist.
/// Legt eine Gruppenrichtlinie den Wert fest, gilt die Einstellung als nicht verfuegbar.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin WMI wrapper; value checks and flow live in CommandWhitelist and CommandExecutor.")]
public sealed class WindowsDefenderAccess : IDefenderAccess
{
    private const string Namespace = @"root\Microsoft\Windows\Defender";
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows Defender\Scan";
    private const string PolicyValue = "AvgCPULoadFactor";
    private readonly IRegistryAccess _registry;

    public WindowsDefenderAccess(IRegistryAccess registry) => _registry = registry;

    public int? GetScanAvgCpuLoad()
    {
        if (_registry.GetValue(RegHive.HKLM, PolicyPath, PolicyValue) is not null)
        {
            return null;
        }

        try
        {
            // Laeuft ein anderer Virenschutz, ist Defender "Not running" oder passiv: die Einstellung ist dann lesbar, aber wirkungslos.
            using (var status = new ManagementObjectSearcher(Namespace, "SELECT AMRunningMode FROM MSFT_MpComputerStatus"))
            {
                foreach (var item in status.Get())
                {
                    using (item)
                    {
                        if (!string.Equals(item["AMRunningMode"] as string, "Normal", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }
                    }
                }
            }

            using var searcher = new ManagementObjectSearcher(Namespace, "SELECT ScanAvgCPULoadFactor FROM MSFT_MpPreference");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    return item["ScanAvgCPULoadFactor"] is byte value ? value : null;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }

        return null;
    }

    public void SetScanAvgCpuLoad(int percent)
    {
        if (!CommandWhitelist.IsValidScanLoad(percent.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            throw new TweakPilotException(ErrorKind.CatalogInvalid, $"Invalid scan load {percent}.");
        }

        try
        {
            using var preference = new ManagementClass(Namespace, "MSFT_MpPreference", null);
            using var input = preference.GetMethodParameters("Set");
            input["ScanAvgCPULoadFactor"] = (byte)percent;
            using var output = preference.InvokeMethod("Set", input, null);
            if (output?["ReturnValue"] is uint code && code != 0)
            {
                throw new TweakPilotException(
                    code == 5 ? ErrorKind.PermissionDenied : ErrorKind.General,
                    $"MSFT_MpPreference.Set exited with code {code}.");
            }
        }
        catch (ManagementException ex)
        {
            throw new TweakPilotException(ErrorKind.General, $"MSFT_MpPreference.Set failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new TweakPilotException(ErrorKind.PermissionDenied, $"MSFT_MpPreference.Set: {ex.Message}");
        }
    }
}
