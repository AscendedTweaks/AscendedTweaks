using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Insights;

/// <summary>How "Ready to play" starts music.</summary>
public enum MusicMode
{
    None,

    /// <summary>Opens Spotify on a search (spotify:search:...); the user presses play. Needs no setup.</summary>
    SpotifySearch,

    /// <summary>Runs a program the user set up, for example a Spotify command line, with fixed arguments.</summary>
    Command,
}

/// <summary>
/// "Ready to play": which of the user's programs keep running, which game starts and which music plays. Stored per user;
/// the default keeps nothing extra, plays nothing and starts no game.
/// </summary>
public sealed record PlayPreset
{
    /// <summary>Programs the user keeps running, by process name without ".exe" (for example "Spotify", "StreamDeck").</summary>
    public List<string> Keep { get; init; } = [];

    public string? GameName { get; init; }

    /// <summary>Launch link or program of the game (for example com.epicgames.launcher://apps/...).</summary>
    public string? GameLaunch { get; init; }

    /// <summary>Install folder of the game; its processes always keep running.</summary>
    public string? GameFolder { get; init; }

    public MusicMode Music { get; init; }

    /// <summary>Search text for <see cref="MusicMode.SpotifySearch"/>.</summary>
    public string? MusicSearch { get; init; }

    /// <summary>Program for <see cref="MusicMode.Command"/> (a name on PATH such as "pythonw", or a full path).</summary>
    public string? MusicCommand { get; init; }

    public string? MusicArguments { get; init; }
}

/// <summary>Starts programs and links for "Ready to play"; nothing is downloaded, only what the user set up runs.</summary>
public interface IProgramLauncher
{
    /// <summary>Opens a link or file with its Windows default (game launch links, spotify: links).</summary>
    void Open(string target);

    /// <summary>Starts a program with arguments, without waiting for it.</summary>
    void Start(string program, string arguments);
}

public sealed record PlayResult(IReadOnlyList<string> Closed, IReadOnlyList<string> StillRunning, string? MusicError, string? GameError);

/// <summary>
/// "Ready to play" in one click: closes the user's other programs, then starts the music and the game. What always keeps
/// running cannot be removed from the preset: Windows, antivirus and VPN, graphics and audio drivers, anti-cheat, game
/// launchers, the game itself and Ascended Tweaks. Programs with a window are only asked to close; one that refuses (for
/// example because it asks to save) keeps running and is reported. Only windowless processes are ended by force.
/// </summary>
public sealed class PlayStarter
{
    /// <summary>Always kept (process names, lower case, prefix match): driver helpers, anti-cheat, game launchers.</summary>
    public static readonly IReadOnlyList<string> FixedKeepPrefixes =
    [
        "amdrs", "amdadlx", "amdow", "atieclxx", "cncmd", "radeonsoftware", "nvcontainer", "nvdisplay", "nvsphelper", "nvidia", "igfx", "intelgraphics",
        "rtkaud", "realtek", "nahimic",
        "easyanticheat", "beservice", "battleye", "vgc", "vgtray", "faceit", "eac",
        "epicgameslauncher", "epicwebhelper", "epiconlineservices", "eosoverlay", "steam", "gameoverlayui", "battle.net", "eadesktop", "eabackgroundservice",
        "ubisoftconnect", "upc", "riotclient", "gog",
    ];

    public static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);

    private readonly IRunningPrograms _programs;
    private readonly IProgramLauncher _launcher;
    private readonly string _windowsDirectory;
    private readonly int _selfId;
    private readonly Func<TimeSpan, Task> _delay;

    public PlayStarter(IRunningPrograms programs, IProgramLauncher launcher, string windowsDirectory, int selfId, Func<TimeSpan, Task>? delay = null)
    {
        _programs = programs;
        _launcher = launcher;
        _windowsDirectory = windowsDirectory;
        _selfId = selfId;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Processes "Ready to play" would close; everything else keeps running.</summary>
    public static IReadOnlyList<RunningProcess> ToClose(IReadOnlyList<RunningProcess> processes, PlayPreset preset, string windowsDirectory, int selfId)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(preset);
        var keepNames = preset.Keep.Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var kept = new HashSet<int>();
        foreach (var group in processes.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var list = group.ToList();
            var name = group.Key.ToLowerInvariant();
            var keep = BackgroundCleanup.Classify(list, windowsDirectory, selfId) != ProgramProtection.None
                || keepNames.Contains(name)
                || FixedKeepPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                || (preset.GameFolder is { Length: > 0 } folder && list.Any(p => p.Path?.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) == true));
            if (keep)
            {
                kept.UnionWith(list.Select(p => p.Id));
            }
        }

        // Helpers started by a kept program keep running too (Stream Deck plugins, Spotify and launcher helpers).
        var children = processes.Where(p => p.ParentId != 0).ToLookup(p => p.ParentId);
        var queue = new Queue<int>(kept);
        while (queue.Count > 0)
        {
            foreach (var child in children[queue.Dequeue()])
            {
                if (kept.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return processes.Where(p => !kept.Contains(p.Id)).ToList();
    }

    public async Task<PlayResult> RunAsync(PlayPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var close = ToClose(_programs.List(), preset, _windowsDirectory, _selfId);
        foreach (var process in close.Where(p => p.HasWindow))
        {
            _programs.RequestClose(process.Id);
        }

        for (var waited = TimeSpan.Zero; waited < CloseWait && close.Any(p => _programs.IsRunning(p.Id)); waited += TimeSpan.FromMilliseconds(250))
        {
            await _delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        // What still shows a window may be waiting for the user (unsaved work): it stays. Windowless rest is ended.
        foreach (var process in close.Where(p => _programs.IsRunning(p.Id) && !_programs.HasWindow(p.Id)))
        {
            _programs.ForceClose(process.Id);
        }

        var still = close.Where(p => _programs.IsRunning(p.Id)).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var closed = close.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Except(still, StringComparer.OrdinalIgnoreCase).ToList();

        string? musicError = null;
        try
        {
            switch (preset.Music)
            {
                case MusicMode.SpotifySearch when !string.IsNullOrWhiteSpace(preset.MusicSearch):
                    _launcher.Open("spotify:search:" + Uri.EscapeDataString(preset.MusicSearch.Trim()));
                    break;
                case MusicMode.Command when !string.IsNullOrWhiteSpace(preset.MusicCommand):
                    _launcher.Start(preset.MusicCommand.Trim(), preset.MusicArguments ?? string.Empty);
                    break;
            }
        }
        catch (Exception ex) when (ex is AscendedTweaksException or InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            musicError = ex.Message;
        }

        string? gameError = null;
        if (!string.IsNullOrWhiteSpace(preset.GameLaunch))
        {
            try
            {
                _launcher.Open(preset.GameLaunch.Trim());
            }
            catch (Exception ex) when (ex is AscendedTweaksException or InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                gameError = ex.Message;
            }
        }

        return new PlayResult(closed, still, musicError, gameError);
    }
}
