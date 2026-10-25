using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using AscendedTweaks.Engine.Insights;

namespace AscendedTweaks.Engine.Windows;

/// <summary>Processes of the current Windows session via System.Diagnostics.Process; closing uses CloseMainWindow (WM_CLOSE) or Kill.</summary>
[ExcludeFromCodeCoverage(Justification = "Thin wrapper around Process; grouping and protection live in BackgroundCleanup.")]
public sealed class WindowsRunningPrograms : IRunningPrograms
{
    public IReadOnlyList<RunningProcess> List()
    {
        int session;
        using (var self = Process.GetCurrentProcess())
        {
            session = self.SessionId;
        }

        var parents = ParentIds(session);
        var result = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session || process.Id == 0)
                    {
                        continue;
                    }

                    var path = PathOf(process);
                    result.Add(new RunningProcess(process.Id, process.ProcessName, path, DescriptionOf(path), process.MainWindowHandle != IntPtr.Zero, process.WorkingSet64, parents.GetValueOrDefault(process.Id)));
                }
                catch (InvalidOperationException)
                {
                    // The process ended while reading.
                }
            }
        }

        return result;
    }

    public bool HasWindow(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Parent process per process of the session (Win32_Process); empty when WMI is not readable.</summary>
    private static Dictionary<int, int> ParentIds(int session)
    {
        var result = new Dictionary<int, int>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher($"SELECT ProcessId, ParentProcessId FROM Win32_Process WHERE SessionId = {session}");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    result[Convert.ToInt32(item["ProcessId"], System.Globalization.CultureInfo.InvariantCulture)] =
                        Convert.ToInt32(item["ParentProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Without parents, helpers of kept programs are judged by name and folder only.
        }

        return result;
    }

    public bool RequestClose(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.CloseMainWindow();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return true;
        }
    }

    public void ForceClose(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already ended or not allowed; the caller counts what is still running.
        }
    }

    public bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static string? PathOf(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? DescriptionOf(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
