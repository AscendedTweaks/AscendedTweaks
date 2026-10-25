using System.Collections.ObjectModel;
using System.Globalization;
using AscendedTweaks.App.Controls;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Debloat;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>Verwaltung entfernter Apps: Entfernen (Helfer), Liste, Wiederherstellen (Benutzerkontext).</summary>
public interface IAppManager
{
    IReadOnlyList<AppxPackageInfo> ListInstalled();

    IReadOnlyList<RemovedApp> Remove(IReadOnlyList<string> packageNames, bool deprovision);

    IReadOnlyList<RemovedApp> RemovedApps();

    RestoreOutcome Restore(RemovedApp app);
}

/// <summary>Debloat (Abschnitt 6.6): Apps, Oberflaeche und Widgets, OneDrive.</summary>
public sealed partial class DebloatPageViewModel : PageViewModel
{
    private readonly Dictionary<string, Func<object>> _factories;
    private readonly Dictionary<string, object> _created = [];

    [ObservableProperty]
    private SectionItem _selectedSection;

    [ObservableProperty]
    private object? _currentSection;

    public DebloatPageViewModel(TweakWorkspace workspace, Func<AppsViewModel> apps, Func<OneDriveViewModel> oneDrive)
        : base("nav.debloat", showHonestyBanner: true)
    {
        Sections =
        [
            new("apps", "debloat.section.apps"),
            new("ui", "debloat.section.ui"),
            new("onedrive", "debloat.section.onedrive"),
        ];
        _factories = new Dictionary<string, Func<object>>(StringComparer.Ordinal)
        {
            ["apps"] = () => { var a = apps(); _ = a.LoadAsync(); return a; },
            ["ui"] = () => new TweakListPageViewModel("nav.debloat", workspace, "DEB", "UI"),
            ["onedrive"] = () => { var o = oneDrive(); _ = o.LoadAsync(); return o; },
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

public sealed partial class AppRowViewModel : ObservableObject
{
    private readonly AppsViewModel _owner;

    [ObservableProperty]
    private bool _isSelected;

    public AppRowViewModel(AppsViewModel owner, AppRow row, Localizer localizer)
    {
        _owner = owner;
        Row = row;
        if (row.Catalog is { } catalog)
        {
            Name = localizer[$"deb.{catalog.Id}.name"];
            Description = localizer[$"deb.{catalog.Id}.desc"];
            (RiskTone, RiskSymbol, RiskText) = Badge(catalog.Risk, localizer);
        }
        else
        {
            Name = row.PackageName;
            var publisher = row.Installed?.PublisherDisplayName ?? string.Empty;
            Description = localizer.Format("debloat.publisher", ("name", publisher));

            // Treiber- und Sicherheits-Apps anderer Hersteller: deutlicher Hinweis, nie vorausgewaehlt.
            var (rating, _) = AutostartInventory.Rate(row.PackageName, null, new SignatureInfo(SignatureStatus.Valid, publisher));
            Warning = rating == AutostartRating.Keep ? localizer["debloat.warning.vendor"] : null;
            (RiskTone, RiskSymbol, RiskText) = Badge(RiskLevel.Caution, localizer);
        }

        LockReason = row.Locked ? localizer["debloat.locked"] : null;
    }

    public AppRow Row { get; }

    public string Name { get; }

    public string Description { get; }

    public string? Warning { get; }

    public string? LockReason { get; }

    public BadgeTone RiskTone { get; }

    public Wpf.Ui.Controls.SymbolRegular RiskSymbol { get; }

    public string RiskText { get; }

    public bool IsInstalled => Row.Installed is not null;

    public bool CanSelect => IsInstalled && !Row.Locked && !_owner.IsReadOnly;

    partial void OnIsSelectedChanged(bool value) => _owner.SelectionChanged();

    private static (BadgeTone, Wpf.Ui.Controls.SymbolRegular, string) Badge(RiskLevel risk, Localizer localizer)
    {
        var (tone, symbol, key) = BadgeStyle.Risk(risk);
        return (tone, symbol, localizer[key]);
    }
}

public sealed record AppGroupViewModel(string Title, IReadOnlyList<AppRowViewModel> Apps);

public sealed partial class AppsViewModel : ObservableObject
{
    private readonly IAppManager _apps;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;
    private readonly int _build;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _deprovision;

    [ObservableProperty]
    private string? _error;

    private readonly Action<string>? _openUri;

    public AppsViewModel(IAppManager apps, IDialogService dialogs, Localizer localizer, int build, bool readOnly, Action<string>? openUri = null)
    {
        _apps = apps;
        _dialogs = dialogs;
        _localizer = localizer;
        _build = build;
        IsReadOnly = readOnly;
        _openUri = openUri;
    }

    /// <summary>
    /// Edge is not removed by the app: in the EU (EEA) Windows itself offers "Uninstall" for Microsoft Edge under Settings &gt;
    /// Apps, and only that keeps Windows search, links and updates working. The WebView2 runtime other apps need stays.
    /// </summary>
    [RelayCommand]
    private void OpenEdgeUninstall() => _openUri?.Invoke("ms-settings:appsfeatures");

    public ObservableCollection<AppGroupViewModel> Groups { get; } = [];

    public bool IsReadOnly { get; }

    public bool CanRemove => !IsBusy && !IsReadOnly && AllRows().Any(r => r.IsSelected);

    public string SelectedText => _localizer.Format("debloat.selected", ("n", AllRows().Count(r => r.IsSelected)));

    public async Task LoadAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var installed = await Task.Run(_apps.ListInstalled).ConfigureAwait(true);
            var rows = AppInventory.Build(installed, _build).Select(r => new AppRowViewModel(this, r, _localizer)).ToList();
            Groups.Clear();
            foreach (var group in Enum.GetValues<AppGroup>())
            {
                var apps = rows.Where(r => r.Row.Catalog?.Group == group).ToList();
                if (apps.Count > 0)
                {
                    Groups.Add(new AppGroupViewModel(_localizer["debloat.group." + group], apps));
                }
            }

            var other = rows.Where(r => r.Row.Catalog is null && !r.Row.Locked).ToList();
            if (other.Count > 0)
            {
                Groups.Add(new AppGroupViewModel(_localizer["debloat.group.vendor"], other));
            }

            var locked = rows.Where(r => r.Row.Locked).ToList();
            if (locked.Count > 0)
            {
                Groups.Add(new AppGroupViewModel(_localizer["debloat.group.locked"], locked));
            }
        }
#pragma warning disable CA1031 // Lesefehler der Paketverwaltung duerfen die Seite nicht beenden.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Error = _localizer.Format("perm.error", ("error", ex.Message));
        }
        finally
        {
            IsBusy = false;
            SelectionChanged();
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (!CanRemove)
        {
            return;
        }

        var selected = AllRows().Where(r => r.IsSelected).ToList();
        var body = _localizer.Format("debloat.confirm.body", ("list", string.Join(", ", selected.Select(r => r.Name))))
            + "\n\n" + _localizer["debloat.confirm.restore"]
            + (Deprovision ? "\n\n" + _localizer["debloat.confirm.deprovision"] : string.Empty);
        if (!await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["debloat.confirm.title"], body, _localizer["debloat.remove"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        IReadOnlyList<RemovedApp> removed;
        try
        {
            removed = await Task.Run(() => _apps.Remove(selected.Select(r => r.Row.PackageName).ToList(), Deprovision)).ConfigureAwait(true);
        }
        catch (AscendedTweaksException ex)
        {
            IsBusy = false;
            await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["debloat.result.title"], ex.Message, [], false)).ConfigureAwait(true);
            return;
        }

        IsBusy = false;
        var rows = removed.Select(r => new ResultRow(r.PackageName, r.Ok, r.Ok ? _localizer["debloat.result.removed"] : r.Error ?? _localizer["result.failed"])).ToList();
        await _dialogs.ShowAsync(new ResultDialogViewModel(
            _localizer["debloat.result.title"],
            _localizer.Format("result.summary", ("ok", rows.Count(r => r.Ok)), ("failed", rows.Count(r => !r.Ok))),
            rows,
            false)).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    internal void SelectionChanged()
    {
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(SelectedText));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRemove));

    private IEnumerable<AppRowViewModel> AllRows() => Groups.SelectMany(g => g.Apps);
}

/// <summary>DEB-028: OneDrive nur ohne Datenverlust-Gefahr deinstallieren; sonst Erklaerung, was zuerst zu tun ist.</summary>
public sealed partial class OneDriveViewModel : ObservableObject
{
    private readonly Func<OneDriveStatus> _check;
    private readonly Func<OneDriveStatus> _uninstall;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private OneDriveStatus? _status;

    public OneDriveViewModel(Func<OneDriveStatus> check, Func<OneDriveStatus> uninstall, IDialogService dialogs, Localizer localizer, bool readOnly)
    {
        _check = check;
        _uninstall = uninstall;
        _dialogs = dialogs;
        _localizer = localizer;
        IsReadOnly = readOnly;
    }

    public bool IsReadOnly { get; }

    public ObservableCollection<string> Reasons { get; } = [];

    public string StatusText => Status switch
    {
        null => _localizer["clean.scanning"],
        { Installed: false } => _localizer["onedrive.notinstalled"],
        { CanUninstall: true } => _localizer["onedrive.ready"],
        _ => _localizer["onedrive.blocked"],
    };

    public bool CanUninstall => Status?.CanUninstall == true && !IsBusy && !IsReadOnly;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Status = await Task.Run(_check).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Uninstall()
    {
        if (!CanUninstall || !await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["onedrive.confirm.title"], _localizer["onedrive.confirm.body"], _localizer["onedrive.uninstall"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        try
        {
            Status = await Task.Run(_uninstall).ConfigureAwait(true);
        }
        catch (AscendedTweaksException ex)
        {
            await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["onedrive.confirm.title"], ex.Message, [], false)).ConfigureAwait(true);
            Status = await Task.Run(_check).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnStatusChanged(OneDriveStatus? value)
    {
        Reasons.Clear();
        foreach (var block in value?.Blocks.Where(b => b != OneDriveBlock.NotInstalled) ?? [])
        {
            Reasons.Add(block == OneDriveBlock.OnlineOnlyFiles
                ? _localizer.Format("onedrive.block.OnlineOnlyFiles", ("n", value!.OnlineOnlyFiles.ToString(CultureInfo.CurrentCulture)))
                : block == OneDriveBlock.KnownFolderMove
                    ? _localizer.Format("onedrive.block.KnownFolderMove", ("folders", string.Join(", ", value!.RedirectedFolders.Select(f => _localizer["onedrive.folder." + FolderKey(f)]))))
                    : _localizer["onedrive.block." + block]);
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanUninstall));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanUninstall));

    private static string FolderKey(string folder) => folder switch
    {
        "Personal" => "documents",
        "My Pictures" => "pictures",
        "My Music" => "music",
        "My Video" => "videos",
        "Desktop" => "desktop",
        _ => "downloads",
    };
}

public sealed record RemovedAppRow(RemovedApp App, string When, string State);
