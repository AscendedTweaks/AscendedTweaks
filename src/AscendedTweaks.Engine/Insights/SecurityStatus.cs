using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AscendedTweaks.Engine.Insights;

public enum CheckState
{
    Good,
    Attention,
    Info,
    Unknown,
}

/// <summary>Rohwerte der Anti-Cheat-Pruefung; null bedeutet "nicht lesbar".</summary>
public sealed record SecurityFacts(bool? SecureBoot, int? TpmVersion, bool? VbsRunning, bool? MemoryIntegrityRunning, bool? TestSigning, bool? KernelDebugging);

public interface ISecurityFactsSource
{
    SecurityFacts Read();
}

public sealed record SecurityCheck(string Id, CheckState State, string ValueKey, string ExplanationKey);

/// <summary>
/// Anti-Cheat-Kompatibilitaetspruefung (GAME-020), nur Anzeige. Aussagen bleiben allgemein
/// ("manche Anti-Cheat-Systeme ..."), weil Anforderungen einzelner Spiele nur mit belegter Quelle genannt werden duerfen.
/// </summary>
public static class AntiCheatCheck
{
    public static IReadOnlyList<SecurityCheck> Evaluate(SecurityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return
        [
            new("secureboot", facts.SecureBoot switch { true => CheckState.Good, false => CheckState.Attention, null => CheckState.Unknown }, OnOff(facts.SecureBoot), "anticheat.explain.secureboot"),
            new("tpm", facts.TpmVersion switch { >= 2 => CheckState.Good, 1 => CheckState.Attention, 0 => CheckState.Attention, _ => CheckState.Unknown },
                facts.TpmVersion switch { >= 2 => "anticheat.value.tpm2", 1 => "anticheat.value.tpm12", 0 => "anticheat.value.none", _ => "anticheat.value.unknown" }, "anticheat.explain.tpm"),
            new("hvci", facts.MemoryIntegrityRunning is null ? CheckState.Unknown : CheckState.Info, OnOff(facts.MemoryIntegrityRunning), "anticheat.explain.hvci"),
            new("vbs", facts.VbsRunning is null ? CheckState.Unknown : CheckState.Info, OnOff(facts.VbsRunning), "anticheat.explain.vbs"),
            new("testsigning", facts.TestSigning switch { false => CheckState.Good, true => CheckState.Attention, null => CheckState.Unknown }, OnOff(facts.TestSigning), "anticheat.explain.testsigning"),
            new("kerneldebug", facts.KernelDebugging switch { false => CheckState.Good, true => CheckState.Attention, null => CheckState.Unknown }, OnOff(facts.KernelDebugging), "anticheat.explain.kerneldebug"),
        ];
    }

    private static string OnOff(bool? value) => value switch
    {
        true => "anticheat.value.on",
        false => "anticheat.value.off",
        null => "anticheat.value.unknown",
    };
}

/// <summary>Liest die Fakten ohne Administratorrechte: Registry, TBS, WMI (DeviceGuard) und NtQuerySystemInformation.</summary>
[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsSecurityFacts : ISecurityFactsSource
{
    private const int SystemKernelDebuggerInformation = 35;
    private const int SystemCodeIntegrityInformation = 103;
    private const uint CodeIntegrityTestSign = 0x02;

    public SecurityFacts Read() => new(SecureBoot(), TpmVersion(), Vbs(out var hvci), hvci, TestSigning(), KernelDebugging());

    private static bool? SecureBoot()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return key?.GetValue("UEFISecureBootEnabled") is int value ? value == 1 : null;
    }

    private static int? TpmVersion()
    {
        try
        {
            var info = new TpmDeviceInfo();
            var result = Tbsi_GetDeviceInfo((uint)Marshal.SizeOf<TpmDeviceInfo>(), ref info);
            return result switch
            {
                0 => info.TpmVersion == 2 ? 2 : 1,
                0x8028400F => 0, // TBS_E_TPM_NOT_FOUND
                _ => null,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool? Vbs(out bool? hvci)
    {
        hvci = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var status = Convert.ToInt32(item["VirtualizationBasedSecurityStatus"], System.Globalization.CultureInfo.InvariantCulture);
                    var running = item["SecurityServicesRunning"] as uint[] ?? [];
                    hvci = running.Contains(2u);
                    return status == 2;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return null;
        }

        return null;
    }

    private static bool? TestSigning()
    {
        var info = new CodeIntegrityInformation { Length = (uint)Marshal.SizeOf<CodeIntegrityInformation>() };
        return NtQuerySystemInformation(SystemCodeIntegrityInformation, ref info, info.Length, out _) == 0
            ? (info.CodeIntegrityOptions & CodeIntegrityTestSign) != 0
            : null;
    }

    private static bool? KernelDebugging()
    {
        var info = new KernelDebuggerInformation();
        return NtQuerySystemInformation(SystemKernelDebuggerInformation, ref info, (uint)Marshal.SizeOf<KernelDebuggerInformation>(), out _) == 0
            ? info.Enabled != 0 && info.NotPresent == 0
            : null;
    }

    [DllImport("tbs.dll")]
    private static extern uint Tbsi_GetDeviceInfo(uint size, ref TpmDeviceInfo info);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, ref CodeIntegrityInformation info, uint length, out uint returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, ref KernelDebuggerInformation info, uint length, out uint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CodeIntegrityInformation
    {
        public uint Length;
        public uint CodeIntegrityOptions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KernelDebuggerInformation
    {
        public byte Enabled;
        public byte NotPresent;
    }
}
