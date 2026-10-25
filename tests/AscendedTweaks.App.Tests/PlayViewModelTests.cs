using System.IO;
using AscendedTweaks.App.ViewModels;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class PlayViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "AscendedTweaks.test.play." + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void A_missing_or_broken_file_is_the_empty_default()
    {
        var store = new PlayPresetStore(_path);
        store.Load().Should().BeEquivalentTo(new PlayPreset());

        File.WriteAllText(_path, "{ not json");
        store.Load().Should().BeEquivalentTo(new PlayPreset());
    }

    [Fact]
    public void Changes_are_saved_and_loaded_again()
    {
        var vm = Create();
        vm.Keep.Should().BeEmpty();
        vm.SelectedGame!.Launch.Should().BeNull("no game by default");

        vm.NewKeep = "StreamDeck.exe";
        vm.AddKeepCommand.Execute(null);
        vm.NewKeep = "streamdeck";
        vm.AddKeepCommand.Execute(null);
        vm.SelectedGame = vm.Games.Single(g => g.Name == "Fortnite");
        vm.SelectedMusic = vm.MusicOptions.Single(m => m.Mode == MusicMode.Command);
        vm.MusicCommand = "pythonw";
        vm.MusicArguments = "cli.py play \"Popular\"";

        var loaded = new PlayPresetStore(_path).Load();
        loaded.Keep.Should().Equal("StreamDeck");
        loaded.GameName.Should().Be("Fortnite");
        loaded.GameLaunch.Should().Be("com.epicgames.launcher://fortnite");
        loaded.Music.Should().Be(MusicMode.Command);
        loaded.MusicArguments.Should().Be("cli.py play \"Popular\"");

        var again = Create();
        again.Keep.Should().Equal("StreamDeck");
        again.SelectedGame!.Name.Should().Be("Fortnite");
        again.IsCommandMode.Should().BeTrue();
        again.Summary.Should().Be("Closes your other programs (except 1 you keep) · starts Fortnite · music: your command");
    }

    [Fact]
    public void Suggestions_list_what_would_be_closed()
    {
        var vm = Create();

        vm.ToggleEditingCommand.Execute(null);

        vm.Suggestions.Should().Equal("opera");
        vm.FixedKeep.Should().HaveCount(7);
    }

    private PlayViewModel Create()
    {
        var programs = new Programs();
        var launcher = new Launcher();
        return new PlayViewModel(
            new PlayStarter(programs, launcher, @"C:\Windows", 99, _ => Task.CompletedTask),
            new PlayPresetStore(_path),
            launcher,
            () => [new DetectedGame("Fortnite", GameSource.Epic, @"D:\Games\Fortnite", null, "com.epicgames.launcher://fortnite", AntiCheat.EasyAntiCheat)],
            programs.List,
            new Localizer(Localizer.English),
            @"C:\Windows",
            99);
    }

    private sealed class Launcher : IProgramLauncher
    {
        public void Open(string target)
        {
        }

        public void Start(string program, string arguments)
        {
        }
    }

    private sealed class Programs : IRunningPrograms
    {
        public IReadOnlyList<RunningProcess> List() =>
        [
            new(1, "explorer", @"C:\Windows\explorer.exe", null, true, 1),
            new(2, "opera", @"C:\Users\x\Opera\opera.exe", null, true, 1),
        ];

        public bool RequestClose(int id) => true;

        public void ForceClose(int id)
        {
        }

        public bool IsRunning(int id) => false;
    }
}
