using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Controls;
using TweakPilot.App.Services;
using TweakPilot.Engine.Insights;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

/// <summary>Gaming (Abschnitt 6.3): Tweak-Karten, Spielprofile und Anti-Cheat-Pruefung; der Anti-Cheat-Hinweis steht immer oben.</summary>
public sealed partial class GamingPageViewModel : PageViewModel
{
    private readonly Dictionary<string, Func<object>> _factories;
    private readonly Dictionary<string, object> _created = [];

    [ObservableProperty]
    private SectionItem _selectedSection;

    [ObservableProperty]
    private object? _currentSection;

    public GamingPageViewModel(TweakWorkspace workspace, Func<GameProfilesViewModel> profiles, Func<AntiCheatViewModel> antiCheat, Func<GamingChecksViewModel>? checks = null, Action<GamingJump>? navigate = null)
        : base("nav.gaming", showHonestyBanner: true)
    {
        Sections = checks is null
            ?
            [
                new("tweaks", "perf.section.tweaks"),
                new("profiles", "gaming.section.profiles"),
                new("anticheat", "gaming.section.anticheat"),
            ]
            :
            [
                new("tweaks", "perf.section.tweaks"),
                new("checks", "gaming.section.checks"),
                new("profiles", "gaming.section.profiles"),
                new("anticheat", "gaming.section.anticheat"),
            ];
        _factories = new Dictionary<string, Func<object>>(StringComparer.Ordinal)
        {
            ["tweaks"] = () => new TweakListPageViewModel("nav.gaming", workspace, "GAME", "POWR", "DEFN"),
            ["profiles"] = () => { var p = profiles(); _ = p.LoadAsync(); return p; },
            ["anticheat"] = () => { var a = antiCheat(); _ = a.LoadAsync(); return a; },
            ["checks"] = () =>
            {
                var c = checks!();
                c.Targets = new HashSet<GamingJump> { GamingJump.Cleanup, GamingJump.Autostart, GamingJump.AntiCheat, GamingJump.GameProfiles };
                c.Navigate = jump =>
                {
                    if (jump is GamingJump.AntiCheat or GamingJump.GameProfiles)
                    {
                        SelectedSection = Sections.First(s => s.Key == (jump == GamingJump.AntiCheat ? "anticheat" : "profiles"));
                    }
                    else
                    {
                        navigate?.Invoke(jump);
                    }
                };
                _ = c.LoadAsync();
                return c;
            },
        };
        _selectedSection = Sections[0];
        CurrentSection = Section(Sections[0].Key);
    }

    public IReadOnlyList<SectionItem> Sections { get; }

    partial void OnSelectedSectionChanged(SectionItem value)
    {
        if (value is not null)
        {
            CurrentSection = Section(value.Key);
        }
    }

    private object Section(string key)
    {
        if (!_created.TryGetValue(key, out var section))
        {
            section = _factories[key]();
            _created[key] = section;
        }

        return section;
    }
}

public sealed record AntiCheatRow(string Title, BadgeTone Tone, string StateText, string Explanation);

public sealed partial class AntiCheatViewModel : ObservableObject
{
    private readonly ISecurityFactsSource _source;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isLoading;

    public AntiCheatViewModel(ISecurityFactsSource source, Localizer localizer)
    {
        _source = source;
        _localizer = localizer;
    }

    public ObservableCollection<AntiCheatRow> Rows { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var facts = await Task.Run(_source.Read).ConfigureAwait(true);
            Rows.Clear();
            foreach (var check in AntiCheatCheck.Evaluate(facts))
            {
                var tone = check.State switch
                {
                    CheckState.Good => BadgeTone.Success,
                    CheckState.Attention => BadgeTone.Warning,
                    CheckState.Info => BadgeTone.Neutral,
                    _ => BadgeTone.Neutral,
                };
                Rows.Add(new AntiCheatRow(_localizer["anticheat.check." + check.Id], tone, _localizer[check.ValueKey], _localizer[check.ExplanationKey]));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed record DetectedGameRow(DetectedGame Game, string Detail, bool NeedsExe);

/// <summary>Spielprofile: gefundene Spiele uebernehmen, Einstellungen pro Spiel, Start mit Prioritaet.</summary>
public sealed partial class GameProfilesViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<DetectedGame>> _scan;
    private readonly GameProfileStore _store;
    private readonly TweakWorkspace _workspace;
    private readonly IGameLauncher _launcher;
    private readonly IFilePicker _picker;
    private readonly List<GameProfile> _profiles = [];

    [ObservableProperty]
    private bool _isLoading;

    public GameProfilesViewModel(Func<IReadOnlyList<DetectedGame>> scan, GameProfileStore store, TweakWorkspace workspace, IGameLauncher launcher, IFilePicker picker)
    {
        _scan = scan;
        _store = store;
        _workspace = workspace;
        _launcher = launcher;
        _picker = picker;
    }

    public ObservableCollection<GameProfileViewModel> Profiles { get; } = [];

    public ObservableCollection<DetectedGameRow> Detected { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    public bool HasDetected => Detected.Count > 0;

    internal TweakWorkspace Workspace => _workspace;

    internal IGameLauncher Launcher => _launcher;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var (games, applied) = await Task.Run(() => (_scan(), AppliedIds())).ConfigureAwait(true);
            _profiles.Clear();
            _profiles.AddRange(_store.Load());

            // ANNAHME (Abschnitt 6.3): Das Fortnite-Profil ist das erste Beispielprofil, sobald Fortnite gefunden wird.
            if (_profiles.Count == 0 && games.FirstOrDefault(g => g.Source == GameSource.Epic && g.Name == "Fortnite" && g.ExePath is not null) is { } fortnite)
            {
                _profiles.Add(FromGame(fortnite, fortnite.ExePath!));
                _store.Save(_profiles);
            }

            Profiles.Clear();
            foreach (var profile in _profiles)
            {
                Profiles.Add(new GameProfileViewModel(this, profile, applied));
            }

            Detected.Clear();
            foreach (var game in games.Where(g => !_profiles.Any(p => SameGame(p, g))))
            {
                var loc = _workspace.Localizer;
                var anti = game.AntiCheat == AntiCheat.None ? loc["gaming.anticheat.none"] : loc.Format("gaming.anticheat.found", ("names", game.AntiCheat.ToString()));
                Detected.Add(new DetectedGameRow(game, $"{game.Source} · {anti}", game.ExePath is null));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(HasDetected));
        }
    }

    [RelayCommand]
    private async Task Add(DetectedGameRow row)
    {
        var exe = row.Game.ExePath ?? _picker.PickExe(row.Game.InstallFolder);
        if (exe is null)
        {
            return;
        }

        _profiles.Add(FromGame(row.Game, exe));
        _store.Save(_profiles);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task AddManual()
    {
        if (_picker.PickExe(null) is not { } exe)
        {
            return;
        }

        _profiles.Add(new GameProfile { Name = Path.GetFileNameWithoutExtension(exe), ExePath = exe });
        _store.Save(_profiles);
        await LoadAsync().ConfigureAwait(true);
    }

    internal void Save() => _store.Save(_profiles);

    internal async Task RemoveAsync(GameProfileViewModel profile)
    {
        var applied = AppliedIds();
        var undo = profile.Profile.AllTweakIds().Where(applied.Contains).ToList();
        if (undo.Count > 0)
        {
            await _workspace.ApplyIdsAsync([], undo).ConfigureAwait(true);
            if (profile.Profile.AllTweakIds().Any(AppliedIds().Contains))
            {
                // Zuruecksetzen abgebrochen oder fehlgeschlagen: Profil bleibt, damit nichts ohne Rueckweg verloren geht.
                await LoadAsync().ConfigureAwait(true);
                return;
            }
        }

        _profiles.Remove(profile.Profile);
        Save();
        await LoadAsync().ConfigureAwait(true);
    }

    internal HashSet<string> AppliedIds() => _workspace.Service.GetAppliedTweaks().Select(e => e.TweakId).ToHashSet(StringComparer.Ordinal);

    private static bool SameGame(GameProfile profile, DetectedGame game) =>
        (game.ExePath is not null && string.Equals(profile.ExePath, game.ExePath, StringComparison.OrdinalIgnoreCase))
        || profile.ExePath.StartsWith(game.InstallFolder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static GameProfile FromGame(DetectedGame game, string exe) => new()
    {
        Name = game.Name,
        ExePath = exe,
        Source = game.Source,
        LaunchUri = game.LaunchUri,
        AntiCheat = game.AntiCheat,
        GpuHighPerformance = true,
    };
}

public sealed partial class GameProfileViewModel : ObservableObject
{
    private readonly GameProfilesViewModel _owner;
    private readonly HashSet<string> _applied;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isStarting;

    public GameProfileViewModel(GameProfilesViewModel owner, GameProfile profile, HashSet<string> applied)
    {
        _owner = owner;
        _applied = applied;
        Profile = profile;
        StatusText = IsApplied ? Loc["gaming.profile.applied"] : Profile.DesiredTweakIds().Count > 0 ? Loc["gaming.profile.notapplied"] : null;
    }

    public GameProfile Profile { get; }

    public string Name => Profile.Name;

    public string Detail => $"{Profile.ExePath} · {(Profile.HasAntiCheat ? Loc.Format("gaming.anticheat.found", ("names", Profile.AntiCheat.ToString())) : Loc["gaming.anticheat.none"])}";

    public bool ExpertMode => _owner.Workspace.ExpertMode;

    /// <summary>GAME-016 ist bei Anti-Cheat-Spielen gesperrt (Abschnitt 6.3).</summary>
    public bool CanUsePermanentPriority => !Profile.HasAntiCheat;

    public bool IsApplied => Profile.DesiredTweakIds().All(_applied.Contains) && !Undesired().Any();

    public bool GpuHighPerformance
    {
        get => Profile.GpuHighPerformance;
        set => Change(v => Profile.GpuHighPerformance = v, value);
    }

    public bool DisableFullscreenOptimizations
    {
        get => Profile.DisableFullscreenOptimizations;
        set => Change(v => Profile.DisableFullscreenOptimizations = v, value);
    }

    public bool StartHighPriority
    {
        get => Profile.StartHighPriority;
        set => Change(v => Profile.StartHighPriority = v, value);
    }

    public bool PermanentPriority
    {
        get => Profile.PermanentPriority;
        set => Change(v => Profile.PermanentPriority = v && CanUsePermanentPriority, value);
    }

    private Localizer Loc => _owner.Workspace.Localizer;

    /// <summary>Uebernehmen: fehlende Tweaks anwenden, abgewaehlte zuruecksetzen; immer mit Vorschau.</summary>
    [RelayCommand]
    private async Task Apply()
    {
        var applies = Profile.DesiredTweakIds().Where(id => !_applied.Contains(id)).ToList();
        var reverts = Undesired().ToList();
        await _owner.Workspace.ApplyIdsAsync(applies, reverts).ConfigureAwait(true);
        await _owner.LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task Start()
    {
        IsStarting = true;
        StatusText = Loc["gaming.start.running"];
        try
        {
            var result = await _owner.Launcher.StartAsync(Profile, CancellationToken.None).ConfigureAwait(true);
            StatusText = Loc["gaming.start." + result.Outcome] + (result.Detail is null ? string.Empty : $" ({result.Detail})");
        }
        finally
        {
            IsStarting = false;
        }
    }

    [RelayCommand]
    private Task Remove() => _owner.RemoveAsync(this);

    private IEnumerable<string> Undesired()
    {
        var desired = Profile.DesiredTweakIds();
        return Profile.AllTweakIds().Where(id => _applied.Contains(id) && !desired.Contains(id));
    }

    private void Change(Action<bool> set, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        set(value);
        _owner.Save();
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(IsApplied));
        StatusText = IsApplied ? Loc["gaming.profile.applied"] : Loc["gaming.profile.notapplied"];
    }
}
