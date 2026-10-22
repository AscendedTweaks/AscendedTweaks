using System.Collections.ObjectModel;
using System.Globalization;
using AscendedTweaks.App.Controls;
using AscendedTweaks.App.Services;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Insights;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.ViewModels;

public sealed record SectionItem(string Key, string TitleKey)
{
    /// <summary>Screenreader lesen Listeneintraege ueber ToString vor: den uebersetzten Titel, nicht den Typnamen.</summary>
    public override string ToString() => AscendedTweaks.Localization.Localizer.Current[TitleKey];
}

/// <summary>Leistung (Abschnitt 6.2): Tweak-Karten plus Autostart, Dienste, Aufgaben und Prozesse als eigene Bereiche.</summary>
public sealed partial class PerformancePageViewModel : PageViewModel
{
    private readonly Dictionary<string, Func<object>> _factories;
    private readonly Dictionary<string, object> _created = [];

    [ObservableProperty]
    private SectionItem _selectedSection;

    [ObservableProperty]
    private object? _currentSection;

    public PerformancePageViewModel(
        TweakWorkspace workspace,
        Func<AutostartViewModel> autostart,
        Func<CatalogServicesViewModel> services,
        Func<CatalogTasksViewModel> tasks,
        Func<ProcessesViewModel> processes)
        : base("nav.performance", showHonestyBanner: true)
    {
        Sections =
        [
            new("tweaks", "perf.section.tweaks"),
            new("autostart", "perf.section.autostart"),
            new("services", "perf.section.services"),
            new("tasks", "perf.section.tasks"),
            new("processes", "perf.section.processes"),
        ];
        _factories = new Dictionary<string, Func<object>>(StringComparer.Ordinal)
        {
            ["tweaks"] = () => new TweakListPageViewModel("nav.performance", workspace, "PERF", "BROW"),
            ["autostart"] = () => Loaded(autostart(), a => a.LoadAsync()),
            ["services"] = () => Loaded(services(), s => s.LoadAsync()),
            ["tasks"] = () => Loaded(tasks(), t => t.LoadAsync()),
            ["processes"] = () => Loaded(WithJump(processes()), p => p.MeasureAsync()),
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

    private ProcessesViewModel WithJump(ProcessesViewModel model)
    {
        model.OpenAutostart = () => SelectedSection = Sections.First(s => s.Key == "autostart");
        return model;
    }

    private static T Loaded<T>(T model, Func<T, Task> load)
    {
        _ = load(model);
        return model;
    }

    /// <summary>Bereiche werden erst beim ersten Aufruf erzeugt und danach wiederverwendet.</summary>
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

public sealed record AutostartRow(
    AutostartEntry Entry,
    string Detail,
    string StateText,
    BadgeTone RatingTone,
    string RatingText,
    string Reason,
    string? Note,
    bool CanDisable,
    bool CanEnable,
    string? Memory = null);

/// <summary>Autostart-Manager: Anzeige mit Bewertung; Deaktivieren und Wieder-Aktivieren laufen ueber PERF-026 der Engine.</summary>
public sealed partial class AutostartViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<AutostartEntry>> _read;
    private readonly TweakWorkspace _workspace;
    private readonly Localizer _localizer;
    private readonly IProcessSampler? _processes;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    public AutostartViewModel(Func<IReadOnlyList<AutostartEntry>> read, TweakWorkspace workspace, IProcessSampler? processes = null)
    {
        _read = read;
        _workspace = workspace;
        _localizer = workspace.Localizer;
        _processes = processes;
    }

    public ObservableCollection<AutostartRow> Rows { get; } = [];

    public bool IsEmpty => !IsLoading && Error is null && Rows.Count == 0;

    public async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var (entries, applied, running) = await Task.Run(() => (_read(), _workspace.Service.GetAppliedTweaks().Select(e => e.TweakId).ToHashSet(StringComparer.Ordinal), Sample())).ConfigureAwait(true);
            Rows.Clear();
            foreach (var entry in entries)
            {
                Rows.Add(ToRow(entry, applied, running));
            }
        }
#pragma warning disable CA1031 // Reine Anzeige: ein Lesefehler darf die Seite nicht beenden.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Error = _localizer.Format("perm.error", ("error", ex.Message));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task Reload() => LoadAsync();

    [RelayCommand]
    private async Task Disable(AutostartRow row)
    {
        if (row?.CanDisable == true)
        {
            await _workspace.ApplyIdsAsync([row.Entry.TweakId!]).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task Enable(AutostartRow row)
    {
        if (row?.CanEnable == true)
        {
            await _workspace.RevertAsync(row.Entry.TweakId!).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Laufende Prozesse fuer die RAM-Angabe; ohne Sampler oder bei einem Lesefehler keine Zahlen.</summary>
    private IReadOnlyList<ProcessSample> Sample()
    {
        try
        {
            return _processes?.Sample() ?? [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private AutostartRow ToRow(AutostartEntry entry, IReadOnlySet<string> applied, IReadOnlyList<ProcessSample> running)
    {
        var disabledByApp = entry.TweakId is not null && applied.Contains(entry.TweakId);
        var publisher = entry.Signature.Status == SignatureStatus.Valid && entry.Signature.Publisher is { } p ? p : _localizer["autostart.unsigned"];
        var parts = new List<string> { publisher, _localizer["autostart.source." + entry.Source] };
        if (entry.MachineWide)
        {
            parts.Add(_localizer["autostart.machine"]);
        }

        if (entry.FilePath is { } path)
        {
            parts.Add(path);
        }

        string? note = null;
        if (entry.TweakId is null)
        {
            note = _localizer[entry.Source == AutostartSource.LogonTask ? "autostart.readonly.task" : "autostart.readonly.runonce"];
        }
        else if (!entry.Enabled && !disabledByApp)
        {
            note = _localizer["autostart.disabled.external"];
        }

        var (tone, ratingKey) = entry.Rating switch
        {
            AutostartRating.Keep => (BadgeTone.Warning, "autostart.rating.Keep"),
            AutostartRating.Optional => (BadgeTone.Success, "autostart.rating.Optional"),
            _ => (BadgeTone.Danger, "autostart.rating.Unknown"),
        };

        return new AutostartRow(
            entry,
            string.Join(" · ", parts),
            _localizer[entry.Enabled ? "autostart.enabled" : "autostart.disabled"],
            tone,
            _localizer[ratingKey],
            _localizer[entry.ReasonKey],
            note,
            entry.CanDisable && !_workspace.IsReadOnly,
            !entry.Enabled && disabledByApp && !_workspace.IsReadOnly,
            entry.Known is { } known && KnownStartupApps.RunningMemory(known, running) is { } bytes
                ? _localizer.Format("autostart.memory", ("value", ProcessesViewModel.Megabytes(bytes, CultureInfo.GetCultureInfo(_localizer.Culture))))
                : null);
    }
}

public sealed record CatalogServiceRow(TweakCardViewModel Card, string Service, string DefaultText, string CurrentText, string TargetText);

/// <summary>Dienste-Manager: nur Dienste aus dem Katalog, keine Massenliste (Abschnitt 6.2).</summary>
public sealed partial class CatalogServicesViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;
    private readonly IServiceAccess _services;

    public CatalogServicesViewModel(TweakWorkspace workspace, IServiceAccess services)
    {
        _workspace = workspace;
        _services = services;
    }

    public ObservableCollection<CatalogServiceRow> Rows { get; } = [];

    public static string StartKey(ServiceStartSetting setting) => "start." + setting;

    public static string StartKey(ServiceConfig? config) => config is null
        ? "start.missing"
        : config.StartType switch
        {
            ServiceStartType.Automatic when config.DelayedAutoStart => "start.AutomaticDelayed",
            ServiceStartType.Automatic => "start.Automatic",
            ServiceStartType.Manual => "start.Manual",
            _ => "start.Disabled",
        };

    public async Task LoadAsync()
    {
        var items = _workspace.Cards
            .SelectMany(card => card.Tweak.Apply.OfType<ServiceSetStartTypeAction>().Select(action => (card, action)))
            .ToList();
        var configs = await Task.Run(() => items.Select(i => SafeConfig(i.action.Service)).ToList()).ConfigureAwait(true);
        var loc = _workspace.Localizer;
        Rows.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var (card, action) = items[i];
            Rows.Add(new CatalogServiceRow(
                card,
                action.Service,
                loc.Format("services.default", ("value", action.WindowsDefault is { } d ? loc[StartKey(d)] : loc["services.unknown"])),
                loc.Format("services.current", ("value", loc[StartKey(configs[i])])),
                loc.Format("services.target", ("value", loc[StartKey(action.StartType)]))));
        }
    }

    [RelayCommand]
    private void Details(CatalogServiceRow row) => _workspace.OpenDetails(row.Card);

    private ServiceConfig? SafeConfig(string service)
    {
        try
        {
            return _services.GetConfig(service);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or AscendedTweaksException)
        {
            return null;
        }
    }
}

public sealed record CatalogTaskRow(TweakCardViewModel Card, string TaskPath, string CurrentText);

/// <summary>Geplante Aufgaben aus dem Katalog mit Ist-Zustand.</summary>
public sealed partial class CatalogTasksViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;
    private readonly IScheduledTaskAccess _tasks;

    public CatalogTasksViewModel(TweakWorkspace workspace, IScheduledTaskAccess tasks)
    {
        _workspace = workspace;
        _tasks = tasks;
    }

    public ObservableCollection<CatalogTaskRow> Rows { get; } = [];

    public async Task LoadAsync()
    {
        var items = _workspace.Cards
            .SelectMany(card => card.Tweak.Apply.OfType<ScheduledTaskSetEnabledAction>().Select(action => (card, action)))
            .ToList();
        var states = await Task.Run(() => items.Select(i => SafeState(i.action.TaskPath)).ToList()).ConfigureAwait(true);
        var loc = _workspace.Localizer;
        Rows.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            var key = states[i] switch
            {
                true => "tasks.enabled",
                false => "tasks.disabled",
                null => "tasks.missing",
            };
            Rows.Add(new CatalogTaskRow(items[i].card, items[i].action.TaskPath, loc[key]));
        }
    }

    [RelayCommand]
    private void Details(CatalogTaskRow row) => _workspace.OpenDetails(row.Card);

    private bool? SafeState(string path)
    {
        try
        {
            return _tasks.GetEnabled(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AscendedTweaksException)
        {
            return null;
        }
    }
}

public sealed record ProcessRow(string Name, string Count, string Value, string? Explanation = null, bool CanJump = false);

/// <summary>Prozess- und Speicher-Uebersicht, rein lesend.</summary>
public sealed partial class ProcessesViewModel : ObservableObject
{
    private readonly ProcessOverview _overview;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isMeasuring;

    /// <summary>Gesetzt von der Leistungsseite: wechselt zum Autostart-Bereich.</summary>
    public Action? OpenAutostart { get; set; }

    [ObservableProperty]
    private string? _totalText;

    public ProcessesViewModel(ProcessOverview overview, Localizer localizer)
    {
        _overview = overview;
        _localizer = localizer;
    }

    public ObservableCollection<ProcessRow> ByMemory { get; } = [];

    public ObservableCollection<ProcessRow> ByCpu { get; } = [];

    public static string Megabytes(long bytes, CultureInfo culture) => string.Create(culture, $"{bytes / 1024.0 / 1024.0:N0} MB");

    [RelayCommand]
    public async Task MeasureAsync()
    {
        if (IsMeasuring)
        {
            return;
        }

        IsMeasuring = true;
        try
        {
            var result = await _overview.MeasureAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
            var culture = CultureInfo.GetCultureInfo(_localizer.Culture);
            ByMemory.Clear();
            foreach (var g in result.ByMemory)
            {
                var key = KnownProcesses.KeyFor(g.Name);
                ByMemory.Add(new ProcessRow(g.Name, Count(g), Megabytes(g.WorkingSetBytes, culture), _localizer[key], key.StartsWith("autostart.known.", StringComparison.Ordinal) && OpenAutostart is not null));
            }

            ByCpu.Clear();
            foreach (var g in result.ByCpu)
            {
                ByCpu.Add(new ProcessRow(g.Name, Count(g), string.Create(culture, $"{g.CpuPercent:0.0} %")));
            }

            TotalText = _localizer.Format("processes.total", ("value", Megabytes(result.TotalWorkingSetBytes, culture)));
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private string Count(ProcessGroup group) => group.Count > 1 ? _localizer.Format("processes.count", ("n", group.Count)) : string.Empty;

    [RelayCommand]
    private void JumpToAutostart() => OpenAutostart?.Invoke();
}
