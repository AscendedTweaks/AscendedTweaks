using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class BackgroundCleanupTests
{
    private const string Windows = @"C:\Windows";

    [Fact]
    public void Programs_are_grouped_biggest_first_and_protected_ones_are_marked()
    {
        RunningProcess[] processes =
        [
            new(1, "opera", @"C:\Users\x\AppData\Local\Programs\Opera\opera.exe", "Opera Internet Browser", true, 4_000),
            new(2, "opera", @"C:\Users\x\AppData\Local\Programs\Opera\opera.exe", null, false, 6_000),
            new(3, "Spotify", @"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe", "Spotify", true, 3_000),
            new(4, "explorer", @"C:\Windows\explorer.exe", "Windows Explorer", true, 9_000),
            new(5, "svchost", null, null, false, 8_000),
            new(6, "bduserhost", @"C:\Program Files\Bitdefender\bduserhost.exe", null, false, 500),
            new(7, "NordVPN", @"C:\Program Files\NordVPN\NordVPN.exe", "NordVPN", true, 700),
            new(8, "msedgewebview2", @"C:\Program Files (x86)\Microsoft\EdgeWebView\msedgewebview2.exe", null, false, 900),
            new(9, "RobloxCrashHandler", @"C:\Users\x\AppData\Local\Roblox\RobloxCrashHandler.exe", null, false, 100),
            new(10, "AscendedTweaks", @"C:\Users\x\Desktop\Ascended Tweaks\AscendedTweaks.exe", null, true, 200),
        ];

        var groups = BackgroundCleanup.Group(processes, Windows, selfId: 99);

        groups.Select(g => g.Name).Should().StartWith(["opera", "explorer", "svchost", "Spotify"], "sorted by memory");
        var opera = groups.Single(g => g.Name == "opera");
        opera.DisplayName.Should().Be("Opera Internet Browser");
        opera.Ids.Should().Equal(1, 2);
        opera.WorkingSetBytes.Should().Be(10_000);
        opera.HasWindow.Should().BeTrue();
        opera.Protection.Should().Be(ProgramProtection.None);
        groups.Single(g => g.Name == "explorer").Protection.Should().Be(ProgramProtection.Windows);
        groups.Single(g => g.Name == "svchost").Protection.Should().Be(ProgramProtection.Windows, "a path that cannot be read is treated as protected");
        groups.Single(g => g.Name == "bduserhost").Protection.Should().Be(ProgramProtection.Security);
        groups.Single(g => g.Name == "NordVPN").Protection.Should().Be(ProgramProtection.Security);
        groups.Single(g => g.Name == "msedgewebview2").Protection.Should().Be(ProgramProtection.Helper);
        groups.Single(g => g.Name == "RobloxCrashHandler").Protection.Should().Be(ProgramProtection.Helper);
        groups.Single(g => g.Name == "AscendedTweaks").Protection.Should().Be(ProgramProtection.Self);
    }

    [Fact]
    public async Task Closing_asks_politely_and_forces_only_when_told()
    {
        var fake = new FakePrograms(1, 2);
        var closer = new ProgramCloser(fake, _ => Task.CompletedTask);
        var program = new BackgroundProgram("game", "Game", [1, 2], 10, true, ProgramProtection.None);

        fake.EndOnRequest = false;
        (await closer.CloseAsync(program, force: false, TimeSpan.FromSeconds(1))).Should().Be(2, "the program did not close by itself");
        fake.Requested.Should().Equal(1, 2);
        fake.Forced.Should().BeEmpty();

        (await closer.CloseAsync(program, force: true, TimeSpan.FromSeconds(1))).Should().Be(0);
        fake.Forced.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Protected_programs_are_never_closed()
    {
        var fake = new FakePrograms(5);
        var closer = new ProgramCloser(fake, _ => Task.CompletedTask);

        var act = () => closer.CloseAsync(new BackgroundProgram("explorer", "Explorer", [5], 1, true, ProgramProtection.Windows), force: true, TimeSpan.Zero);

        await act.Should().ThrowAsync<AscendedTweaksException>();
        fake.Requested.Should().BeEmpty();
        fake.Forced.Should().BeEmpty();
    }

    private sealed class FakePrograms(params int[] running) : IRunningPrograms
    {
        private readonly HashSet<int> _running = [.. running];

        public bool EndOnRequest { get; set; } = true;

        public List<int> Requested { get; } = [];

        public List<int> Forced { get; } = [];

        public IReadOnlyList<RunningProcess> List() => [];

        public bool RequestClose(int id)
        {
            Requested.Add(id);
            if (EndOnRequest)
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
