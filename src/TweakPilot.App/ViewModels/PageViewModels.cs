using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Services;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

public sealed record Choice<T>(T Value, string LabelKey);

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly Localizer _localizer;
    private readonly IThemeService _theme;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private bool _loading;

    [ObservableProperty]
    private Choice<string>? _selectedLanguage;

    [ObservableProperty]
    private Choice<ThemeChoice>? _selectedTheme;

    [ObservableProperty]
    private bool _expertMode;

    private readonly Action<bool>? _expertModeChanged;

    public SettingsViewModel(Localizer localizer, IThemeService theme, SettingsStore store, AppSettings settings, Action<bool>? expertModeChanged = null)
        : base("nav.settings", showHonestyBanner: false)
    {
        _expertModeChanged = expertModeChanged;
        _localizer = localizer;
        _theme = theme;
        _store = store;
        _settings = settings;
        _loading = true;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Value == settings.Language) ?? Languages[0];
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == settings.Theme) ?? Themes[0];
        ExpertMode = settings.ExpertMode;
        _loading = false;
    }

    public IReadOnlyList<Choice<string>> Languages { get; } =
    [
        new(Localizer.German, "settings.language.de"),
        new(Localizer.English, "settings.language.en"),
    ];

    public IReadOnlyList<Choice<ThemeChoice>> Themes { get; } =
    [
        new(ThemeChoice.Dark, "settings.theme.dark"),
        new(ThemeChoice.Light, "settings.theme.light"),
        new(ThemeChoice.System, "settings.theme.system"),
    ];

    partial void OnSelectedLanguageChanged(Choice<string>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        _localizer.SetCulture(value.Value);
        _settings.Language = value.Value;
        _store.Save(_settings);
    }

    /// <summary>Expertenmodus blendet Risiko "Experte" und Nachweis "Umstritten" ein (Abschnitt 6.11).</summary>
    partial void OnExpertModeChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.ExpertMode = value;
        _store.Save(_settings);
        _expertModeChanged?.Invoke(value);
    }

    partial void OnSelectedThemeChanged(Choice<ThemeChoice>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        _theme.Apply(value.Value);
        _settings.Theme = value.Value;
        _store.Save(_settings);
    }

    /// <summary>Der Modus wurde im Kopf der App umgeschaltet: Schalter nachziehen, ohne erneut zu speichern.</summary>
    public void SyncExpertMode(bool expert)
    {
        _loading = true;
        ExpertMode = expert;
        _loading = false;
    }

    /// <summary>Wartung (Protokolle, Update, Kommandozeile); fehlt im Einrichtungsassistenten.</summary>
    public SettingsMaintenanceViewModel? Maintenance { get; set; }

    /// <summary>"Immer behalten"-Liste der grossen Dateien (V2 Abschnitt 6.3): ansehen und Eintraege entfernen.</summary>
    public KeepListViewModel? KeepList { get; init; }

    /// <summary>"Alle Einstellungen der App zuruecksetzen": Sprache, Design und Expertenmodus auf Standard. Angewendete Tweaks bleiben unveraendert.</summary>
    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        SelectedLanguage = Languages.First(l => l.Value == defaults.Language);
        SelectedTheme = Themes.First(t => t.Value == defaults.Theme);
        ExpertMode = defaults.ExpertMode;
    }
}

/// <summary>Gestaltungskatalog: alle Komponenten und Zustaende (nur in Entwicklungs-Builds in der Navigation).</summary>
public sealed partial class DesignCatalogViewModel : PageViewModel
{
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isRunning = true;

    [ObservableProperty]
    private bool? _toggleOn = true;

    [ObservableProperty]
    private bool? _toggleOff = false;

    [ObservableProperty]
    private bool? _toggleMixed;

    public DesignCatalogViewModel(Localizer localizer)
        : base("nav.catalog", showHonestyBanner: true)
    {
        _localizer = localizer;
        ListItems = new ObservableCollection<string>(Enumerable.Range(1, 4).Select(i => localizer.Format("catalog.list.item", ("n", i))));
    }

    public ObservableCollection<string> ListItems { get; }

    public string UnavailableTooltip => _localizer.Format("unavailable.tooltip", ("reason", _localizer["catalog.reason.example"]));

    public string PendingText => _localizer.Format("changebar.pending", ("n", 3));

    [RelayCommand]
    private void ToggleRunning() => IsRunning = !IsRunning;
}
