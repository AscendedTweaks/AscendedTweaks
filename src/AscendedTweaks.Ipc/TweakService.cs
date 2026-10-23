using System.Globalization;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Cleanup;
using AscendedTweaks.Engine.State;

namespace AscendedTweaks.Ipc;

/// <summary>
/// Einziger Einstieg fuer Oberflaeche und CLI. Benutzer-Tweaks laufen im eigenen Prozess (ohne Helfer),
/// Maschinen-Tweaks und Benutzer-Richtlinien im Helfer (Abschnitt 9.2). Lesen, Vorschau und Status brauchen nie Admin-Rechte.
/// </summary>
public sealed class TweakService : IDisposable
{
    /// <summary>Reihenfolge beim Anwenden; schlaegt ein spaeterer Teil fehl, werden die frueheren zurueckgenommen.</summary>
    private static readonly TweakScope[] Order = [TweakScope.Machine, TweakScope.UserPolicy, TweakScope.User];

    private readonly ITweakCatalog _catalog;
    private readonly TweakEngine _user;
    private readonly TweakEngine _machineReader;
    private readonly TweakEngine _policyReader;
    private readonly Func<IHelperChannel> _helperFactory;
    private readonly TimeProvider _time;
    private IHelperChannel? _helper;

    /// <param name="user">Engine fuer Benutzer-Tweaks mit dem Benutzer-Zustandsspeicher.</param>
    /// <param name="machineReader">Engine auf dem Maschinen-Zustandsspeicher, nur zum Lesen (Status, Vorschau, Ledger).</param>
    /// <param name="policyReader">Engine auf dem Speicher der Benutzer-Richtlinien dieses Benutzers, nur zum Lesen.</param>
    /// <param name="helperFactory">Erzeugt die Helfer-Verbindung erst beim ersten privilegierten Schritt.</param>
    public TweakService(ITweakCatalog catalog, TweakEngine user, TweakEngine machineReader, TweakEngine policyReader, Func<IHelperChannel> helperFactory, TimeProvider time)
    {
        _catalog = catalog;
        _user = user;
        _machineReader = machineReader;
        _policyReader = policyReader;
        _helperFactory = helperFactory;
        _time = time;
    }

    public bool HelperStarted => _helper is not null;

    private CleanupEngine? _userCleanup;
    private CleanupEngine? _machineEstimator;

    /// <summary>
    /// Bereinigung: Benutzer-Kategorien laufen im eigenen Prozess, Systemordner im Helfer. Ohne laufenden Helfer werden
    /// Systemordner nur geschaetzt (ohne Administratorrechte gelesen), damit ein Scan keinen UAC-Dialog ausloest.
    /// </summary>
    public void AttachCleanup(CleanupEngine user, CleanupEngine machineEstimator)
    {
        _userCleanup = user;
        _machineEstimator = machineEstimator;
    }

    public CleanupScan ScanCleanup(string categoryId)
    {
        var category = CleanupCategory(categoryId);
        if (category.Scope == TweakScope.User)
        {
            return RequireCleanup(_userCleanup).Scan(categoryId);
        }

        return HelperStarted
            ? Helper.ScanCleanup(categoryId)
            : RequireCleanup(_machineEstimator).Scan(categoryId) with { Estimated = true };
    }

    public CleanupResult RunCleanup(string categoryId, CleanupMode mode)
    {
        var category = CleanupCategory(categoryId);
        return category.Scope == TweakScope.User
            ? RequireCleanup(_userCleanup).Run(categoryId, mode)
            : Helper.RunCleanup(categoryId, mode);
    }

    /// <summary>Entfernt Apps fuer alle Benutzer im Helfer (UAC).</summary>
    public IReadOnlyList<AscendedTweaks.Engine.Debloat.RemovedApp> RemoveApps(IReadOnlyList<string> names, bool deprovision) => Helper.RemoveApps(names, deprovision);

    /// <summary>Wartung im Helfer (UAC): Netzwerk-Reset, ReTrim, Defragmentierung nur fuer HDD.</summary>
    public AscendedTweaks.Engine.Tools.MaintenanceResult RunMaintenance(AscendedTweaks.Engine.Tools.MaintenanceAction action, char? drive) => Helper.RunMaintenance(action, drive);

    /// <summary>SMART-Werte, nur Anzeige, im Helfer gelesen (UAC).</summary>
    public IReadOnlyList<AscendedTweaks.Engine.Tools.DiskHealth> ReadDiskHealth() => Helper.ReadDiskHealth();

    private static CleanupCategory CleanupCategory(string categoryId) =>
        CleanupCategories.TryGet(categoryId, out var category)
            ? category
            : throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Unknown cleanup category {categoryId}.");

    private static CleanupEngine RequireCleanup(CleanupEngine? engine) =>
        engine ?? throw new AscendedTweaksException(ErrorKind.General, "Cleanup is not set up.");

    private IHelperChannel Helper => _helper ??= _helperFactory();

    /// <summary>Export als Skript; liest nur (alle Werte sind ohne Administratorrechte lesbar oder werden als unbekannt markiert).</summary>
    public AscendedTweaks.Engine.Export.ScriptExport ExportScripts(IReadOnlyList<string> tweakIds, Func<string, string> name, string title) => _user.ExportScripts(tweakIds, name, title);

    public TweakStatus GetStatus(string tweakId) => Reader(ScopeOf(tweakId)).GetStatus(tweakId);

    public IReadOnlyList<PlanItem> DescribeTweak(string tweakId) => Reader(ScopeOf(tweakId)).DescribeTweak(tweakId);

    /// <summary>Wiederherstellungspunkt auf Wunsch (First steps). Startet den Helfer (UAC).</summary>
    public RestorePointResult CreateRestorePoint(string name) => Helper.CreateRestorePoint(name);

    public BatchPlan Preview(IEnumerable<string> tweakIds)
    {
        var groups = Split(tweakIds);
        return MergePlans(Order.Select(scope => PlanFor(scope, groups[scope])));
    }

    public IReadOnlyList<LedgerEntry> GetAppliedTweaks()
    {
        return Order.SelectMany(scope => Reader(scope).GetAppliedTweaks()).OrderBy(e => e.AppliedUtc).ToList();
    }

    public BatchResult Apply(IEnumerable<string> tweakIds, ApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var groups = Split(tweakIds);
        var plans = Order.ToDictionary(scope => scope, scope => PlanFor(scope, groups[scope]));
        var preview = MergePlans(plans.Values);
        if (options.DryRun)
        {
            return new BatchResult { Outcome = BatchOutcome.DryRun, Plan = preview };
        }

        if (preview.Ready.Count == 0)
        {
            return new BatchResult { Outcome = BatchOutcome.NothingToDo, Plan = preview };
        }

        // Ein Wiederherstellungspunkt fuer den Gesamt-Batch. Nur Helfer-Teile brauchen Admin (UAC).
        var needsHelper = plans[TweakScope.Machine].Ready.Count > 0 || plans[TweakScope.UserPolicy].Ready.Count > 0;
        var name = string.Create(CultureInfo.InvariantCulture, $"{ProductInfo.Name} {_time.GetLocalNow():yyyy-MM-dd HH:mm} {preview.Items.Count} changes");
        var restorePoint = needsHelper ? Helper.CreateRestorePoint(name) : new NoRestorePointService().TryCreate(name);
        if (!restorePoint.IsAcceptable && options.ConfirmWithoutRestorePoint?.Invoke(restorePoint) != true)
        {
            return new BatchResult { Outcome = BatchOutcome.Aborted, Plan = preview, RestorePoint = restorePoint };
        }

        var committed = new List<BatchResult>();
        var skipped = new List<PreflightEntry>();
        foreach (var scope in Order)
        {
            var plan = plans[scope];
            if (plan.Ready.Count == 0)
            {
                continue;
            }

            var result = scope == TweakScope.User
                ? _user.ApplyBatch(plan.Ready.Select(t => t.Id), new ApplyOptions
                {
                    ConfirmWithoutRestorePoint = _ => true,
                    Progress = options.Progress,
                    Cancellation = options.Cancellation,
                })
                : Helper.ApplyTweaks(new ApplyTweaksRequest
                {
                    Ids = plan.Ready.Select(t => t.Id).ToList(),
                    RestorePointHandled = true,
                    ProceedWithoutRestorePoint = true,
                }).ToResult(_catalog);

            // The helper checks again with administrator rights and can find that nothing is left to change (for example
            // a feature that only it can read is already off). That is not a failure: keep the other parts, report the reason.
            skipped.AddRange(result.Plan.Excluded);
            if (result.Outcome == BatchOutcome.NothingToDo)
            {
                continue;
            }

            if (result.Outcome != BatchOutcome.Committed)
            {
                // Dieser Teil hat sich selbst zurueckgenommen; die bereits abgeschlossenen Teile ebenfalls zuruecknehmen.
                var compensation = Compensate(committed);
                return result with
                {
                    Outcome = compensation.Count == 0 ? result.Outcome : BatchOutcome.RollbackFailed,
                    Plan = preview,
                    RestorePoint = restorePoint,
                    RollbackErrors = [.. result.RollbackErrors, .. compensation],
                    BackupSetPath = JoinPaths(committed.Select(c => c.BackupSetPath).Append(result.BackupSetPath)),
                };
            }

            committed.Add(result);
        }

        var final = WithSkipped(preview, skipped);
        if (committed.Count == 0)
        {
            return new BatchResult { Outcome = BatchOutcome.NothingToDo, Plan = final, RestorePoint = restorePoint };
        }

        return new BatchResult
        {
            Outcome = BatchOutcome.Committed,
            Plan = final,
            RestorePoint = restorePoint,
            BatchId = committed[0].BatchId,
            AppliedTweaks = committed.SelectMany(c => c.AppliedTweaks).ToList(),
            BackupSetPath = JoinPaths(committed.Select(c => c.BackupSetPath)),
            RebootRequired = committed.Any(c => c.RebootRequired),
        };
    }

    /// <summary>The preview with the tweaks the engines skipped themselves moved from "ready" to "excluded", each once.</summary>
    private static BatchPlan WithSkipped(BatchPlan preview, IEnumerable<PreflightEntry> skipped)
    {
        var known = preview.Excluded.Select(e => e.TweakId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = skipped.Where(e => known.Add(e.TweakId)).ToList();
        if (added.Count == 0)
        {
            return preview;
        }

        var ids = added.Select(e => e.TweakId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new BatchPlan
        {
            Ready = preview.Ready.Where(t => !ids.Contains(t.Id)).ToList(),
            Excluded = [.. preview.Excluded, .. added],
            Items = preview.Items.Where(i => !ids.Contains(i.TweakId)).ToList(),
        };
    }

    public RevertResult Revert(string tweakId, ConflictResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        var scope = ScopeOf(tweakId);
        if (scope == TweakScope.User)
        {
            return _user.RevertTweak(tweakId, resolver);
        }

        // Ohne Ledger-Eintrag gibt es nichts zurueckzusetzen; dafuer ist kein UAC-Dialog noetig.
        var id = _catalog.TryGet(tweakId, out var tweak) ? tweak.Id : tweakId;
        if (!Reader(scope).GetAppliedTweaks().Any(e => e.TweakId == id))
        {
            return new RevertResult(id, RevertOutcome.NotApplied);
        }

        try
        {
            var response = Helper.RevertTweak(new RevertTweakRequest { Id = id });
            if (response.PendingConflicts.Count > 0)
            {
                var decisions = new Dictionary<int, ConflictDecision>();
                foreach (var conflict in response.PendingConflicts)
                {
                    var decision = resolver(conflict);
                    if (decision == ConflictDecision.Cancel)
                    {
                        return new RevertResult(id, RevertOutcome.Cancelled);
                    }

                    decisions[conflict.CaptureIndex] = decision;
                }

                response = Helper.RevertTweak(new RevertTweakRequest { Id = id, Decisions = decisions });
            }

            return new RevertResult(
                id,
                response.Outcome,
                response.ErrorKind is { } kind ? new AscendedTweaksException(kind, response.Error ?? string.Empty, id) : null);
        }
        catch (AscendedTweaksException ex)
        {
            return new RevertResult(id, RevertOutcome.Failed, ex);
        }
    }

    /// <summary>Setzt alle gemerkten Tweaks aller Bereiche in umgekehrter Reihenfolge zurueck.</summary>
    public IReadOnlyList<RevertResult> RevertAll(ConflictResolver resolver)
    {
        return GetAppliedTweaks()
            .OrderByDescending(e => e.AppliedUtc)
            .Select(e => Revert(e.TweakId, resolver))
            .ToList();
    }

    /// <summary>Ausgangszustand aller Katalog-Stellen; nur lesend, deshalb ohne Helfer im Benutzer-Speicher abgelegt.</summary>
    public BaselineResult CaptureBaseline() => _user.CaptureBaseline();

    public IReadOnlyList<BaselineDeviation> CompareWithBaseline() => _user.CompareWithBaseline();

    public IReadOnlyList<(TweakScope Scope, BackupSetInfo Set)> ListBackupSets()
    {
        return Order.SelectMany(scope => Reader(scope).ListBackupSets().Select(b => (scope, b)))
            .OrderByDescending(x => x.b.CreatedUtc)
            .ToList();
    }

    /// <summary>Nur Benutzer-Sets; die anderen liegen in %ProgramData% und sind fuer Benutzer schreibgeschuetzt.</summary>
    public void DeleteUserBackupSet(string backupSetId) => _user.Store.DeleteBackupSet(backupSetId);

    public IReadOnlyList<(TweakScope Scope, JournalBatch Batch)> GetInterruptedBatches()
    {
        return Order.SelectMany(scope => Reader(scope).GetInterruptedBatches().Select(b => (scope, b))).ToList();
    }

    public IReadOnlyList<string> RecoverInterrupted(TweakScope scope, string batchId)
    {
        return scope == TweakScope.User ? _user.RecoverInterrupted(batchId) : Helper.RecoverInterrupted(batchId, scope);
    }

    public void Dispose()
    {
        _helper?.Dispose();
        _helper = null;
    }

    private List<string> Compensate(IEnumerable<BatchResult> committed)
    {
        var errors = new List<string>();
        foreach (var id in committed.SelectMany(c => c.AppliedTweaks).Reverse())
        {
            var result = Revert(id, _ => ConflictDecision.RestoreOriginal);
            if (result.Outcome != RevertOutcome.Reverted)
            {
                errors.Add($"{id}: {result.Error?.Message ?? result.Outcome.ToString()}");
            }
        }

        return errors;
    }

    private BatchPlan PlanFor(TweakScope scope, IReadOnlyList<string> ids)
    {
        return scope == TweakScope.User ? _user.Plan(ids) : Reader(scope).PlanPreview(ids);
    }

    private TweakEngine Reader(TweakScope scope) => scope switch
    {
        TweakScope.Machine => _machineReader,
        TweakScope.UserPolicy => _policyReader,
        _ => _user,
    };

    private TweakScope ScopeOf(string tweakId) => _catalog.TryGet(tweakId, out var tweak) ? tweak.Scope : TweakScope.User;

    private Dictionary<TweakScope, IReadOnlyList<string>> Split(IEnumerable<string> tweakIds)
    {
        ArgumentNullException.ThrowIfNull(tweakIds);
        var ids = tweakIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return Order.ToDictionary(scope => scope, scope => (IReadOnlyList<string>)ids.Where(id => ScopeOf(id) == scope).ToList());
    }

    private static BatchPlan MergePlans(IEnumerable<BatchPlan> plans)
    {
        var list = plans.ToList();
        return new BatchPlan
        {
            Ready = list.SelectMany(p => p.Ready).ToList(),
            Excluded = list.SelectMany(p => p.Excluded).ToList(),
            Items = list.SelectMany(p => p.Items).ToList(),
        };
    }

    private static string? JoinPaths(IEnumerable<string?> paths)
    {
        var present = paths.Where(p => p is not null).ToList();
        return present.Count == 0 ? null : string.Join("; ", present);
    }
}
