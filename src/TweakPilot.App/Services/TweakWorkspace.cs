using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TweakPilot.App.Controls;
using TweakPilot.Catalog;
using TweakPilot.Core;
using TweakPilot.Engine;
using TweakPilot.Ipc;
using TweakPilot.Localization;
using Wpf.Ui.Controls;

namespace TweakPilot.App.Services;

public static class BadgeStyle
{
    public static (BadgeTone Tone, SymbolRegular Symbol, string Key) Risk(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => (BadgeTone.Success, SymbolRegular.CheckmarkCircle16, "risk.safe"),
        RiskLevel.Caution => (BadgeTone.Warning, SymbolRegular.Warning16, "risk.caution"),
        _ => (BadgeTone.Danger, SymbolRegular.ErrorCircle16, "risk.expert"),
    };

    public static (BadgeTone Tone, SymbolRegular Symbol, string Key) Evidence(EvidenceLevel evidence) => evidence switch
    {
        EvidenceLevel.Proven => (BadgeTone.Accent, SymbolRegular.Checkmark16, "evidence.proven"),
        EvidenceLevel.Minor => (BadgeTone.Neutral, SymbolRegular.Subtract16, "evidence.minor"),
        _ => (BadgeTone.Warning, SymbolRegular.QuestionCircle16, "evidence.disputed"),
    };
}

/// <summary>
/// Gemeinsamer Zustand aller Tweak-Seiten: Karten, ausstehende Aenderungen (Aenderungsleiste),
/// Detailpanel und der Ablauf Vorschau, Expertenwarnung, Fortschritt, Ergebnis.
/// </summary>
public sealed partial class TweakWorkspace : ObservableObject
{
    private readonly TweakCatalog _catalog;
    private readonly TweakService _service;
    private readonly Localizer _localizer;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly IUiInvoker _ui;
    private readonly IExplorerRestarter _explorer;
    private readonly Dictionary<string, TweakCardViewModel> _cards = new(StringComparer.Ordinal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingText), nameof(HasPending))]
    private int _pendingCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private TweakDetailsViewModel? _details;

    public TweakWorkspace(
        TweakCatalog catalog,
        TweakService service,
        Localizer localizer,
        AppSettings settings,
        IDialogService dialogs,
        IUiInvoker ui,
        IExplorerRestarter explorer,
        bool readOnly)
    {
        _catalog = catalog;
        _service = service;
        _localizer = localizer;
        _settings = settings;
        _dialogs = dialogs;
        _ui = ui;
        _explorer = explorer;
        IsReadOnly = readOnly;
        foreach (var tweak in catalog.Tweaks)
        {
            _cards[tweak.Id] = new TweakCardViewModel(this, tweak);
        }

        _localizer.PropertyChanged += OnLanguageChanged;
    }

    public bool IsReadOnly { get; }

    public bool ExpertMode => _settings.ExpertMode;

    public bool HasPending => PendingCount > 0;

    /// <summary>Klartext der Aenderungsleiste (V2): "3 Dinge werden geaendert. Neustart noetig: nein."</summary>
    public string PendingText => _localizer.Format(
        "changebar.plain",
        ("n", PendingCount),
        ("reboot", _localizer[_cards.Values.Any(c => c.IsPending && c.Tweak.RequiresReboot) ? "dashboard.yes" : "dashboard.no"].ToLower(System.Globalization.CultureInfo.GetCultureInfo(_localizer.Culture))));

    public Localizer Localizer => _localizer;

    public TweakService Service => _service;

    /// <summary>Katalog mit erzeugten Tweaks (Autostart, Spiele, DNS, Updates).</summary>
    public ITweakCatalog Catalog => new CompositeCatalog(_catalog);

    public IEnumerable<TweakCardViewModel> Cards => _cards.Values;

    public IReadOnlyList<TweakCardViewModel> CardsFor(params string[] modules)
    {
        return _cards.Values.Where(c => modules.Contains(c.Tweak.Module)).OrderBy(c => c.Tweak.Id, StringComparer.Ordinal).ToList();
    }

    public TweakCardViewModel Card(string id) => _cards[id];

    public string Text(string key) => _catalog.Text(_localizer.Culture, key);

    /// <summary>Text in einer bestimmten Sprache (Suche ueber Deutsch und Englisch).</summary>
    public string TextIn(string culture, string key) => _catalog.Text(culture, key);

    public string NameOf(string tweakId)
    {
        if (_cards.TryGetValue(tweakId, out var card))
        {
            return card.Name;
        }

        if (AutostartTweaks.TryParse(tweakId, out _, out var name))
        {
            return _localizer.Format("autostart.tweakname", ("name", name));
        }

        if (GameTweaks.TargetOf(tweakId) is { } game)
        {
            return $"{Text($"tweak.{tweakId[..8]}.name")}: {game}";
        }

        var parts = tweakId.Split('/');
        if (CustomDnsTweaks.TryCreate(tweakId, out _))
        {
            return $"{Text("tweak.NET-004.name")}: {parts[1]}";
        }

        if (UpdateTweaks.TryCreate(tweakId, out _))
        {
            return parts[0] == UpdateTweaks.PauseId
                ? $"{Text("tweak.UPD-001.name")}: {_localizer.Format("updates.pause.until", ("date", DateOnly.ParseExact(parts[1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)).ToString("d", System.Globalization.CultureInfo.GetCultureInfo(_localizer.Culture))))}"
                : $"{Text("tweak.UPD-007.name")}: Windows {parts[1]} {parts[2]}";
        }

        return tweakId;
    }

    /// <summary>Bereich eines Katalog-Tweaks oder eines erzeugten Autostart-Tweaks; unbekannte IDs gelten als Benutzer-Tweak.</summary>
    public TweakScope ScopeOf(string tweakId)
    {
        if (_cards.TryGetValue(tweakId, out var card))
        {
            return card.Tweak.Scope;
        }

        return AutostartTweaks.TryCreate(tweakId, out var tweak) || GameTweaks.TryCreate(tweakId, out tweak) || CustomDnsTweaks.TryCreate(tweakId, out tweak) || UpdateTweaks.TryCreate(tweakId, out tweak)
            ? tweak.Scope
            : TweakScope.User;
    }

    public void SetExpertMode(bool enabled)
    {
        _settings.ExpertMode = enabled;
        foreach (var card in _cards.Values)
        {
            card.NotifyVisibility();
        }

        OnPropertyChanged(nameof(ExpertMode));
    }

    /// <summary>Liest den Ist-Zustand aller Karten (nur lesend, im Hintergrund).</summary>
    public async Task RefreshAsync()
    {
        var states = await Task.Run(() => _cards.Keys.ToDictionary(id => id, _service.GetStatus)).ConfigureAwait(true);
        foreach (var (id, status) in states)
        {
            _cards[id].Update(status);
        }
    }

    internal void PendingChanged() => PendingCount = _cards.Values.Count(c => c.IsPending);

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var card in _cards.Values)
        {
            card.NotifyEnabled();
        }
    }

    public void OpenDetails(TweakCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var items = _service.DescribeTweak(card.Tweak.Id);
        Details = new TweakDetailsViewModel(this, card, items);
    }

    [RelayCommand]
    private void CloseDetails() => Details = null;

    [RelayCommand]
    private void Discard()
    {
        foreach (var card in _cards.Values)
        {
            card.ClearPending();
        }

        PendingChanged();
    }

    [RelayCommand]
    private Task Preview() => RunAsync(previewOnly: true);

    [RelayCommand]
    private Task Apply() => RunAsync(previewOnly: false);

    /// <summary>
    /// Wendet Tweaks ausserhalb der Karten an (zum Beispiel Autostart-Eintraege), mit demselben Ablauf:
    /// Vorschau, Expertenwarnung, Fortschritt, Ergebnis.
    /// </summary>
    public async Task<BatchSummary> ApplyIdsAsync(IReadOnlyList<string> tweakIds, IReadOnlyList<string>? revertIds = null, BatchOptions? options = null)
    {
        revertIds ??= [];
        if (IsBusy || (tweakIds.Count == 0 && revertIds.Count == 0))
        {
            return BatchSummary.Empty;
        }

        IsBusy = true;
        try
        {
            _batchOptions = options;
            _lastSummary = BatchSummary.Empty;
            await RunBatchAsync(tweakIds, revertIds, previewOnly: false).ConfigureAwait(true);
            return _lastSummary;
        }
        finally
        {
            _batchOptions = null;
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private BatchOptions? _batchOptions;
    private BatchSummary _lastSummary = BatchSummary.Empty;

    /// <summary>Setzt mehrere Tweaks zurueck ("Preset rueckgaengig", "Alles wieder rueckgaengig"), mit einem Ergebnisfenster.</summary>
    public async Task RevertManyAsync(IReadOnlyList<string> tweakIds)
    {
        if (IsBusy || tweakIds.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var results = new List<RevertResult>();
            foreach (var id in tweakIds.Reverse())
            {
                results.Add(await Task.Run(() => _service.Revert(id, Resolver)).ConfigureAwait(true));
            }

            await ShowRevertResults(results).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Setzt einen einzelnen Tweak zurueck (Detailpanel, Wiederherstellungsseite).</summary>
    public async Task RevertAsync(string tweakId)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _service.Revert(tweakId, Resolver)).ConfigureAwait(true);
            await ShowRevertResults([result]).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    internal ConflictDecision Resolver(RevertConflict conflict)
    {
        return _ui.Invoke(() => _dialogs.ShowAsync(new ConflictDialogViewModel(NameOf(conflict.TweakId), conflict)));
    }

    internal async Task ShowRevertResults(IReadOnlyList<RevertResult> results)
    {
        var rows = results.Select(r => new ResultRow(
            NameOf(r.TweakId),
            r.Outcome is RevertOutcome.Reverted or RevertOutcome.NotApplied,
            r.Outcome switch
            {
                RevertOutcome.Reverted => _localizer["result.reverted"],
                RevertOutcome.NotApplied => _localizer["result.notapplied"],
                RevertOutcome.Cancelled => _localizer["result.cancelled"],
                _ => r.Error?.Message ?? _localizer["result.failed"],
            })).ToList();
        await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["result.title.revert"], Summary(rows), rows, false)).ConfigureAwait(true);
    }

    private async Task RunAsync(bool previewOnly)
    {
        if (IsBusy || PendingCount == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var applies = _cards.Values.Where(c => c.IsPending && c.Desired == true).Select(c => c.Tweak.Id).ToList();
            var reverts = _cards.Values.Where(c => c.IsPending && c.Desired == false).Select(c => c.Tweak.Id).ToList();
            if (await RunBatchAsync(applies, reverts, previewOnly).ConfigureAwait(true))
            {
                foreach (var card in _cards.Values)
                {
                    card.ClearPending();
                }

                PendingChanged();
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Gemeinsamer Ablauf; liefert true, wenn angewendet wurde (nicht bei Abbruch oder reiner Vorschau).</summary>
    private async Task<bool> RunBatchAsync(IReadOnlyList<string> applies, IReadOnlyList<string> reverts, bool previewOnly)
    {
        // Vorschau ist immer der erste Schritt; ohne Bestaetigung wird nichts angewendet.
        BatchPlan plan;
        try
        {
            plan = await Task.Run(() => _service.Preview(applies)).ConfigureAwait(true);
        }
        catch (TweakPilotException ex)
        {
            // Ist-Wert nicht lesbar (zum Beispiel Netzwerkadapter): im Ergebnis melden statt die App zu beenden.
            var errorRows = applies.Select(id => new ResultRow(NameOf(id), false, ex.Message)).ToList();
            await _dialogs.ShowAsync(new ResultDialogViewModel(_localizer["result.title"], Summary(errorRows), errorRows, false)).ConfigureAwait(true);
            return false;
        }

        var preview = new PreviewDialogViewModel(
            plan.Items.Select(i => new PreviewRow(NameOf(i.TweakId), i.ValueName is null ? i.Target : $"{i.Target} : {i.ValueName}", i.Current, i.New)).ToList(),
            plan.Excluded.Select(e => new NoteRow(NameOf(e.TweakId), e.Reason)).ToList(),
            reverts.Select(NameOf).ToList(),
            plan.RequiresReboot,
            plan.Ready.Any(t => t.RequiresAdmin),
            previewOnly,
            userOnly: plan.Ready.Count > 0 && plan.Ready.All(t => t.Scope == TweakScope.User))
        {
            Groups = _batchOptions?.Groups?.Invoke(plan.Ready.Select(t => t.Id).Concat(reverts).ToList()) ?? [],
        };
        if (!await _dialogs.ShowAsync(preview).ConfigureAwait(true) || previewOnly)
        {
            return false;
        }

        var expert = plan.Ready.Where(t => t.Risk == RiskLevel.Expert).Select(t => NameOf(t.Id)).ToList();
        if (expert.Count > 0 && !await _dialogs.ShowAsync(new ExpertWarningViewModel(_localizer, expert)).ConfigureAwait(true))
        {
            return false;
        }

        var rows = new List<ResultRow>();
        var reboot = false;
        var restartExplorer = false;
        if (plan.Ready.Count > 0)
        {
            var progress = new ProgressDialogViewModel(NameOf);
            _dialogs.Show(progress);
            BatchResult result;
            try
            {
                var reporter = new Progress<BatchProgress>(progress.Report);
                result = await Task.Run(() => _service.Apply(plan.Ready.Select(t => t.Id), new ApplyOptions
                {
                    // Reine Benutzer-Tweaks: Der Hinweis stand in der Vorschau, deren Bestaetigung ist die Zustimmung.
                    // Ist ein Wiederherstellungspunkt dagegen fehlgeschlagen, wird ausdruecklich nachgefragt (Regel S1).
                    ConfirmWithoutRestorePoint = rp => (rp.Status == RestorePointStatus.NotAttempted && preview.UserOnly) || ConfirmWithoutRestorePoint(rp),
                    Progress = reporter,
                    Cancellation = progress.Token,
                })).ConfigureAwait(true);
            }
            catch (TweakPilotException ex)
            {
                // Helfer nicht gestartet, UAC abgelehnt oder Fehler im Helfer: im Ergebnis melden statt die App zu beenden.
                result = new BatchResult { Outcome = BatchOutcome.Aborted, Plan = plan, Error = ex };
            }
            finally
            {
                _dialogs.Dismiss(progress);
            }

            rows.AddRange(ApplyRows(result));
            reboot = result.RebootRequired;
            _lastSummary = _lastSummary with { Applied = result.AppliedTweaks.ToList() };
            restartExplorer = result.AppliedTweaks.Any(id => _cards.TryGetValue(id, out var card) && card.Tweak.RestartExplorer);
        }

        var reverted = new List<string>();
        foreach (var id in reverts)
        {
            var revert = await Task.Run(() => _service.Revert(id, Resolver)).ConfigureAwait(true);
            if (revert.Outcome == RevertOutcome.Reverted)
            {
                reverted.Add(id);
            }

            rows.Add(new ResultRow(NameOf(id), revert.Outcome == RevertOutcome.Reverted, revert.Outcome == RevertOutcome.Reverted
                ? _localizer["result.reverted"]
                : revert.Error?.Message ?? _localizer["result.cancelled"]));
        }

        _lastSummary = _lastSummary with { Reverted = reverted };
        var resultDialog = new ResultDialogViewModel(_localizer["result.title"], Summary(rows), rows, reboot)
        {
            UndoText = _batchOptions?.Undo is not null && (_lastSummary.Applied.Count > 0 || reverted.Count > 0) ? _localizer["result.undo"] : null,
        };
        await _dialogs.ShowAsync(resultDialog).ConfigureAwait(true);
        if (resultDialog.UndoRequested && _batchOptions?.Undo is { } undo)
        {
            _lastSummary = _lastSummary with { UndoRequested = true };
            IsBusy = false;
            await undo(_lastSummary).ConfigureAwait(true);
            return true;
        }

        // Explorer-Neustart nur nach Rueckfrage (Beispiel UI-001).
        if (restartExplorer && await _dialogs.ShowAsync(new ConfirmDialogViewModel(
                _localizer["explorer.title"], _localizer["explorer.body"], _localizer["explorer.confirm"], _localizer["dialog.later"])).ConfigureAwait(true))
        {
            await Task.Run(_explorer.Restart).ConfigureAwait(true);
        }

        return true;
    }

    private bool ConfirmWithoutRestorePoint(RestorePointResult restorePoint)
    {
        return _ui.Invoke(() => _dialogs.ShowAsync(new ConfirmDialogViewModel(
            _localizer["restorepoint.title"],
            restorePoint.Message + " " + _localizer["restorepoint.body"],
            _localizer["restorepoint.continue"],
            _localizer["dialog.cancel"])));
    }

    private IEnumerable<ResultRow> ApplyRows(BatchResult result)
    {
        foreach (var tweak in result.Plan.Ready)
        {
            if (result.AppliedTweaks.Contains(tweak.Id))
            {
                yield return new ResultRow(NameOf(tweak.Id), true, _localizer["result.applied"]);
            }
            else if (tweak.Id == result.FailedTweakId)
            {
                yield return new ResultRow(NameOf(tweak.Id), false, result.Error?.Message ?? _localizer["result.failed"]);
            }
            else
            {
                yield return new ResultRow(NameOf(tweak.Id), false, result.Outcome switch
                {
                    BatchOutcome.RolledBack => _localizer["result.rolledback"],
                    BatchOutcome.RollbackFailed => _localizer["result.rollbackfailed"] + " " + result.BackupSetPath,
                    BatchOutcome.Aborted => result.Error?.Message ?? _localizer["result.cancelled"],
                    _ => result.Error?.Message ?? _localizer["result.failed"],
                });
            }
        }

        foreach (var excluded in result.Plan.Excluded)
        {
            yield return new ResultRow(NameOf(excluded.TweakId), false, excluded.Reason);
        }
    }

    private string Summary(IReadOnlyList<ResultRow> rows)
    {
        return _localizer.Format("result.summary", ("ok", rows.Count(r => r.Ok)), ("failed", rows.Count(r => !r.Ok)));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var card in _cards.Values)
        {
            card.NotifyTexts();
        }

        OnPropertyChanged(nameof(PendingText));
    }
}

/// <summary>Was ein Batch wirklich geaendert hat (fuer Presets und "Preset rueckgaengig").</summary>
public sealed record BatchSummary(IReadOnlyList<string> Applied, IReadOnlyList<string> Reverted, bool UndoRequested = false)
{
    public static readonly BatchSummary Empty = new([], []);
}

/// <summary>V2: Klartext-Gruppen fuer die Vorschau und "Alles wieder rueckgaengig" im Ergebnis.</summary>
public sealed record BatchOptions(Func<IReadOnlyList<string>, IReadOnlyList<PreviewGroup>>? Groups, Func<BatchSummary, Task>? Undo);

/// <summary>Tweak-Karte (docs/DESIGN.md, Abschnitt 4).</summary>
public sealed partial class TweakCardViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;
    private TweakStatus? _status;

    public TweakCardViewModel(TweakWorkspace workspace, TweakDefinition tweak)
    {
        _workspace = workspace;
        Tweak = tweak;
    }

    public TweakDefinition Tweak { get; }

    public string Name => _workspace.Text(Tweak.NameKey);

    public string Description => _workspace.Text(Tweak.DescriptionKey);

    public string SideEffects => _workspace.Text(Tweak.SideEffectsKey);

    public BadgeTone RiskTone => BadgeStyle.Risk(Tweak.Risk).Tone;

    public SymbolRegular RiskSymbol => BadgeStyle.Risk(Tweak.Risk).Symbol;

    public string RiskText => _workspace.Localizer[BadgeStyle.Risk(Tweak.Risk).Key];

    public BadgeTone EvidenceTone => BadgeStyle.Evidence(Tweak.Evidence).Tone;

    public SymbolRegular EvidenceSymbol => BadgeStyle.Evidence(Tweak.Evidence).Symbol;

    public string EvidenceText => _workspace.Localizer[BadgeStyle.Evidence(Tweak.Evidence).Key];

    public string RebootText => _workspace.Localizer["badge.reboot"];

    public string AdminText => _workspace.Localizer["badge.admin"];

    public bool IsVisible => !Tweak.HiddenByDefault || _workspace.ExpertMode;

    /// <summary>Nutzen-Titel fuer den Einfach-Modus (hoechstens 5 Woerter).</summary>
    public string PlainTitle => _workspace.Text(Tweak.PlainTitleKey ?? Tweak.NameKey);

    /// <summary>Nutzen-Satz fuer den Einfach-Modus (hoechstens 90 Zeichen).</summary>
    public string PlainDescription => _workspace.Text(Tweak.PlainDescKey ?? Tweak.DescriptionKey);

    /// <summary>Wirkungswort statt Badges: Spuerbar, Klein, Unklar.</summary>
    public string ImpactText => _workspace.Localizer["impact." + (Tweak.Impact ?? ImpactLevel.Unclear).ToString().ToLowerInvariant()];

    public string ImpactNote => Tweak.ImpactNoteKey is { } key ? _workspace.Text(key) : string.Empty;

    /// <summary>Ein Risiko-Symbol mit Text: Haken "Sicher", Ausrufezeichen "Mit Vorsicht", Schild "Nur fuer Experten".</summary>
    public SymbolRegular RiskPlainSymbol => Tweak.Risk switch
    {
        RiskLevel.Safe => SymbolRegular.CheckmarkCircle16,
        RiskLevel.Caution => SymbolRegular.Warning16,
        _ => SymbolRegular.Shield16,
    };

    public string RiskPlainText => _workspace.Localizer["risk.plain." + Tweak.Risk.ToString().ToLowerInvariant()];

    /// <summary>Suche ueber Titel und Beschreibung in Deutsch und Englisch.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var keys = new[] { Tweak.PlainTitleKey, Tweak.PlainDescKey, Tweak.NameKey, Tweak.DescriptionKey };
        return new[] { Localizer.German, Localizer.English }
            .SelectMany(culture => keys.Where(k => k is not null).Select(k => _workspace.TextIn(culture, k!)))
            .Any(text => text.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public TweakState State => _status?.State ?? TweakState.Unavailable;

    public bool Tracked => _status?.TrackedInLedger ?? false;

    /// <summary>Gewuenschter Zustand, solange eine Aenderung aussteht.</summary>
    public bool? Desired { get; private set; }

    public bool IsPending => Desired is not null;

    /// <summary>Angewendet, aber nicht durch die App: ohne eigene Sicherung kein Zuruecksetzen moeglich.</summary>
    public bool AppliedByOthers => State == TweakState.Applied && !Tracked;

    public bool IsToggleEnabled => _status is not null
        && State != TweakState.Unavailable
        && !AppliedByOthers
        && !_workspace.IsReadOnly
        && !_workspace.IsBusy;

    public string? ToggleToolTip => State switch
    {
        TweakState.Unavailable when _status is not null => _workspace.Localizer.Format("unavailable.tooltip", ("reason", _status.Reason ?? string.Empty)),
        TweakState.Applied when !Tracked => _workspace.Localizer["card.appliedbyothers"],
        _ => null,
    };

    /// <summary>Schalterstellung: ausstehender Wunsch oder Ist-Zustand (Teilweise = null).</summary>
    public bool? IsOn
    {
        get => Desired ?? State switch
        {
            TweakState.Applied => true,
            TweakState.Mixed => null,
            _ => false,
        };
        set
        {
            var target = value == true;
            var current = State == TweakState.Applied;
            Desired = target == current && State != TweakState.Mixed ? null : target;
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(IsPending));
            _workspace.PendingChanged();
        }
    }

    public void Update(TweakStatus status)
    {
        _status = status;
        OnPropertyChanged(string.Empty);
    }

    public void ClearPending()
    {
        Desired = null;
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsPending));
    }

    public void NotifyTexts() => OnPropertyChanged(string.Empty);

    public void NotifyVisibility() => OnPropertyChanged(nameof(IsVisible));

    public void NotifyEnabled() => OnPropertyChanged(nameof(IsToggleEnabled));

    [RelayCommand]
    private void ShowDetails() => _workspace.OpenDetails(this);
}

/// <summary>Seitenpanel "Mehr Details" (420 Punkte rechts).</summary>
public sealed partial class TweakDetailsViewModel : ObservableObject
{
    private readonly TweakWorkspace _workspace;

    public TweakDetailsViewModel(TweakWorkspace workspace, TweakCardViewModel card, IReadOnlyList<PlanItem> technical)
    {
        _workspace = workspace;
        Card = card;
        Technical = technical.Select(t => new PreviewRow(card.Name, t.ValueName is null ? t.Target : $"{t.Target} : {t.ValueName}", t.Current, t.New)).ToList();
    }

    public TweakCardViewModel Card { get; }

    public IReadOnlyList<PreviewRow> Technical { get; }

    public IReadOnlyList<string> Sources => Card.Tweak.Sources;

    public bool CanReset => Card.Tracked && !_workspace.IsReadOnly;

    [RelayCommand]
    private Task Reset() => _workspace.RevertAsync(Card.Tweak.Id);
}
