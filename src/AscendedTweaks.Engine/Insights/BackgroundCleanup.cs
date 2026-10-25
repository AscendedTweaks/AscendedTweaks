using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Insights;

/// <summary>A process in the user's Windows session. Path is null when it cannot be read (elevated or protected).</summary>
public sealed record RunningProcess(int Id, string Name, string? Path, string? Description, bool HasWindow, long WorkingSetBytes, int ParentId = 0);

/// <summary>Why a program gets no close button.</summary>
public enum ProgramProtection
{
    None,

    /// <summary>Part of Windows (under the Windows folder) or not readable, for example an elevated process.</summary>
    Windows,

    /// <summary>Antivirus or VPN: closing it would lower protection.</summary>
    Security,

    /// <summary>Helper of another program (embedded browser, crash reporter): close the program itself instead.</summary>
    Helper,

    /// <summary>Ascended Tweaks itself.</summary>
    Self,
}

/// <summary>All processes of one program, grouped by name.</summary>
public sealed record BackgroundProgram(string Name, string DisplayName, IReadOnlyList<int> Ids, long WorkingSetBytes, bool HasWindow, ProgramProtection Protection);

/// <summary>Processes of the user's session; closing only ever affects processes the user picked.</summary>
public interface IRunningPrograms
{
    /// <summary>Processes in the current user's session, not the Windows services of session 0.</summary>
    IReadOnlyList<RunningProcess> List();

    /// <summary>Asks the program to close like its X button (it may ask to save); false when it has no window.</summary>
    bool RequestClose(int id);

    /// <summary>Ends the process immediately; unsaved data in it is lost.</summary>
    void ForceClose(int id);

    bool IsRunning(int id);

    /// <summary>True while the process shows a main window; a program that hid in the tray has none.</summary>
    bool HasWindow(int id) => List().FirstOrDefault(p => p.Id == id)?.HasWindow == true;
}

/// <summary>
/// "Clean up background": shows what runs in the user's session, biggest first, and closes a program only when the user
/// clicks it. Deliberately not a "RAM cleaner" (section 6.12): nothing is closed automatically, nothing is trimmed.
/// </summary>
public static class BackgroundCleanup
{
    /// <summary>Antivirus and VPN programs (process names, lower case, prefix match).</summary>
    private static readonly string[] SecurityPrefixes =
    [
        "bdagent", "bduserhost", "bdservicehost", "bdntwrk", "bdredline", "seccenter", "vsserv", "msmpeng", "mssense", "securityhealth",
        "nordvpn", "nordsec", "protonvpn", "expressvpn", "mullvad", "windscribe", "surfshark", "cyberghost", "openvpn", "wireguard",
        "mbam", "malwarebytes", "avast", "avg", "avp", "kaspersky", "egui", "ekrn", "norton", "mcafee", "mcshield", "sophos", "eset",
    ];

    /// <summary>Helpers that belong to another program; they end when that program closes.</summary>
    private static readonly string[] HelperNames =
    [
        "msedgewebview2", "cefsharp.browsersubprocess", "qtwebengineprocess", "crashpad_handler", "conhost",
    ];

    public static IReadOnlyList<BackgroundProgram> Group(IEnumerable<RunningProcess> processes, string windowsDirectory, int selfId)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list = g.ToList();
                var display = list.Select(p => p.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))?.Trim() ?? g.Key;
                return new BackgroundProgram(g.Key, display, list.Select(p => p.Id).ToList(), list.Sum(p => p.WorkingSetBytes), list.Any(p => p.HasWindow), Classify(list, windowsDirectory, selfId));
            })
            .OrderByDescending(p => p.WorkingSetBytes)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static ProgramProtection Classify(IReadOnlyCollection<RunningProcess> group, string windowsDirectory, int selfId)
    {
        ArgumentNullException.ThrowIfNull(group);
        var name = group.First().Name.ToLowerInvariant();
        if (group.Any(p => p.Id == selfId) || name.StartsWith("ascendedtweaks", StringComparison.Ordinal))
        {
            return ProgramProtection.Self;
        }

        if (SecurityPrefixes.Any(s => name.StartsWith(s, StringComparison.Ordinal)))
        {
            return ProgramProtection.Security;
        }

        if (group.Any(p => p.Path is null || IsUnder(p.Path, windowsDirectory)))
        {
            return ProgramProtection.Windows;
        }

        return HelperNames.Contains(name) || name.Contains("crashhandler", StringComparison.Ordinal) || name.Contains("crashreporter", StringComparison.Ordinal)
            ? ProgramProtection.Helper
            : ProgramProtection.None;
    }

    private static bool IsUnder(string path, string directory)
    {
        var root = directory.TrimEnd('\\') + "\\";
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Closes one picked program: cleanly first (like its X button), by force only when the user asks for it.</summary>
public sealed class ProgramCloser
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);
    private readonly IRunningPrograms _programs;
    private readonly Func<TimeSpan, Task> _delay;

    public ProgramCloser(IRunningPrograms programs, Func<TimeSpan, Task>? delay = null)
    {
        _programs = programs;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Returns how many processes of the program are still running after <paramref name="wait"/>.</summary>
    public async Task<int> CloseAsync(BackgroundProgram program, bool force, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.Protection != ProgramProtection.None)
        {
            throw new AscendedTweaksException(ErrorKind.General, $"{program.DisplayName} is protected and is not closed.");
        }

        foreach (var id in program.Ids)
        {
            if (force)
            {
                _programs.ForceClose(id);
            }
            else
            {
                _programs.RequestClose(id);
            }
        }

        for (var waited = TimeSpan.Zero; waited < wait && program.Ids.Any(_programs.IsRunning); waited += Step)
        {
            await _delay(Step).ConfigureAwait(false);
        }

        return program.Ids.Count(_programs.IsRunning);
    }
}
