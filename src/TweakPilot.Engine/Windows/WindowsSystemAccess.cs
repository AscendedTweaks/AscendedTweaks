using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using TweakPilot.Core;

namespace TweakPilot.Engine.Windows;

/// <summary>Dienststeuerung ueber ServiceController und ChangeServiceConfig (Win32).</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne Win32-Huelle, abgedeckt durch VM-Integrationstests.")]
public sealed class WindowsServiceAccess : IServiceAccess
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceConfigDelayedAutoStartInfo = 3;

    public ServiceConfig? GetConfig(string serviceName)
    {
        using var controller = ServiceController.GetServices().FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        if (controller is null)
        {
            return null;
        }

        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{controller.ServiceName}");
        var delayed = key?.GetValue("DelayedAutostart") is int d && d == 1;
        var start = (ServiceStartType)(int)controller.StartType;
        return new ServiceConfig(start, start == ServiceStartType.Automatic && delayed, controller.Status == ServiceControllerStatus.Running);
    }

    public void SetStartType(string serviceName, ServiceStartType startType, bool delayedAutoStart)
    {
        var manager = NativeMethods.OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw Error("OpenSCManager");
        }

        try
        {
            var service = NativeMethods.OpenServiceW(manager, serviceName, ServiceChangeConfig | ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                throw Error("OpenService");
            }

            try
            {
                if (!NativeMethods.ChangeServiceConfigW(service, ServiceNoChange, (uint)startType, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                {
                    throw Error("ChangeServiceConfig");
                }

                var info = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = delayedAutoStart ? 1 : 0 };
                if (startType == ServiceStartType.Automatic
                    && !NativeMethods.ChangeServiceConfig2W(service, ServiceConfigDelayedAutoStartInfo, ref info))
                {
                    throw Error("ChangeServiceConfig2");
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    public void Stop(string serviceName, TimeSpan timeout)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status != ServiceControllerStatus.Stopped)
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
    }

    public void Start(string serviceName, TimeSpan timeout)
    {
        using var controller = new ServiceController(serviceName);
        if (controller.Status != ServiceControllerStatus.Running)
        {
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
    }

    private static TweakPilotException Error(string function)
    {
        var code = Marshal.GetLastWin32Error();
        var kind = code == 5 ? ErrorKind.PermissionDenied : ErrorKind.General;
        return new TweakPilotException(kind, $"{function} failed: {new Win32Exception(code).Message}");
    }
}

/// <summary>Energieplaene ueber die Power-Management-API (powrprof.dll).</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne Win32-Huelle, abgedeckt durch VM-Integrationstests.")]
public sealed class WindowsPowerPlanAccess : IPowerPlanAccess
{
    private const uint AccessScheme = 16;
    private const uint ErrorNoMoreItems = 259;

    public Guid GetActive()
    {
        Check(NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var pointer), "PowerGetActiveScheme");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            NativeMethods.LocalFree(pointer);
        }
    }

    public IReadOnlyList<Guid> ListSchemes()
    {
        var result = new List<Guid>();
        for (uint index = 0; ; index++)
        {
            var guid = Guid.Empty;
            var size = (uint)Marshal.SizeOf<Guid>();
            var code = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, ref guid, ref size);
            if (code == ErrorNoMoreItems)
            {
                return result;
            }

            Check(code, "PowerEnumerate");
            result.Add(guid);
        }
    }

    public void SetActive(Guid scheme) => Check(NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme), "PowerSetActiveScheme");

    public void Duplicate(Guid source, Guid destination)
    {
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        try
        {
            Marshal.StructureToPtr(destination, buffer, false);
            var pointer = buffer;
            Check(NativeMethods.PowerDuplicateScheme(IntPtr.Zero, ref source, ref pointer), "PowerDuplicateScheme");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Delete(Guid scheme) => Check(NativeMethods.PowerDeleteScheme(IntPtr.Zero, ref scheme), "PowerDeleteScheme");

    /// <summary>PowerReadACValueIndex; ERROR_FILE_NOT_FOUND (2) bedeutet, dass es die Einstellung auf dem Geraet nicht gibt.</summary>
    public uint? ReadAcValue(Guid scheme, Guid subgroup, Guid setting)
    {
        var code = NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value);
        if (code == 2)
        {
            return null;
        }

        Check(code, "PowerReadACValueIndex");
        return value;
    }

    /// <summary>PowerWriteACValueIndex; im aktiven Plan gilt der Wert erst nach PowerSetActiveScheme (Microsoft Learn).</summary>
    public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        Check(NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value), "PowerWriteACValueIndex");
        if (GetActive() == scheme)
        {
            SetActive(scheme);
        }
    }

    /// <summary>GetPwrCapabilities: SystemS4 (Byte 6) und HiberFilePresent (Byte 8) aus SYSTEM_POWER_CAPABILITIES.</summary>
    public bool? IsHibernationEnabled()
    {
        var capabilities = PowerCapabilities.Read();
        return capabilities is null || !capabilities.Value.SystemS4 ? null : capabilities.Value.HiberFilePresent;
    }

    private static void Check(uint code, string function)
    {
        if (code != 0)
        {
            var kind = code == 5 ? ErrorKind.PermissionDenied : ErrorKind.General;
            throw new TweakPilotException(kind, $"{function} failed: {new Win32Exception((int)code).Message}");
        }
    }
}

/// <summary>Startet Programme ohne Shell; Argumente werden einzeln uebergeben (Regel S8, Abschnitt 9.4).</summary>
[ExcludeFromCodeCoverage(Justification = "Duenne Prozess-Huelle, abgedeckt durch VM-Integrationstests.")]
public sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new TweakPilotException(ErrorKind.General, $"{executable} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TweakPilotException(ErrorKind.General, $"{executable} exceeded the time limit.");
        }

        return new CommandResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}

[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsSystemEnvironment : ISystemEnvironment
{
    public WindowsSystemEnvironment()
    {
        using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        Build = Environment.OSVersion.Version.Build;
        Ubr = key?.GetValue("UBR") is int ubr ? ubr : 0;
        Edition = (key?.GetValue("EditionID") as string) switch
        {
            "Core" or "CoreN" or "CoreSingleLanguage" or "CoreCountrySpecific" => WindowsEdition.Home,
            "Professional" or "ProfessionalN" or "ProfessionalWorkstation" or "ProfessionalEducation" => WindowsEdition.Pro,
            "Enterprise" or "EnterpriseN" or "EnterpriseS" or "EnterpriseSN" => WindowsEdition.Enterprise,
            "Education" or "EducationN" => WindowsEdition.Education,
            _ => null,
        };
        using var identity = WindowsIdentity.GetCurrent();
        IsElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public int Build { get; }

    public int Ubr { get; }

    public WindowsEdition? Edition { get; }

    public bool IsElevated { get; }

    public bool HasBattery { get; } = BatteryInfo.HasBattery();

    public string SystemDirectory => Environment.SystemDirectory;
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceDelayedAutoStartInfo
    {
        public int DelayedAutostart;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfigW(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig2W(IntPtr service, uint infoLevel, ref ServiceDelayedAutoStartInfo info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid, uint accessFlags, uint index, ref Guid buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerDuplicateScheme(IntPtr rootPowerKey, ref Guid sourceSchemeGuid, ref IntPtr destinationSchemeGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerDeleteScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettingsGuid, ref Guid powerSettingGuid, uint acValueIndex);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool GetPwrCapabilities(IntPtr capabilities);
}

/// <summary>Ausgewaehlte Felder aus SYSTEM_POWER_CAPABILITIES (dokumentierte Struktur, feste Byte-Positionen).</summary>
public readonly record struct PowerCapabilities(bool SystemS4, bool HiberFilePresent, bool BatteriesPresent)
{
    private const int SystemS4Offset = 6;
    private const int HiberFilePresentOffset = 8;
    private const int SystemBatteriesPresentOffset = 30;

    [ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
    public static PowerCapabilities? Read()
    {
        // Die Struktur ist kleiner als 128 Byte; der Puffer ist grosszuegig bemessen.
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            for (var i = 0; i < 256; i++)
            {
                Marshal.WriteByte(buffer, i, 0);
            }

            if (!NativeMethods.GetPwrCapabilities(buffer))
            {
                return null;
            }

            return new PowerCapabilities(
                Marshal.ReadByte(buffer, SystemS4Offset) != 0,
                Marshal.ReadByte(buffer, HiberFilePresentOffset) != 0,
                Marshal.ReadByte(buffer, SystemBatteriesPresentOffset) != 0);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
