using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>The "Ready to play" preset of this user, as JSON next to the other user files; a missing or broken file is the empty default.</summary>
public sealed class PlayPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public PlayPresetStore(string path) => FilePath = path;

    public static string DefaultPath =>
        Path.Combine(ProductInfo.DataDirectory(Environment.SpecialFolder.LocalApplicationData), "play-preset.json");

    public string FilePath { get; }

    public PlayPreset Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<PlayPreset>(File.ReadAllText(FilePath), Options) ?? new PlayPreset() : new PlayPreset();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PlayPreset();
        }
    }

    public void Save(PlayPreset preset)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(preset, Options));
    }
}

/// <summary>A game "Ready to play" can start.</summary>
public sealed record PlayGameOption(string Name, string? Launch, string? Folder)
{
    public override string ToString() => Name;
}

public sealed record PlayMusicOption(MusicMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Card "Ready to play": one button closes the other programs and starts music and game; "Customize" edits the preset.
/// The fixed part of the keep list (Windows, protection, drivers, anti-cheat, launchers, the game, this app) is shown but
/// cannot be removed. Every change is saved at once.
/// </summary>
public sealed partial class PlayViewModel : ObservableObject
{
    private readonly PlayStarter _starter;
    private readonly PlayPresetStore _store;
    private readonly IProgramLauncher _launcher;
    private readonly Func<IReadOnlyList<RunningProcess>> _running;
    private readonly Localizer _localizer;
    private readonly string _windowsDirectory;
    private readonly int _selfId;
    private bool _loading;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string _newKeep = string.Empty;

    [ObservableProperty]
    private PlayGameOption? _selectedGame;

    [ObservableProperty]
    private PlayMusicOption? _selectedMusic;

    [ObservableProperty]
    private string _musicSearch = string.Empty;

    [ObservableProperty]
    private string _musicCommand = string.Empty;

    [ObservableProperty]
    private string _musicArguments = string.Empty;

    public PlayViewModel(
        PlayStarter starter,
        PlayPresetStore store,
        IProgramLauncher launcher,
        Func<IReadOnlyList<DetectedGame>> games,
        Func<IReadOnlyList<RunningProcess>> running,
        Localizer localizer,
        string windowsDirectory,
        int selfId)
    {
        _starter = starter;
        _store = store;
        _launcher = launcher;
        _running = running;
        _localizer = localizer;
        _windowsDirectory = windowsDirectory;
        _selfId = selfId;

        FixedKeep =
        [
            localizer["play.fixed.windows"], localizer["play.fixed.security"], localizer["play.fixed.drivers"],
            localizer["play.fixed.anticheat"], localizer["play.fixed.launchers"], localizer["play.fixed.game"], localizer["play.fixed.self"],
        ];
        MusicOptions =
        [
            new(MusicMode.None, localizer["play.music.none"]),
            new(MusicMode.SpotifySearch, localizer["play.music.search"]),
            new(MusicMode.Command, localizer["play.music.command"]),
        ];

        var preset = store.Load();
        _loading = true;
        Games = [new PlayGameOption(localizer["play.game.none"], null, null)];
        foreach (var game in SafeGames(games))
        {
            Games.Add(new PlayGameOption(game.Name, game.LaunchUri ?? game.ExePath, game.InstallFolder));
        }

        if (preset.GameName is { } name && Games.All(g => g.Name != name))
        {
            Games.Add(new PlayGameOption(name, preset.GameLaunch, preset.GameFolder));
        }

        foreach (var keep in preset.Keep)
        {
            Keep.Add(keep);
        }

        SelectedGame = Games.FirstOrDefault(g => g.Name == preset.GameName) ?? Games[0];
        SelectedMusic = MusicOptions.First(m => m.Mode == preset.Music);
        MusicSearch = preset.MusicSearch ?? string.Empty;
        MusicCommand = preset.MusicCommand ?? string.Empty;
        MusicArguments = preset.MusicArguments ?? string.Empty;
        _loading = false;
        Keep.CollectionChanged += (_, _) => Changed();
    }

    public IReadOnlyList<string> FixedKeep { get; }

    public ObservableCollection<string> Keep { get; } = [];

    public ObservableCollection<PlayGameOption> Games { get; }

    public IReadOnlyList<PlayMusicOption> MusicOptions { get; }

    /// <summary>Programs running now that would be closed, as suggestions for the keep list.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool IsSearchMode => SelectedMusic?.Mode == MusicMode.SpotifySearch;

    public bool IsCommandMode => SelectedMusic?.Mode == MusicMode.Command;

    public string Summary
    {
        get
        {
            var preset = Current();
            var game = preset.GameName ?? _localizer["play.summary.nogame"];
            var music = preset.Music switch
            {
                MusicMode.SpotifySearch when preset.MusicSearch is { Length: > 0 } s => _localizer.Format("play.summary.search", ("text", s)),
                MusicMode.Command when preset.MusicCommand is { Length: > 0 } => _localizer["play.summary.command"],
                _ => _localizer["play.summary.nomusic"],
            };
            return _localizer.Format("play.summary", ("n", preset.Keep.Count), ("game", game), ("music", music));
        }
    }

    public PlayPreset Current() => new()
    {
        Keep = [.. Keep],
        GameName = SelectedGame?.Launch is null ? null : SelectedGame.Name,
        GameLaunch = SelectedGame?.Launch,
        GameFolder = SelectedGame?.Folder,
        Music = SelectedMusic?.Mode ?? MusicMode.None,
        MusicSearch = string.IsNullOrWhiteSpace(MusicSearch) ? null : MusicSearch.Trim(),
        MusicCommand = string.IsNullOrWhiteSpace(MusicCommand) ? null : MusicCommand.Trim(),
        MusicArguments = string.IsNullOrWhiteSpace(MusicArguments) ? null : MusicArguments.Trim(),
    };

    [RelayCommand]
    private async Task Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        ResultText = null;
        try
        {
            var result = await Task.Run(() => _starter.RunAsync(Current())).ConfigureAwait(true);
            var parts = new List<string> { _localizer.Format("play.result.closed", ("n", result.Closed.Count)) };
            if (result.StillRunning.Count > 0)
            {
                parts.Add(_localizer.Format("play.result.still", ("names", string.Join(", ", result.StillRunning))));
            }

            if (result.MusicError is { } music)
            {
                parts.Add(_localizer.Format("play.result.musicerror", ("error", music)));
            }

            if (result.GameError is { } game)
            {
                parts.Add(_localizer.Format("play.result.gameerror", ("error", game)));
            }

            ResultText = string.Join(" ", parts);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void ToggleEditing()
    {
        IsEditing = !IsEditing;
        if (IsEditing)
        {
            RefreshSuggestions();
        }
    }

    [RelayCommand]
    private void AddKeep()
    {
        var name = Path.GetFileNameWithoutExtension(NewKeep.Trim());
        if (name.Length > 0 && !Keep.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Keep.Add(name);
        }

        NewKeep = string.Empty;
        RefreshSuggestions();
    }

    [RelayCommand]
    private void RemoveKeep(string name)
    {
        Keep.Remove(name);
        RefreshSuggestions();
    }

    /// <summary>Runs only the music step, to check a search or command without closing anything.</summary>
    [RelayCommand]
    private void TestMusic()
    {
        try
        {
            if (IsSearchMode && MusicSearch.Trim().Length > 0)
            {
                _launcher.Open("spotify:search:" + Uri.EscapeDataString(MusicSearch.Trim()));
            }
            else if (IsCommandMode && MusicCommand.Trim().Length > 0)
            {
                _launcher.Start(MusicCommand.Trim(), MusicArguments.Trim());
            }

            ResultText = _localizer["play.test.started"];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            ResultText = _localizer.Format("play.result.musicerror", ("error", ex.Message));
        }
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        try
        {
            var names = PlayStarter.ToClose(_running(), Current(), _windowsDirectory, _selfId)
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
            foreach (var name in names)
            {
                Suggestions.Add(name);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // Suggestions are a convenience; typing a name works without them.
        }
    }

    partial void OnSelectedGameChanged(PlayGameOption? value) => Changed();

    partial void OnSelectedMusicChanged(PlayMusicOption? value)
    {
        OnPropertyChanged(nameof(IsSearchMode));
        OnPropertyChanged(nameof(IsCommandMode));
        Changed();
    }

    partial void OnMusicSearchChanged(string value) => Changed();

    partial void OnMusicCommandChanged(string value) => Changed();

    partial void OnMusicArgumentsChanged(string value) => Changed();

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _store.Save(Current());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultText = _localizer.Format("perm.error", ("error", ex.Message));
        }

        OnPropertyChanged(nameof(Summary));
    }

    private static IReadOnlyList<DetectedGame> SafeGames(Func<IReadOnlyList<DetectedGame>> games)
    {
        try
        {
            return games();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return [];
        }
    }
}
