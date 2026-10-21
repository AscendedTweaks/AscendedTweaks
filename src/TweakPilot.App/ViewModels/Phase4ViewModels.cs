using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Services;
using TweakPilot.Core;
using TweakPilot.Engine;
using TweakPilot.Engine.State;
using TweakPilot.Localization;

namespace TweakPilot.App.ViewModels;

/// <summary>Seite mit Tweak-Karten eines oder mehrerer Module.</summary>
public sealed class TweakListPageViewModel : PageViewModel
{
    public TweakListPageViewModel(string titleKey, TweakWorkspace workspace, params string[] modules)
        : this(titleKey, workspace, null, modules)
    {
    }

    public TweakListPageViewModel(string titleKey, TweakWorkspace workspace, PermissionOverviewViewModel? permissions, params string[] modules)
        : base(titleKey, showHonestyBanner: true)
    {
        Workspace = workspace;
        Permissions = permissions;
        Cards = workspace.CardsFor(modules);
    }

    public TweakWorkspace Workspace { get; }

    /// <summary>Nur auf der Datenschutzseite gesetzt (Abschnitt 6.4).</summary>
    public PermissionOverviewViewModel? Permissions { get; }

    public IReadOnlyList<TweakCardViewModel> Cards { get; }
}

public sealed record AppliedRow(string TweakId, string Name, string When, TweakScope Scope);

public sealed record BackupRow(string Id, string When, string Tweaks, string Size, bool IsBaseline, bool CanDelete, TweakScope Scope, string Path);

public sealed record InterruptedRow(string BatchId, string Tweaks, TweakScope Scope);

/// <summary>Wiederherstellungsseite (Abschnitt 7.6).</summary>
public sealed partial class RestorePageViewModel : PageViewModel
{
    private readonly TweakWorkspace _workspace;
    private readonly IDialogService _dialogs;
    private readonly Localizer _localizer;
    private readonly IAppManager? _apps;

    [ObservableProperty]
    private bool _isBusy;

    public RestorePageViewModel(TweakWorkspace workspace, IDialogService dialogs, IAppManager? apps = null)
        : base("nav.restore", showHonestyBanner: false)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _localizer = workspace.Localizer;
        _apps = apps;
    }

    /// <summary>Entfernte Apps (Abschnitt 6.6): Wiederherstellung versucht Neuregistrierung, sonst Store-Seite.</summary>
    public ObservableCollection<RemovedAppRow> RemovedApps { get; } = [];

    public bool HasRemovedApps => RemovedApps.Count > 0;

    [RelayCommand]
    private async Task RestoreApp(RemovedAppRow row)
    {
        if (_apps is null || row is null)
        {
            return;
        }

        var outcome = await Task.Run(() => _apps.Restore(row.App)).ConfigureAwait(true);
        await _dialogs.ShowAsync(new ResultDialogViewModel(
            _localizer["restore.apps.title"],
            _localizer["restore.apps.outcome." + outcome],
            [new ResultRow(row.App.PackageName, outcome != TweakPilot.Engine.Debloat.RestoreOutcome.Failed, _localizer["restore.apps.outcome." + outcome])],
            false)).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    public ObservableCollection<AppliedRow> Applied { get; } = [];

    public ObservableCollection<BackupRow> Backups { get; } = [];

    public ObservableCollection<InterruptedRow> Interrupted { get; } = [];

    public bool HasInterrupted => Interrupted.Count > 0;

    public bool HasApplied => Applied.Count > 0;

    public bool HasBackups => Backups.Count > 0;

    public IReadOnlyList<string> LimitKeys { get; } = ["restore.limit.files", "restore.limit.apps", "restore.limit.restorepoint", "restore.limit.personal"];

    public async Task LoadAsync()
    {
        var (applied, backups, interrupted) = await Task.Run(() => (
            _workspace.Service.GetAppliedTweaks(),
            _workspace.Service.ListBackupSets(),
            _workspace.Service.GetInterruptedBatches())).ConfigureAwait(true);
        var culture = CultureInfo.GetCultureInfo(_localizer.Culture);

        Applied.Clear();
        foreach (var entry in applied.OrderByDescending(e => e.AppliedUtc))
        {
            var scope = _workspace.ScopeOf(entry.TweakId);
            Applied.Add(new AppliedRow(entry.TweakId, _workspace.NameOf(entry.TweakId), entry.AppliedUtc.ToLocalTime().ToString("g", culture), scope));
        }

        Backups.Clear();
        foreach (var (scope, set) in backups)
        {
            var tweaks = set.IsBaseline ? _localizer["restore.baseline"] : string.Join(", ", set.TweakIds.Select(_workspace.NameOf));
            Backups.Add(new BackupRow(
                set.Id,
                set.CreatedUtc.ToLocalTime().ToString("g", culture),
                tweaks,
                string.Create(culture, $"{set.SizeBytes / 1024.0:0.#} KB"),
                set.IsBaseline,
                scope == TweakScope.User && !set.IsBaseline && !applied.Any(a => a.BackupSetId == set.Id),
                scope,
                set.Path));
        }

        Interrupted.Clear();
        foreach (var (scope, batch) in interrupted)
        {
            Interrupted.Add(new InterruptedRow(batch.BatchId, string.Join(", ", batch.Tweaks.Select(t => _workspace.NameOf(t.Id))), scope));
        }

        RemovedApps.Clear();
        if (_apps is not null)
        {
            var removed = await Task.Run(_apps.RemovedApps).ConfigureAwait(true);
            foreach (var app in removed.OrderByDescending(a => a.RemovedUtc))
            {
                var state = app.Restored switch
                {
                    true => _localizer["restore.apps.restored"],
                    _ => _localizer["restore.apps.removed"],
                };
                RemovedApps.Add(new RemovedAppRow(app, app.RemovedUtc.ToLocalTime().ToString("g", culture), state));
            }
        }

        OnPropertyChanged(nameof(HasRemovedApps));
        OnPropertyChanged(nameof(HasInterrupted));
        OnPropertyChanged(nameof(HasApplied));
        OnPropertyChanged(nameof(HasBackups));
    }

    [RelayCommand]
    private async Task Reset(string tweakId)
    {
        await _workspace.RevertAsync(tweakId).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>"Alles rueckgaengig machen": alle Tweaks in umgekehrter Reihenfolge, danach Vergleich mit dem Ausgangszustand.</summary>
    [RelayCommand]
    private async Task UndoAll()
    {
        if (IsBusy || !await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["restore.undoall"], _localizer["restore.undoall.body"], _localizer["restore.undoall"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var results = await Task.Run(() => _workspace.Service.RevertAll(_workspace.Resolver)).ConfigureAwait(true);
            var deviations = await Task.Run(() => _workspace.Service.CompareWithBaseline()).ConfigureAwait(true);
            var rows = results.Select(r => new ResultRow(
                _workspace.NameOf(r.TweakId),
                r.Outcome == RevertOutcome.Reverted,
                r.Outcome == RevertOutcome.Reverted ? _localizer["result.reverted"] : r.Error?.Message ?? _localizer["result.cancelled"])).ToList();

            // Abweichungen vom Ausgangszustand offen zeigen (Abschnitt 7.3), zum Beispiel von Windows geaenderte Werte.
            rows.AddRange(deviations.Select(d => new ResultRow(
                _workspace.NameOf(d.TweakId),
                false,
                _localizer.Format("restore.deviation", ("target", d.View.Target), ("current", d.View.Current), ("original", d.View.Original)))));
            var summary = deviations.Count == 0 ? _localizer["restore.matchesbaseline"] : _localizer.Format("restore.deviations", ("n", deviations.Count));
            await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["result.title.revert"], summary, rows, false)).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            await _workspace.RefreshAsync().ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task Recover(InterruptedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var errors = await Task.Run(() => _workspace.Service.RecoverInterrupted(row.Scope, row.BatchId)).ConfigureAwait(true);
        var rows = errors.Count == 0
            ? [new ResultRow(row.Tweaks, true, _localizer["restore.recovered"])]
            : errors.Select(e => new ResultRow(row.Tweaks, false, e)).ToList();
        await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["restore.recover"], string.Empty, rows, false)).ConfigureAwait(true);
        await _workspace.RefreshAsync().ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteBackup(BackupRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.CanDelete || !await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["restore.delete.title"], _localizer["restore.delete.body"], _localizer["restore.delete"], _localizer["dialog.cancel"], danger: true)).ConfigureAwait(true))
        {
            return;
        }

        _workspace.Service.DeleteUserBackupSet(row.Id);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private static void OpenRestorePoint()
    {
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "rstrui.exe")) { UseShellExecute = true });
    }

    [RelayCommand]
    private static void OpenBackupFolder()
    {
        Directory.CreateDirectory(StateStore.DefaultUserRoot);
        Process.Start(new ProcessStartInfo(StateStore.DefaultUserRoot) { UseShellExecute = true });
    }
}

/// <summary>One language button in First steps: label in its own language, check mark when active.</summary>
public sealed record LanguageOption(string Culture, string Label, bool IsActive);

/// <summary>First steps on first start (V3): 1 Welcome and language, 2 Safety and limits with required check, 3 Back up your current state.</summary>
public sealed partial class FirstStepsViewModel : DialogViewModel<bool>
{
    private readonly TweakWorkspace _workspace;
    private readonly Localizer _localizer;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(IsLastStep), nameof(StepText))]
    private int _step = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private bool _understood;

    [ObservableProperty]
    private string _baselineStatus = string.Empty;

    [ObservableProperty]
    private string _restorePointStatus = string.Empty;

    [ObservableProperty]
    private bool _isWorking;

    public FirstStepsViewModel(TweakWorkspace workspace, AppSettings settings, SettingsStore store, SettingsViewModel preferences)
    {
        _workspace = workspace;
        _localizer = workspace.Localizer;
        _settings = settings;
        _store = store;
        Preferences = preferences;
        _localizer.PropertyChanged += (_, _) => LanguageChanged();
    }

    public SettingsViewModel Preferences { get; }

    /// <summary>English and Deutsch, each labeled in its own language.</summary>
    public IReadOnlyList<LanguageOption> Languages =>
    [
        new(Localizer.English, "English", _localizer.Culture == Localizer.English),
        new(Localizer.German, "Deutsch", _localizer.Culture == Localizer.German),
    ];

    /// <summary>Switches every visible text live and saves the choice (through the same setting as Settings).</summary>
    [RelayCommand]
    private void SelectLanguage(string culture)
    {
        if (Preferences.Languages.FirstOrDefault(l => l.Value == culture) is { } choice)
        {
            Preferences.SelectedLanguage = choice;
        }

        LanguageChanged();
    }

    private void LanguageChanged()
    {
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(StepText));
    }

    public IReadOnlyList<string> LimitKeys { get; } = ["restore.limit.files", "restore.limit.apps", "restore.limit.restorepoint", "restore.limit.personal"];

    /// <summary>Weiter erst nach dem Pflicht-Haken "Ich habe verstanden" (Schritt 2).</summary>
    public bool CanContinue => Step != 2 || Understood;

    public const int Steps = 3;

    public bool IsLastStep => Step == Steps;

    public string StepText => _localizer.Format("firststeps.step", ("n", Step), ("total", Steps));

    public override void Cancel()
    {
        // Ohne Zustimmung (Schritt 2) startet die App nicht in den Bearbeitungsmodus; Abbrechen beendet nur den Dialog.
        Close(false);
    }

    [RelayCommand]
    private void Next()
    {
        if (!CanContinue)
        {
            return;
        }

        if (Step < Steps)
        {
            Step++;
            return;
        }

        _settings.FirstStepsCompleted = true;
        _store.Save(_settings);
        Close(true);
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 1)
        {
            Step--;
        }
    }

    [RelayCommand]
    private async Task CaptureBaseline()
    {
        IsWorking = true;
        try
        {
            var result = await Task.Run(_workspace.Service.CaptureBaseline).ConfigureAwait(true);
            BaselineStatus = result.AlreadyExisted
                ? _localizer["firststeps.baseline.exists"]
                : _localizer.Format("firststeps.baseline.done", ("n", result.CapturedPlaces), ("skipped", result.Skipped.Count));
        }
        catch (TweakPilotException ex)
        {
            BaselineStatus = ex.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        IsWorking = true;
        try
        {
            var result = await Task.Run(() => _workspace.Service.CreateRestorePoint($"{ProductInfo.Name} Ausgangszustand")).ConfigureAwait(true);
            RestorePointStatus = result.Message;
        }
        catch (TweakPilotException ex)
        {
            RestorePointStatus = ex.Message;
        }
        finally
        {
            IsWorking = false;
        }
    }
}
