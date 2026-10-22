using System.Collections.ObjectModel;
using System.Globalization;
using AscendedTweaks.App.Controls;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

/// <summary>Bereinigung (Abschnitt 6.5): immer erst scannen, Groessen zeigen, auswaehlen, dann loeschen.</summary>
public sealed partial class CleanupPageViewModel : PageViewModel
{
    private readonly ICleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _useRecycleBin = true;

    [ObservableProperty]
    private bool _hasScanned;

    public CleanupPageViewModel(ICleanupService service, IDialogService dialogs, Localizer localizer, bool readOnly)
        : base("nav.cleanup", showHonestyBanner: false)
    {
        _service = service;
        _dialogs = dialogs;
        _localizer = localizer;
        IsReadOnly = readOnly;
        foreach (var category in CleanupCategories.All)
        {
            Rows.Add(new CleanupRowViewModel(this, category, localizer));
        }
    }

    public ObservableCollection<CleanupRowViewModel> Rows { get; } = [];

    public bool IsReadOnly { get; }

    public string SelectedText => _localizer.Format("clean.selected", ("size", Size(Rows.Where(r => r.IsSelected).Sum(r => r.Scan?.Bytes ?? 0))), ("n", Rows.Count(r => r.IsSelected)));

    public bool CanClean => HasScanned && !IsBusy && !IsReadOnly && Rows.Any(r => r.IsSelected);

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.0} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0.0} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0} KB"),
    };

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var row in Rows)
            {
                row.SetScanning();
                var category = row.Category;
                try
                {
                    var scan = await Task.Run(() => _service.ScanCleanup(category.Id)).ConfigureAwait(true);
                    row.SetScan(scan);
                }
                catch (AscendedTweaksException ex)
                {
                    row.SetError(ex.Message);
                }
            }

            HasScanned = true;
        }
        finally
        {
            IsBusy = false;
            SelectionChanged();
        }
    }

    [RelayCommand]
    private async Task Clean()
    {
        if (!CanClean)
        {
            return;
        }

        var selected = Rows.Where(r => r.IsSelected).ToList();
        var permanent = selected.Where(r => !UseRecycleBin || !r.Category.SupportsRecycleBin).Select(r => r.Title).ToList();
        var body = _localizer.Format("clean.confirm.body", ("size", Size(selected.Sum(r => r.Scan?.Bytes ?? 0))), ("n", selected.Count))
            + "\n\n" + _localizer["clean.confirm.noundo"]
            + (permanent.Count > 0 ? "\n\n" + _localizer.Format("clean.confirm.permanent", ("list", string.Join(", ", permanent))) : string.Empty)
            + (selected.Any(r => r.Category.Scope == TweakScope.Machine) ? "\n\n" + _localizer["clean.confirm.admin"] : string.Empty);
        if (!await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["clean.confirm.title"], body, _localizer["clean.run"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        var rows = new List<ResultRow>();
        try
        {
            foreach (var row in selected)
            {
                var mode = UseRecycleBin && row.Category.SupportsRecycleBin ? CleanupMode.RecycleBin : CleanupMode.Permanent;
                try
                {
                    var result = await Task.Run(() => _service.RunCleanup(row.Category.Id, mode)).ConfigureAwait(true);
                    rows.Add(new ResultRow(row.Title, result.Failed == 0 && result.Blocked is null, Describe(result)));
                }
                catch (AscendedTweaksException ex)
                {
                    rows.Add(new ResultRow(row.Title, false, ex.Message));
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await _dialogs.ShowAsync(new ResultDialogViewModel(
            _localizer["clean.result.title"],
            _localizer.Format("result.summary", ("ok", rows.Count(r => r.Ok)), ("failed", rows.Count(r => !r.Ok))),
            rows,
            false)).ConfigureAwait(true);
        await ScanAsync().ConfigureAwait(true);
    }

    internal void SelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(CanClean));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanClean));

    partial void OnHasScannedChanged(bool value) => OnPropertyChanged(nameof(CanClean));

    private string Describe(CleanupResult result)
    {
        if (result.Blocked is { } blocked)
        {
            return CleanupRowViewModel.BlockedText(blocked, _localizer);
        }

        var text = _localizer.Format(
            result.Mode == CleanupMode.RecycleBin ? "clean.result.recycled" : "clean.result.deleted",
            ("n", result.DeletedFiles),
            ("size", Size(result.DeletedBytes)));
        if (result.SkippedInUse > 0)
        {
            text += " " + _localizer.Format("clean.result.inuse", ("n", result.SkippedInUse));
        }

        if (result.SkippedReparsePoints > 0)
        {
            text += " " + _localizer.Format("clean.result.reparse", ("n", result.SkippedReparsePoints));
        }

        return result.Message is null ? text : $"{text} {result.Message}";
    }
}

public sealed partial class CleanupRowViewModel : ObservableObject
{
    private readonly CleanupPageViewModel _page;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private bool _canSelect;

    public CleanupRowViewModel(CleanupPageViewModel page, CleanupCategory category, Localizer localizer)
    {
        _page = page;
        _localizer = localizer;
        Category = category;
        SizeText = localizer["clean.notscanned"];
    }

    public CleanupCategory Category { get; }

    public CleanupScan? Scan { get; private set; }

    public string Title => _localizer[$"clean.{Category.Id}.name"];

    public string Description => _localizer[$"clean.{Category.Id}.desc"];

    public string SideEffects => _localizer[$"clean.{Category.Id}.side"];

    public BadgeTone RiskTone => BadgeStyle.Risk(Category.Risk).Tone;

    public Wpf.Ui.Controls.SymbolRegular RiskSymbol => BadgeStyle.Risk(Category.Risk).Symbol;

    public string RiskText => _localizer[BadgeStyle.Risk(Category.Risk).Key];

    public bool NeedsAdmin => Category.Scope == TweakScope.Machine;

    public string ModeText => _localizer[Category.SupportsRecycleBin ? "clean.mode.recyclable" : "clean.mode.permanent"];

    public static string BlockedText(string blocked, Localizer localizer) =>
        blocked.StartsWith("browser-open:", StringComparison.Ordinal)
            ? localizer.Format("clean.blocked.browser", ("name", blocked["browser-open:".Length..]))
            : blocked;

    partial void OnIsSelectedChanged(bool value) => _page.SelectionChanged();

    internal void SetScanning()
    {
        SizeText = _localizer["clean.scanning"];
        Note = null;
    }

    internal void SetError(string message)
    {
        Scan = null;
        SizeText = _localizer["clean.unknown"];
        Note = message;
        CanSelect = false;
        IsSelected = false;
    }

    internal void SetScan(CleanupScan scan)
    {
        Scan = scan;
        SizeText = scan.SizeUnknown ? _localizer["clean.unknown"] : _localizer.Format("clean.size", ("size", CleanupPageViewModel.Size(scan.Bytes)), ("n", scan.Files));
        var notes = new List<string>();
        if (scan.Blocked is { } blocked)
        {
            notes.Add(BlockedText(blocked, _localizer));
        }

        if (scan.Estimated)
        {
            notes.Add(_localizer["clean.estimated"]);
        }

        if (scan.SkippedReparsePoints > 0)
        {
            notes.Add(_localizer.Format("clean.result.reparse", ("n", scan.SkippedReparsePoints)));
        }

        Note = notes.Count == 0 ? null : string.Join(" ", notes);
        CanSelect = scan.Blocked is null && (!scan.NothingToDo || scan.Estimated);
        IsSelected = CanSelect && Category.SelectedByDefault && !scan.NothingToDo;
    }
}
