using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Controls;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Localization;
using Wpf.Ui.Controls;

namespace TweakPilot.App.ViewModels;

/// <summary>Basis aller Seiten. Seiten-ViewModels werden erst beim ersten Aufruf erzeugt (schneller Start).</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(string titleKey, bool showHonestyBanner)
    {
        TitleKey = titleKey;
        ShowHonestyBanner = showHonestyBanner;
    }

    public string TitleKey { get; }

    /// <summary>Dashboard und Tweak-Seiten zeigen dauerhaft den Ehrlichkeitshinweis (Regel S9).</summary>
    public bool ShowHonestyBanner { get; }
}

public sealed class PlaceholderPageViewModel : PageViewModel
{
    public PlaceholderPageViewModel(string titleKey, bool showHonestyBanner)
        : base(titleKey, showHonestyBanner)
    {
    }
}

public sealed record NavItem(string Key, SymbolRegular Symbol, Func<PageViewModel> Create);

public enum SupportBannerKind
{
    None,
    Info,
    Warning,
    Danger,
}

public sealed partial class ShellViewModel : ObservableObject
{
    public const double CollapseBelowWidth = 1000;
    public const double ExpandedNavWidth = 240;
    public const double CollapsedNavWidth = 56;

    private readonly Dictionary<NavItem, PageViewModel> _pages = [];
    private readonly Localizer _localizer;
    private readonly int _build;
    private bool _manualCollapse;
    private double _windowWidth = 1280;

    [ObservableProperty]
    private NavItem? _selectedItem;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private bool _isNavCollapsed;

    [ObservableProperty]
    private NavItem? _selectedFooterItem;

    [ObservableProperty]
    private bool _isExpertMode = true;

    private readonly IReadOnlyList<NavItem> _expertItems;
    private readonly IReadOnlyList<NavItem>? _simpleItems;
    private readonly IReadOnlyList<NavItem> _footerItems;

    /// <param name="items">Navigation im Experten-Modus (alle Seiten).</param>
    /// <param name="simpleItems">V2: Navigation im Einfach-Modus (5 Seiten); ohne sie gibt es nur den Experten-Modus.</param>
    /// <param name="footerItems">V2: fest am unteren Rand im Einfach-Modus (Zurueck, Einstellungen).</param>
    public ShellViewModel(Localizer localizer, int build, IEnumerable<NavItem> items, TweakWorkspace? workspace = null, OverlayDialogService? dialogs = null,
        IEnumerable<NavItem>? simpleItems = null, IEnumerable<NavItem>? footerItems = null, bool expertMode = true)
    {
        Workspace = workspace;
        Dialogs = dialogs ?? new OverlayDialogService();
        _localizer = localizer;
        _build = build;
        _expertItems = items.ToList();
        _simpleItems = simpleItems?.ToList();
        _footerItems = footerItems?.ToList() ?? [];
        Items = [];
        Support = SupportPolicy.Classify(build);
        _localizer.PropertyChanged += OnLanguageChanged;
        ApplyMode(expertMode || _simpleItems is null);
    }

    public ObservableCollection<NavItem> Items { get; }

    public ObservableCollection<NavItem> FooterItems { get; } = [];

    /// <summary>Umschalter "Einfach / Experte" gibt es nur, wenn ein Einfach-Modus definiert ist.</summary>
    public bool SupportsModes => _simpleItems is not null;

    /// <summary>Wird beim Umschalten aufgerufen (Einstellung speichern, versteckte Tweaks ein- oder ausblenden).</summary>
    public Action<bool>? ModeChanged { get; set; }

    /// <summary>Wechselt die Navigation; die zuerst aufgerufene Seite des Modus wird angezeigt.</summary>
    public void ApplyMode(bool expert)
    {
        IsExpertMode = expert || _simpleItems is null;
        SelectedItem = null;
        SelectedFooterItem = null;
        Items.Clear();
        foreach (var item in IsExpertMode ? _expertItems : _simpleItems!)
        {
            Items.Add(item);
        }

        FooterItems.Clear();
        foreach (var item in IsExpertMode ? [] : _footerItems)
        {
            FooterItems.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand]
    private void UseSimple() => SwitchMode(false);

    [RelayCommand]
    private void UseExpert() => SwitchMode(true);

    private void SwitchMode(bool expert)
    {
        if (!SupportsModes || IsExpertMode == expert)
        {
            return;
        }

        ApplyMode(expert);
        ModeChanged?.Invoke(expert);
    }

    /// <summary>Gemeinsamer Zustand der Tweak-Seiten (Aenderungsleiste, Detailpanel). Null nur in Tests ohne Engine.</summary>
    public TweakWorkspace? Workspace { get; }

    public OverlayDialogService Dialogs { get; }

    public BuildSupport Support { get; }

    /// <summary>Nicht unterstuetzter Build: alles ansehen, nichts aendern (Regel S11).</summary>
    public bool IsReadOnly => Support == BuildSupport.Unsupported;

    public double NavWidth => IsNavCollapsed ? CollapsedNavWidth : ExpandedNavWidth;

    public SupportBannerKind BannerKind => Support switch
    {
        BuildSupport.Unsupported => SupportBannerKind.Danger,
        BuildSupport.Untested => SupportBannerKind.Warning,
        _ when _build < SupportPolicy.FirstWindows11Build => SupportBannerKind.Info,
        _ => SupportBannerKind.None,
    };

    public BadgeTone BannerTone => BannerKind switch
    {
        SupportBannerKind.Danger => BadgeTone.Danger,
        SupportBannerKind.Warning => BadgeTone.Warning,
        _ => BadgeTone.Accent,
    };

    public SymbolRegular BannerSymbol => BannerKind switch
    {
        SupportBannerKind.Danger => SymbolRegular.ErrorCircle16,
        SupportBannerKind.Warning => SymbolRegular.Warning16,
        _ => SymbolRegular.Info16,
    };

    public string BannerBadgeText => BannerKind switch
    {
        SupportBannerKind.Danger => _localizer["banner.badge.unsupported"],
        SupportBannerKind.Warning => _localizer["banner.badge.untested"],
        _ => _localizer["banner.badge.info"],
    };

    public string BannerTitle => BannerKind == SupportBannerKind.Danger ? _localizer["unsupported.title"] : string.Empty;

    public string BannerText => BannerKind switch
    {
        SupportBannerKind.Danger => _localizer["unsupported.body"],
        SupportBannerKind.Warning => _localizer.Format("unverified.build", ("build", _build)),
        SupportBannerKind.Info => _localizer["win10.support"],
        _ => string.Empty,
    };

    /// <summary>Wird vom Fenster bei Groessenaenderung aufgerufen.</summary>
    /// <summary>Fenster schmaler als 1000 Punkte: Navigation eingeklappt, Detailpanel ueberlagert den Inhalt.</summary>
    public bool IsNarrow => _windowWidth < CollapseBelowWidth;

    public void UpdateWindowWidth(double width)
    {
        _windowWidth = width;
        OnPropertyChanged(nameof(IsNarrow));
        IsNavCollapsed = _manualCollapse || width < CollapseBelowWidth;
    }

    [RelayCommand]
    private void ToggleNav()
    {
        _manualCollapse = !IsNavCollapsed;
        IsNavCollapsed = _manualCollapse || _windowWidth < CollapseBelowWidth;
    }

    partial void OnIsNavCollapsedChanged(bool value) => OnPropertyChanged(nameof(NavWidth));

    partial void OnSelectedFooterItemChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedItem = null;
        Show(value);
    }

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedFooterItem = null;
        Show(value);
    }

    private void Show(NavItem value)
    {
        if (!_pages.TryGetValue(value, out var page))
        {
            page = value.Create();
            _pages[value] = page;
        }

        CurrentPage = page;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(BannerBadgeText));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerText));
    }
}
