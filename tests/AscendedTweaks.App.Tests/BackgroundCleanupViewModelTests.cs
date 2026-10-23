using AscendedTweaks.App.Services;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class BackgroundCleanupViewModelTests
{
    [Fact]
    public async Task Only_closable_programs_get_a_button_and_nothing_closes_by_itself()
    {
        using var formats = new TestFormats("en-US");
        var programs = new Programs();
        var vm = Create(programs, _ => true);

        await vm.LoadAsync();

        vm.Rows.Select(r => r.Name).Should().Equal("Web browser", "Launcher");
        vm.Rows[0].Detail.Should().Be("2 processes · 2.0 GB");
        vm.Rows[0].CloseText.Should().Be("Close");
        vm.Rows[1].CloseText.Should().Be("Force close", "no window to ask");
        vm.Summary.Should().Be("3 programs with 4 processes use 2.4 GB.");
        vm.ProtectedText.Should().StartWith("1 of them are protected");
        programs.Requested.Should().BeEmpty();
        programs.Forced.Should().BeEmpty();
    }

    [Fact]
    public async Task A_program_without_a_window_is_forced_only_after_a_yes()
    {
        var programs = new Programs();
        var asked = 0;
        var vm = Create(programs, _ => { asked++; return false; });
        await vm.LoadAsync();

        await vm.CloseCommand.ExecuteAsync(vm.Rows.Single(r => r.Name == "Launcher"));

        asked.Should().Be(1);
        programs.Forced.Should().BeEmpty("the user said no");
    }

    [Fact]
    public async Task A_program_that_stays_open_is_forced_after_a_yes()
    {
        var programs = new Programs { EndOnRequest = false };
        var vm = Create(programs, _ => true);
        await vm.LoadAsync();

        await vm.CloseCommand.ExecuteAsync(vm.Rows.Single(r => r.Name == "Web browser"));

        programs.Requested.Should().Equal(1, 2);
        programs.Forced.Should().Equal(1, 2);
    }

    private static BackgroundCleanupViewModel Create(Programs programs, Func<DialogViewModel, object?> answer) =>
        new(programs, new ScriptedDialogs(answer), new Localizer(Localizer.English), @"C:\Windows", 99, _ => Task.CompletedTask);

    private sealed class Programs : IRunningPrograms
    {
        private readonly HashSet<int> _running = [1, 2, 3, 4];

        public bool EndOnRequest { get; set; } = true;

        public List<int> Requested { get; } = [];

        public List<int> Forced { get; } = [];

        public IReadOnlyList<RunningProcess> List() =>
        [
            new(1, "browser", @"C:\Apps\browser.exe", "Web browser", true, 1L << 30),
            new(2, "browser", @"C:\Apps\browser.exe", null, false, 1L << 30),
            new(3, "launcher", @"C:\Apps\launcher.exe", "Launcher", false, 300L << 20),
            new(4, "explorer", @"C:\Windows\explorer.exe", "Windows Explorer", true, 100L << 20),
        ];

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
