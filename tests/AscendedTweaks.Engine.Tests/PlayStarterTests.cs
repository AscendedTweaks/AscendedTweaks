using AscendedTweaks.Engine.Insights;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class PlayStarterTests
{
    private const string Windows = @"C:\Windows";

    private static readonly RunningProcess[] Processes =
    [
        new(1, "explorer", @"C:\Windows\explorer.exe", null, true, 1),
        new(2, "NordVPN", @"C:\Program Files\NordVPN\NordVPN.exe", null, true, 1),
        new(3, "opera", @"C:\Users\x\AppData\Local\Programs\Opera\opera.exe", null, true, 1),
        new(4, "opera", @"C:\Users\x\AppData\Local\Programs\Opera\opera.exe", null, false, 1, ParentId: 3),
        new(5, "StreamDeck", @"C:\Program Files\Elgato\StreamDeck\StreamDeck.exe", null, true, 1),
        new(6, "node", @"C:\Users\x\AppData\Roaming\Elgato\StreamDeck\Plugins\x\node.exe", null, false, 1, ParentId: 5),
        new(7, "EpicGamesLauncher", @"C:\Program Files (x86)\Epic Games\Launcher\EpicGamesLauncher.exe", null, true, 1),
        new(8, "FortniteClient-Win64-Shipping", @"D:\Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", null, true, 1),
        new(9, "Claude", @"C:\Users\x\AppData\Local\AnthropicClaude\Claude.exe", null, true, 1),
        new(10, "OneDrive", @"C:\Users\x\AppData\Local\Microsoft\OneDrive\OneDrive.exe", null, false, 1),
        new(11, "AscendedTweaks.App", @"C:\Tools\AscendedTweaks.App.exe", null, true, 1),
    ];

    private static readonly PlayPreset Preset = new()
    {
        Keep = ["StreamDeck"],
        GameName = "Fortnite",
        GameLaunch = "com.epicgames.launcher://apps/fortnite?action=launch",
        GameFolder = @"D:\Games\Fortnite\",
        Music = MusicMode.SpotifySearch,
        MusicSearch = "Popular The Weeknd",
    };

    [Fact]
    public void Only_programs_outside_the_keep_list_are_closed()
    {
        var close = PlayStarter.ToClose(Processes, Preset, Windows, selfId: 11);

        close.Select(p => p.Id).Should().BeEquivalentTo([3, 4, 9, 10],
            "Windows, the VPN, the kept Stream Deck with its plugin, the launcher, the game and this app keep running");
    }

    [Fact]
    public void The_empty_default_still_keeps_the_fixed_entries()
    {
        var close = PlayStarter.ToClose(Processes, new PlayPreset(), Windows, selfId: 11);

        close.Select(p => p.Name).Should().NotContain(["explorer", "NordVPN", "EpicGamesLauncher", "AscendedTweaks.App"]);
        close.Select(p => p.Name).Should().Contain(["StreamDeck", "FortniteClient-Win64-Shipping"], "without a preset they are ordinary programs");
    }

    [Fact]
    public async Task Windowed_programs_that_refuse_stay_and_windowless_ones_are_ended()
    {
        var programs = new FakePrograms(Processes) { Refuses = { 9 } };
        var launcher = new FakeLauncher();
        var starter = new PlayStarter(programs, launcher, Windows, 11, _ => Task.CompletedTask);

        var result = await starter.RunAsync(Preset);

        programs.Requested.Should().BeEquivalentTo([3, 9], "only programs with a window are asked");
        programs.Forced.Should().BeEquivalentTo([4, 10], "the refusing Claude window is not forced");
        result.StillRunning.Should().Equal("Claude");
        result.Closed.Should().BeEquivalentTo(["opera", "OneDrive"]);
        launcher.Opened.Should().Equal("spotify:search:Popular%20The%20Weeknd", "com.epicgames.launcher://apps/fortnite?action=launch");
        result.MusicError.Should().BeNull();
        result.GameError.Should().BeNull();
    }

    [Fact]
    public async Task A_music_command_is_started_and_a_failing_start_is_reported()
    {
        var launcher = new FakeLauncher { FailOpen = true };
        var starter = new PlayStarter(new FakePrograms([]), launcher, Windows, 11, _ => Task.CompletedTask);

        var result = await starter.RunAsync(Preset with { Music = MusicMode.Command, MusicCommand = " pythonw ", MusicArguments = "cli.py play \"Popular\"" });

        launcher.Started.Should().Equal("pythonw|cli.py play \"Popular\"");
        result.GameError.Should().Be("no handler");
    }

    private sealed class FakeLauncher : IProgramLauncher
    {
        public bool FailOpen { get; init; }

        public List<string> Opened { get; } = [];

        public List<string> Started { get; } = [];

        public void Open(string target)
        {
            if (FailOpen)
            {
                throw new InvalidOperationException("no handler");
            }

            Opened.Add(target);
        }

        public void Start(string program, string arguments) => Started.Add(program + "|" + arguments);
    }

    private sealed class FakePrograms(RunningProcess[] processes) : IRunningPrograms
    {
        private readonly HashSet<int> _running = [.. processes.Select(p => p.Id)];

        public HashSet<int> Refuses { get; } = [];

        public List<int> Requested { get; } = [];

        public List<int> Forced { get; } = [];

        public IReadOnlyList<RunningProcess> List() => processes;

        public bool HasWindow(int id) => processes.Single(p => p.Id == id).HasWindow;

        public bool RequestClose(int id)
        {
            Requested.Add(id);
            if (!Refuses.Contains(id))
            {
                _running.Remove(id);
            }

            return true;
        }

        public void ForceClose(int id)
        {
            Forced.Add(id);
            _running.Remove(id);
        }

        public bool IsRunning(int id) => _running.Contains(id);
    }
}
