using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Engine.Tools;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

/// <summary>"Immer behalten"-Liste (V2 Abschnitt 6.3): Dateien oder Ordner, die nie wieder angezeigt werden. Pro Benutzer, nur lokal.</summary>
public interface IKeepListStore
{
    IReadOnlyList<string> All();

    void Add(string path);

    void Remove(string path);
}

public sealed class KeepListStore : IKeepListStore
{
    private readonly string _path;

    public KeepListStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "KeepList.json");

    public IReadOnlyList<string> All()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Add(string path)
    {
        var list = All().ToList();
        if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
            Save(list);
        }
    }

    public void Remove(string path) => Save(All().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());

    private void Save(List<string> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(list));
    }
}

/// <summary>Ansicht der "Immer behalten"-Liste in den Einstellungen: Eintraege sehen und entfernen.</summary>
public sealed partial class KeepListViewModel : ObservableObject
{
    private readonly IKeepListStore _store;

    public KeepListViewModel(IKeepListStore store)
    {
        _store = store;
        Reload();
    }

    public ObservableCollection<string> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    public void Reload()
    {
        Entries.Clear();
        foreach (var entry in _store.All())
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Remove(string entry)
    {
        _store.Remove(entry);
        Reload();
    }
}

public sealed partial class LargeFileRowViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isSelected;

    public LargeFileRowViewModel(LargeFileItem item, string why, string? duplicate, CultureInfo culture, Action changed)
    {
        Item = item;
        Why = why;
        Duplicate = duplicate;
        Name = Path.GetFileName(item.Path);
        Folder = Path.GetDirectoryName(item.Path) ?? item.Path;
        Size = CleanupPageViewModel.Size(item.SizeBytes);
        When = item.LastWriteUtc.ToLocalTime().ToString("d", culture);
        _changed = changed;
    }

    public LargeFileItem Item { get; }

    public string Name { get; }

    public string Folder { get; }

    public string Size { get; }

    public string When { get; }

    /// <summary>Klartext "Warum sehe ich das?".</summary>
    public string Why { get; }

    /// <summary>"Original" oder "Kopie" bei Duplikat-Gruppen.</summary>
    public string? Duplicate { get; }

    public bool CanSelect => Item.Selectable;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect)
        {
            IsSelected = false;
            return;
        }

        _changed();
    }
}

public sealed record GameFolderRow(GameFolder Game, string Size);

public sealed record ThresholdOption(long Bytes, string Label);

/// <summary>
/// Grosse Dateien (V2 Abschnitt 6): Klassifikation vor Anzeige. Gruppe A "Kann meist weg" und B "Bitte pruefen" sind sichtbar,
/// C und D nur nach "Auch geschuetzte Bereiche anzeigen" und dann nur mit "Im Explorer zeigen". Nichts ist vorausgewaehlt;
/// geloescht wird nur in den Papierkorb, nach Rueckfrage mit Anzahl, Groesse und den drei groessten Dateien.
/// </summary>
public sealed partial class LargeFilesViewModel : ObservableObject
{
    private readonly IDiskTools _tools;
    private readonly Localizer _localizer;
    private readonly IDialogService? _dialogs;
    private readonly IKeepListStore? _keep;
    private readonly Action<string>? _open;
    private CancellationTokenSource? _cancel;
    private IReadOnlyList<string> _gameRoots = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _showProtected;

    [ObservableProperty]
    private ThresholdOption _threshold;

    public LargeFilesViewModel(IDiskTools tools, Localizer localizer, IDialogService? dialogs = null, IKeepListStore? keep = null, Action<string>? open = null, bool expert = false)
    {
        _tools = tools;
        _localizer = localizer;
        _dialogs = dialogs;
        _keep = keep;
        _open = open;
        IsExpert = expert;
        Thresholds =
        [
            new(250L << 20, "250 MB"),
            new(LargeFileScanner.DefaultThreshold, "500 MB"),
            new(1L << 30, "1 GB"),
            new(5L << 30, "5 GB"),
        ];
        _threshold = Thresholds[1];
    }

    /// <summary>Schwelle nur im Experten-Modus einstellbar (Standard 500 MB).</summary>
    public bool IsExpert { get; }

    public IReadOnlyList<ThresholdOption> Thresholds { get; }

    public ObservableCollection<LargeFileRowViewModel> GroupA { get; } = [];

    public ObservableCollection<LargeFileRowViewModel> GroupB { get; } = [];

    public ObservableCollection<LargeFileRowViewModel> Protected { get; } = [];

    public ObservableCollection<GameFolderRow> Games { get; } = [];

    public bool HasResults => GroupA.Count + GroupB.Count + Protected.Count > 0;

    public bool CanRecycle => !IsScanning && _dialogs is not null && Selected.Count > 0;

    public string RecycleText => Selected.Count == 0
        ? _localizer["large.recycle.none"]
        : _localizer.Format("large.recycle", ("n", Selected.Count), ("size", CleanupPageViewModel.Size(Selected.Sum(r => r.Item.SizeBytes))));

    private List<LargeFileRowViewModel> Selected => GroupA.Concat(GroupB).Where(r => r.IsSelected && r.CanSelect).ToList();

    [RelayCommand]
    private async Task Scan()
    {
        if (IsScanning)
        {
            _cancel?.Cancel();
            return;
        }

        IsScanning = true;
        _cancel = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        var progress = new Progress<int>(n => Status = _localizer.Format("large.progress", ("n", n), ("rate", (int)(n / Math.Max(0.5, watch.Elapsed.TotalSeconds)))));
        try
        {
            var threshold = IsExpert ? Threshold.Bytes : LargeFileScanner.DefaultThreshold;
            var keep = _keep?.All() ?? [];
            var token = _cancel.Token;
            var scan = await Task.Run(() => _tools.ScanLargeFiles(threshold, keep, progress, token), token).ConfigureAwait(true);
            Show(scan);
            Status = _localizer.Format(scan.TimedOut ? "large.timeout" : scan.Truncated ? "large.truncated" : "large.done", ("a", GroupA.Count), ("b", GroupB.Count), ("hidden", Protected.Count));
        }
        catch (OperationCanceledException)
        {
            Status = _localizer["disk.large.cancelled"];
        }
        finally
        {
            IsScanning = false;
            _cancel.Dispose();
            _cancel = null;
            Changed();
        }
    }

    private void Show(LargeFileScan scan)
    {
        _gameRoots = scan.GameRoots;
        GroupA.Clear();
        GroupB.Clear();
        Protected.Clear();
        Games.Clear();
        var culture = CultureInfo.GetCultureInfo(_localizer.Culture);
        foreach (var item in scan.Items)
        {
            var duplicate = item.DuplicateGroup is null ? null : _localizer[item.IsOriginal ? "large.original" : "large.copy"];
            var why = _localizer.Format(item.ReasonKey, item.Args.ToArray());
            var row = new LargeFileRowViewModel(item, why, duplicate, culture, Changed);
            (item.Class switch
            {
                LargeFileClass.A => GroupA,
                LargeFileClass.B => GroupB,
                _ => Protected,
            }).Add(row);
        }

        foreach (var game in scan.Games)
        {
            Games.Add(new GameFolderRow(game, CleanupPageViewModel.Size(game.SizeBytes)));
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(CanRecycle));
        OnPropertyChanged(nameof(RecycleText));
        RecycleCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Alle in Gruppe A auswaehlen" ist erlaubt, fuer Gruppe B und Duplikate nie.</summary>
    [RelayCommand]
    private void SelectAllA()
    {
        foreach (var row in GroupA.Where(r => r.CanSelect && r.Item.DuplicateGroup is null))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private async Task Recycle()
    {
        var selected = Selected;
        if (_dialogs is null || selected.Count == 0)
        {
            return;
        }

        var largest = string.Join(", ", selected.OrderByDescending(r => r.Item.SizeBytes).Take(3).Select(r => r.Name));
        var body = _localizer.Format("large.confirm.body", ("n", selected.Count), ("size", CleanupPageViewModel.Size(selected.Sum(r => r.Item.SizeBytes))), ("largest", largest));
        if (!await _dialogs.ShowAsync(new ConfirmDialogViewModel(_localizer["large.confirm.title"], body, _localizer["large.confirm.yes"], _localizer["dialog.cancel"])).ConfigureAwait(true))
        {
            return;
        }

        var items = selected.Select(r => r.Item).ToList();
        var outcome = await Task.Run(() => _tools.RecycleLargeFiles(items, _gameRoots)).ConfigureAwait(true);
        foreach (var row in selected.Where(r => outcome.Recycled.Contains(r.Item.Path, StringComparer.OrdinalIgnoreCase)))
        {
            GroupA.Remove(row);
            GroupB.Remove(row);
        }

        var rows = outcome.Recycled.Select(p => new ResultRow(Path.GetFileName(p), true, _localizer["large.result.recycled"]))
            .Concat(outcome.Skipped.Select(s => new ResultRow(Path.GetFileName(s.Path), false, _localizer[s.ReasonKey])))
            .ToList();
        await _dialogs.ShowAsync(new ResultDialogViewModel(
            _localizer["result.title"],
            _localizer.Format("result.summary", ("ok", outcome.Recycled.Count), ("failed", outcome.Skipped.Count)),
            rows,
            false)).ConfigureAwait(true);
        Changed();
    }

    [RelayCommand]
    private void Show(LargeFileRowViewModel row) => _tools.ShowInExplorer(row.Item.Path);

    /// <summary>"Nie wieder anzeigen" fuer diese Datei.</summary>
    [RelayCommand]
    private void KeepFile(LargeFileRowViewModel row) => Keep(row, row.Item.Path);

    /// <summary>"Nie wieder anzeigen" fuer den ganzen Ordner.</summary>
    [RelayCommand]
    private void KeepFolder(LargeFileRowViewModel row) => Keep(row, row.Folder);

    private void Keep(LargeFileRowViewModel row, string path)
    {
        _keep?.Add(path);
        foreach (var list in new[] { GroupA, GroupB, Protected })
        {
            foreach (var hidden in list.Where(r => string.Equals(r.Item.Path, path, StringComparison.OrdinalIgnoreCase) || LargeFileScanner.IsUnder(r.Item.Path, path)).ToList())
            {
                list.Remove(hidden);
            }
        }

        Changed();
    }

    /// <summary>Spiele werden nie von TweakPilot geloescht; "Im Launcher deinstallieren" oeffnet die App-Liste von Windows.</summary>
    [RelayCommand]
    private void Uninstall(GameFolderRow game) => _open?.Invoke("ms-settings:appsfeatures");

    [RelayCommand]
    private void ShowGame(GameFolderRow game) => _tools.ShowInExplorer(game.Game.Path);
}

/// <summary>Einfach-Modus "Aufraeumen": dieselbe Bereinigung wie im Experten-Modus, aber mit schlanker Ansicht (eine Hauptaktion).</summary>
public sealed record SimpleCleanupView(CleanupPageViewModel Cleanup);
