using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>"Was bedeutet das?": kurze Erklaerung (hoechstens 3 Saetze), auf- und zuklappbar.</summary>
public sealed partial class HelpViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOpen;

    public HelpViewModel(string textKey) => TextKey = textKey;

    public string TextKey { get; }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;
}

/// <summary>
/// Seite im Einfach-Modus (V2, Abschnitt 2.3): Suchleiste, Karten nach Wirkung sortiert, hoechstens 7 sichtbar,
/// darunter "Mehr anzeigen". Versteckte Tweaks (Unklar, Experte) erscheinen hier nie.
/// </summary>
public sealed partial class SimpleTweakPageViewModel : PageViewModel
{
    public const int VisibleLimit = 7;

    private readonly TweakWorkspace _workspace;
    private readonly Func<TweakCardViewModel, bool> _filter;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _showAll;

    public SimpleTweakPageViewModel(string titleKey, TweakWorkspace workspace, Func<TweakCardViewModel, bool> filter, string helpKey, IReadOnlyList<object>? extras = null)
        : base(titleKey, showHonestyBanner: false)
    {
        _workspace = workspace;
        _filter = filter;
        Help = new HelpViewModel(helpKey);
        Extras = extras ?? [];
        workspace.Localizer.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public HelpViewModel Help { get; }

    public IReadOnlyList<object> Extras { get; }

    public ObservableCollection<TweakCardViewModel> Cards { get; } = [];

    public int HiddenCount { get; private set; }

    public bool HasMore => HiddenCount > 0;

    public bool IsEmpty => Cards.Count == 0;

    public string MoreText => _workspace.Localizer.Format("simple.more", ("n", HiddenCount));

    /// <summary>Sortierung: spuerbare Wirkung zuerst, dann geringeres Risiko, dann Titel.</summary>
    public static IEnumerable<TweakCardViewModel> Order(IEnumerable<TweakCardViewModel> cards) => cards
        .OrderBy(c => c.Tweak.Impact ?? ImpactLevel.Unclear)
        .ThenBy(c => c.Tweak.Risk)
        .ThenBy(c => c.PlainTitle, StringComparer.CurrentCultureIgnoreCase);

    public void Refresh()
    {
        var matches = Order(_workspace.Cards.Where(c => c.IsVisible && _filter(c) && c.Matches(Query))).ToList();
        var shown = ShowAll || Query.Length > 0 ? matches : matches.Take(VisibleLimit).ToList();
        HiddenCount = matches.Count - shown.Count;
        Cards.Clear();
        foreach (var card in shown)
        {
            Cards.Add(card);
        }

        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnShowAllChanged(bool value) => Refresh();

    [RelayCommand]
    private void More() => ShowAll = true;
}

public sealed record BackRow(string TweakId, string Title, string When);

/// <summary>"Zurück" im Einfach-Modus (V2, Abschnitt 2.6): was geaendert wurde, je Eintrag "Rueckgaengig", oben "Alles rueckgaengig machen".</summary>
public sealed partial class SimpleBackViewModel : PageViewModel
{
    private readonly TweakWorkspace _workspace;
    private readonly RestorePageViewModel _restore;

    public SimpleBackViewModel(TweakWorkspace workspace, IDialogService dialogs)
        : base("nav.simple.back", showHonestyBanner: false)
    {
        _workspace = workspace;
        _restore = new RestorePageViewModel(workspace, dialogs);
        Help = new HelpViewModel("simple.back.help");
    }

    public HelpViewModel Help { get; }

    public ObservableCollection<BackRow> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public async Task LoadAsync()
    {
        var applied = await Task.Run(_workspace.Service.GetAppliedTweaks).ConfigureAwait(true);
        var culture = CultureInfo.GetCultureInfo(_workspace.Localizer.Culture);
        Items.Clear();
        foreach (var entry in applied.OrderByDescending(e => e.AppliedUtc))
        {
            var title = _workspace.Cards.FirstOrDefault(c => c.Tweak.Id == entry.TweakId)?.PlainTitle ?? _workspace.NameOf(entry.TweakId);
            Items.Add(new BackRow(entry.TweakId, title, entry.AppliedUtc.ToLocalTime().ToString("g", culture)));
        }

        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    private async Task Undo(BackRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        await _workspace.RevertAsync(row.TweakId).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UndoAll()
    {
        await _restore.UndoAllCommand.ExecuteAsync(null).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }
}

/// <summary>Kleine Karte "Internet prüfen" im Einfach-Modus: nur der Ping-Test, ohne Netzwerk-Reset.</summary>
public sealed record SimplePingCard(NetworkToolsViewModel Tools);

/// <summary>Kleine Karte "Updates" im Einfach-Modus: Stand und Windows Update öffnen.</summary>
public sealed record SimpleUpdatesCard(UpdatesOverviewViewModel Updates);

/// <summary>Card "Ready to play": close other programs, start music and game in one click.</summary>
public sealed record SimplePlayCard(PlayViewModel Play);

/// <summary>Card "Clean up background": close programs you do not need before playing.</summary>
public sealed record SimpleBackgroundCard(BackgroundCleanupViewModel Cleanup);

/// <summary>Bereich "Programme beim Start" im Einfach-Modus.</summary>
public sealed record SimpleAutostartCard(AutostartViewModel Autostart);
