using System.Globalization;
using System.Reflection;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Executors;
using AscendedTweaks.Engine.State;

namespace AscendedTweaks.Engine;

/// <summary>
/// Tweak-Engine nach dem Transaktionsprinzip (Abschnitt 5.4):
/// Vorpruefung, Vorschau, Wiederherstellungspunkt, eigene Sicherung, Capture/Apply/Verify, Rollback bei Fehler.
/// Eine Instanz arbeitet mit genau einem Zustandsspeicher (Benutzer oder Maschine).
/// </summary>
public sealed class TweakEngine
{
    private readonly ITweakCatalog _catalog;
    private readonly ExecutorSet _executors;
    private readonly DetectEvaluator _detect;
    private readonly StateStore _store;
    private readonly ISystemEnvironment _environment;
    private readonly IRestorePointService _restorePoints;
    private readonly TimeProvider _time;
    private readonly ILogSink _log;

    public TweakEngine(
        ITweakCatalog catalog,
        ExecutorSet executors,
        StateStore store,
        ISystemEnvironment environment,
        IRestorePointService restorePoints,
        TimeProvider time,
        ILogSink? log = null)
    {
        _catalog = catalog;
        _executors = executors;
        _detect = new DetectEvaluator(executors, environment.IsElevated);
        _store = store;
        _environment = environment;
        _restorePoints = restorePoints;
        _time = time;
        _log = log ?? NullLogSink.Instance;
    }

    public StateStore Store => _store;

    /// <summary>Export als PowerShell-Skript und .reg-Datei mit den aktuellen Werten als Originalstand (Abschnitt 10.4).</summary>
    public Export.ScriptExport ExportScripts(IReadOnlyList<string> tweakIds, Func<string, string> name, string title) =>
        new Export.ScriptExporter(_executors, _catalog).Export(tweakIds, name, title, _time.GetUtcNow());

    public TweakStatus GetStatus(string tweakId)
    {
        if (!_catalog.TryGet(tweakId, out var tweak))
        {
            return new TweakStatus(tweakId, TweakState.Unavailable, "Unknown tweak ID.", false);
        }

        var tracked = _store.LoadLedger().Entries.Any(e => e.TweakId == tweak.Id);
        var gate = CheckGates(tweak, ignoreAdmin: true);
        if (gate is not null)
        {
            return new TweakStatus(tweak.Id, TweakState.Unavailable, gate.Reason, tracked);
        }

        var result = _detect.Evaluate(tweak.Detect);
        return new TweakStatus(tweak.Id, result.State, result.Reason, tracked);
    }

    public IReadOnlyList<LedgerEntry> GetAppliedTweaks() => _store.LoadLedger().Entries;

    public IReadOnlyList<BackupSetInfo> ListBackupSets() => _store.ListBackupSets();

    /// <summary>Technische Details eines Tweaks (Stelle, aktueller und neuer Wert), unabhaengig vom Zustand. Nur lesend.</summary>
    public IReadOnlyList<PlanItem> DescribeTweak(string tweakId)
    {
        if (!_catalog.TryGet(tweakId, out var tweak))
        {
            return [];
        }

        var items = new List<PlanItem>();
        foreach (var action in tweak.Apply)
        {
            try
            {
                items.Add(_executors.For(action).Describe(tweak.Id, action));
            }
            catch (Exception ex) when (ex is AscendedTweaksException or UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                items.Add(new PlanItem { TweakId = tweak.Id, ActionType = ActionTypeNames.NameOf(action), Target = ex.Message, Current = "?", New = "?" });
            }
        }

        return items;
    }

    /// <summary>
    /// Erfasst einmalig den Ausgangszustand aller Stellen, die der Katalog beruehrt (Abschnitt 7.3). Nur lesend;
    /// nicht lesbare Stellen werden uebersprungen. Ein vorhandener Ausgangszustand wird nie ueberschrieben.
    /// </summary>
    public BaselineResult CaptureBaseline()
    {
        var existing = _store.LoadBaselineId();
        if (existing is not null)
        {
            return new BaselineResult(existing, 0, [], AlreadyExisted: true);
        }

        using var lease = _store.AcquireLock();
        var entries = new List<BackupEntry>();
        var skipped = new List<string>();
        foreach (var tweak in _catalog.Tweaks)
        {
            for (var i = 0; i < tweak.Apply.Count; i++)
            {
                try
                {
                    entries.Add(new BackupEntry { TweakId = tweak.Id, ActionIndex = i, Capture = _executors.For(tweak.Apply[i]).Capture(tweak.Apply[i]) });
                }
                catch (Exception ex) when (ex is AscendedTweaksException or UnauthorizedAccessException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    skipped.Add($"{tweak.Id}: {ex.Message}");
                }
            }
        }

        var id = _store.CreateBackupSet(
            new BackupManifest
            {
                AppVersion = AppVersion,
                WindowsBuild = _environment.Build,
                WindowsUbr = _environment.Ubr,
                CreatedUtc = _time.GetUtcNow(),
                BatchId = StateStore.BaselineBatchId,
                TweakIds = entries.Select(e => e.TweakId).Distinct().ToList(),
            },
            entries);
        _store.SaveBaselineId(id);
        _log.Write(new LogRecord { Event = "BaselineCaptured", Target = id, Result = $"{entries.Count} locations, {skipped.Count} skipped" });
        return new BaselineResult(id, entries.Count, skipped, AlreadyExisted: false);
    }

    /// <summary>Stellen, die heute vom Ausgangszustand abweichen (nach "Alles rueckgaengig machen" angezeigt).</summary>
    public IReadOnlyList<BaselineDeviation> CompareWithBaseline()
    {
        var id = _store.LoadBaselineId();
        if (id is null)
        {
            return [];
        }

        var deviations = new List<BaselineDeviation>();
        foreach (var entry in _store.ReadBackupSet(id))
        {
            try
            {
                var executor = _executors.ForCapture(entry.Capture);
                if (!executor.IsAtOriginalState(entry.Capture))
                {
                    deviations.Add(new BaselineDeviation(entry.TweakId, executor.View(entry.Capture)));
                }
            }
            catch (Exception ex) when (ex is AscendedTweaksException or UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                deviations.Add(new BaselineDeviation(entry.TweakId, new CaptureView(entry.TweakId, ex.Message, "?", "?")));
            }
        }

        return deviations;
    }

    /// <summary>Vorpruefung und Vorschau. Liest nur, veraendert nichts (Regel S3).</summary>
    public BatchPlan Plan(IEnumerable<string> tweakIds) => Plan(tweakIds, ignoreAdmin: false);

    /// <summary>
    /// Vorschau ohne Admin-Pruefung: Die Oberflaeche zeigt auch Maschinen-Tweaks ohne UAC an.
    /// Angewendet werden sie spaeter nur im Helfer-Prozess.
    /// </summary>
    public BatchPlan PlanPreview(IEnumerable<string> tweakIds) => Plan(tweakIds, ignoreAdmin: true);

    private BatchPlan Plan(IEnumerable<string> tweakIds, bool ignoreAdmin)
    {
        ArgumentNullException.ThrowIfNull(tweakIds);
        var ready = new List<TweakDefinition>();
        var excluded = new List<PreflightEntry>();
        var ledger = _store.LoadLedger();
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
        var appliedKeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ledger.Entries)
        {
            if (_catalog.TryGet(entry.TweakId, out var applied))
            {
                var keys = applied.Apply.Select(a => _executors.For(a).TargetKey(a)).ToList();
                appliedKeys[applied.Id] = keys;
                foreach (var key in keys)
                {
                    claimed.TryAdd(key, applied.Id);
                }
            }
        }

        foreach (var id in tweakIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_catalog.TryGet(id, out var tweak))
            {
                excluded.Add(new PreflightEntry(id, PreflightOutcome.Unknown, "Unknown tweak ID."));
                continue;
            }

            var gate = CheckGates(tweak, ignoreAdmin);
            if (gate is not null)
            {
                excluded.Add(gate);
                continue;
            }

            var state = _detect.Evaluate(tweak.Detect);

            // Ohne Administratorrechte nicht lesbar: in der Vorschau bleibt der Tweak; der Helfer prueft echt.
            if (state.State == TweakState.Unavailable && state.NeedsElevation && ignoreAdmin)
            {
                ready.Add(tweak);
                continue;
            }

            if (state.State == TweakState.Unavailable)
            {
                excluded.Add(new PreflightEntry(tweak.Id, PreflightOutcome.Unavailable, state.Reason ?? "State cannot be detected."));
                continue;
            }

            if (state.State == TweakState.Applied)
            {
                excluded.Add(new PreflightEntry(tweak.Id, PreflightOutcome.AlreadyApplied, "Already applied."));
                continue;
            }

            var keys = tweak.Apply.Select(a => _executors.For(a).TargetKey(a)).ToList();
            var conflict = keys
                .Select(k => claimed.TryGetValue(k, out var owner) && owner != tweak.Id ? owner : null)
                .FirstOrDefault(o => o is not null);
            if (conflict is not null)
            {
                excluded.Add(new PreflightEntry(tweak.Id, PreflightOutcome.Conflict, $"Changes the same setting as {conflict}.")
                {
                    ConflictWith = conflict,
                    Replaceable = appliedKeys.TryGetValue(conflict, out var ownerKeys) && ownerKeys.All(keys.Contains),
                });
                continue;
            }

            foreach (var key in keys)
            {
                claimed[key] = tweak.Id;
            }

            ready.Add(tweak);
        }

        var items = ready
            .SelectMany(t => t.Apply.Select(a => _executors.For(a).Describe(t.Id, a)))
            .ToList();
        return new BatchPlan { Ready = ready, Excluded = excluded, Items = items };
    }

    public BatchResult ApplyBatch(IEnumerable<string> tweakIds, ApplyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plan = Plan(tweakIds);
        if (options.DryRun)
        {
            return new BatchResult { Outcome = BatchOutcome.DryRun, Plan = plan };
        }

        if (plan.Ready.Count == 0)
        {
            return new BatchResult { Outcome = BatchOutcome.NothingToDo, Plan = plan };
        }

        IDisposable lease;
        try
        {
            lease = _store.AcquireLock();
        }
        catch (Exception ex)
        {
            return new BatchResult
            {
                Outcome = BatchOutcome.BackupFailed,
                Plan = plan,
                Error = Wrap(ex, ErrorKind.BackupFailed, "State folder is not writable; nothing was changed."),
            };
        }

        using (lease)
        {
            return ApplyLocked(plan, options);
        }
    }

    private BatchResult ApplyLocked(BatchPlan plan, ApplyOptions options)
    {
        // Eine offene, unterbrochene Aenderung muss zuerst zurueckgenommen werden. Sonst wuerde ihre spaetere
        // Wiederherstellung die Werte dieses neuen Batches mit veralteten Originalwerten ueberschreiben.
        var interrupted = GetInterruptedBatches();
        if (interrupted.Count > 0)
        {
            return new BatchResult
            {
                Outcome = BatchOutcome.BlockedByInterruptedBatch,
                Plan = plan,
                BatchId = interrupted[0].BatchId,
                Error = new AscendedTweaksException(ErrorKind.StateCorrupt, "An interrupted change is still open. First run \"Undo interrupted change\"."),
            };
        }

        var restorePoint = _restorePoints.TryCreate(string.Create(
            CultureInfo.InvariantCulture,
            $"{ProductInfo.Name} {_time.GetLocalNow():yyyy-MM-dd HH:mm} {plan.Items.Count} changes"));
        if (!restorePoint.IsAcceptable && options.ConfirmWithoutRestorePoint?.Invoke(restorePoint) != true)
        {
            return new BatchResult { Outcome = BatchOutcome.Aborted, Plan = plan, RestorePoint = restorePoint };
        }

        BatchContext context;
        try
        {
            context = BeginBatch(plan);
        }
        catch (Exception ex)
        {
            return new BatchResult
            {
                Outcome = BatchOutcome.BackupFailed,
                Plan = plan,
                RestorePoint = restorePoint,
                Error = Wrap(ex, ErrorKind.BackupFailed, "Backup failed; nothing was changed."),
            };
        }

        TweakDefinition? current = null;
        try
        {
            for (var i = 0; i < plan.Ready.Count; i++)
            {
                if (options.Cancellation.IsCancellationRequested)
                {
                    throw new AscendedTweaksException(ErrorKind.Cancelled, "Vom Nutzer abgebrochen.");
                }

                current = plan.Ready[i];
                options.Progress?.Report(new BatchProgress(i + 1, plan.Ready.Count, current.Id));
                ApplyTweak(context, current);
            }
        }
        catch (Exception ex)
        {
            var error = Wrap(ex, ErrorKind.General, ex.Message, current?.Id);
            var rollbackErrors = Rollback(context, error.Message);
            return new BatchResult
            {
                Outcome = rollbackErrors.Count == 0 ? BatchOutcome.RolledBack : BatchOutcome.RollbackFailed,
                Plan = plan,
                BatchId = context.Batch.BatchId,
                BackupSetPath = _store.BackupSetPath(context.Batch.BackupSetId),
                RestorePoint = restorePoint,
                FailedTweakId = current?.Id,
                Error = error,
                RollbackErrors = rollbackErrors,
            };
        }

        Commit(context);
        return new BatchResult
        {
            Outcome = BatchOutcome.Committed,
            Plan = plan,
            BatchId = context.Batch.BatchId,
            BackupSetPath = _store.BackupSetPath(context.Batch.BackupSetId),
            RestorePoint = restorePoint,
            AppliedTweaks = plan.Ready.Select(t => t.Id).ToList(),
            RebootRequired = plan.RequiresReboot,
        };
    }

    /// <summary>Setzt einen einzelnen Tweak zurueck, mit Konfliktpruefung (Abschnitt 7.4).</summary>
    public RevertResult RevertTweak(string tweakId, ConflictResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        IDisposable lease;
        try
        {
            lease = _store.AcquireLock();
        }
        catch (Exception ex)
        {
            return new RevertResult(tweakId, RevertOutcome.Failed, Wrap(ex, ErrorKind.General, ex.Message, tweakId));
        }

        using var held = lease;
        var ledger = _store.LoadLedger();
        var entry = ledger.Entries.FirstOrDefault(e => string.Equals(e.TweakId, tweakId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new RevertResult(tweakId, RevertOutcome.NotApplied);
        }

        try
        {
            var keep = new HashSet<int>();
            for (var i = 0; i < entry.Captures.Count; i++)
            {
                var capture = entry.Captures[i];
                var executor = _executors.ForCapture(capture);
                if (executor.IsAtWrittenState(capture))
                {
                    continue;
                }

                var decision = resolver(new RevertConflict(entry.TweakId, i, executor.View(capture)));
                if (decision == ConflictDecision.Cancel)
                {
                    return new RevertResult(entry.TweakId, RevertOutcome.Cancelled);
                }

                if (decision == ConflictDecision.KeepCurrent)
                {
                    keep.Add(i);
                }
            }

            _catalog.TryGet(entry.TweakId, out var tweak);
            if (tweak is { Revert.IsRestoreFromBackup: false } && keep.Count == 0)
            {
                RunExplicitRevert(tweak);
            }
            else
            {
                for (var i = entry.Captures.Count - 1; i >= 0; i--)
                {
                    if (keep.Contains(i))
                    {
                        continue;
                    }

                    var capture = entry.Captures[i];
                    var executor = _executors.ForCapture(capture);
                    executor.Restore(capture);
                    if (!executor.IsAtOriginalState(capture))
                    {
                        throw new AscendedTweaksException(
                            ErrorKind.ValueMismatchAfterWrite,
                            $"After resetting, {executor.View(capture).Target} does not match the original.",
                            entry.TweakId);
                    }
                }
            }

            ledger.Entries.Remove(entry);
            _store.SaveLedger(ledger);
            _log.Write(new LogRecord { Event = "TweakReverted", TweakId = entry.TweakId, Result = keep.Count == 0 ? "Original" : $"{keep.Count} Werte behalten" });
            return new RevertResult(entry.TweakId, RevertOutcome.Reverted);
        }
        catch (Exception ex)
        {
            var error = Wrap(ex, ErrorKind.General, ex.Message, entry.TweakId);
            _log.Write(new LogRecord { Event = "TweakRevertFailed", Level = LogLevel.Error, TweakId = entry.TweakId, Error = $"{error.Kind}: {error.Message}" });
            return new RevertResult(entry.TweakId, RevertOutcome.Failed, error);
        }
    }

    /// <summary>Setzt alle gemerkten Tweaks in umgekehrter Reihenfolge zurueck.</summary>
    public IReadOnlyList<RevertResult> RevertAll(ConflictResolver resolver)
    {
        var ids = _store.LoadLedger().Entries
            .OrderByDescending(e => e.AppliedUtc)
            .Select(e => e.TweakId)
            .ToList();
        return ids.Select(id => RevertTweak(id, resolver)).ToList();
    }

    /// <summary>Batches, die nicht abgeschlossen wurden (Absturz, Stromausfall).</summary>
    public IReadOnlyList<JournalBatch> GetInterruptedBatches()
    {
        return _store.LoadJournal().Batches
            .Where(b => b.Status is BatchStatus.Pending or BatchStatus.Applying)
            .ToList();
    }

    /// <summary>Nimmt eine unterbrochene Aenderung anhand des Backup-Sets zurueck.</summary>
    public IReadOnlyList<string> RecoverInterrupted(string batchId)
    {
        using var lease = _store.AcquireLock();
        var journal = _store.LoadJournal();
        var batch = journal.Batches.FirstOrDefault(b => b.BatchId == batchId && b.Status is BatchStatus.Pending or BatchStatus.Applying)
            ?? throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"Kein unterbrochener Batch {batchId}.");
        var entries = _store.ReadBackupSet(batch.BackupSetId);
        var errors = new List<string>();

        var touched = batch.Tweaks
            .Where(t => t.Status is TweakJournalStatus.Applying or TweakJournalStatus.Applied)
            .Select(t => t.Id)
            .Reverse();
        foreach (var tweakId in touched)
        {
            foreach (var entry in entries.Where(e => e.TweakId == tweakId).OrderByDescending(e => e.ActionIndex))
            {
                errors.AddRange(TryRestore(entry.Capture, tweakId));
            }

            batch.Tweaks.First(t => t.Id == tweakId).Status = TweakJournalStatus.RolledBack;
        }

        var ledger = _store.LoadLedger();
        ledger.Entries.RemoveAll(e => e.BatchId == batchId);
        _store.SaveLedger(ledger);

        _log.Write(new LogRecord
        {
            Event = "InterruptedBatchRecovered",
            Level = errors.Count == 0 ? LogLevel.Information : LogLevel.Error,
            Result = errors.Count == 0 ? "RolledBack" : "RollbackFailed",
            Error = errors.Count == 0 ? null : string.Join(" | ", errors),
            Target = batchId,
        });
        batch.Status = errors.Count == 0 ? BatchStatus.RolledBack : BatchStatus.RollbackFailed;
        batch.FinishedUtc = _time.GetUtcNow();
        batch.Error = errors.Count == 0 ? "Interrupted change undone." : string.Join(" | ", errors);
        _store.SaveJournal(journal);
        return errors;
    }

    internal BatchContext BeginBatch(BatchPlan plan)
    {
        var batchId = Guid.NewGuid().ToString("N");
        var entries = new List<BackupEntry>();
        foreach (var tweak in plan.Ready)
        {
            for (var i = 0; i < tweak.Apply.Count; i++)
            {
                var capture = _executors.For(tweak.Apply[i]).Capture(tweak.Apply[i]);
                entries.Add(new BackupEntry { TweakId = tweak.Id, ActionIndex = i, Capture = capture });
            }
        }

        var manifest = new BackupManifest
        {
            AppVersion = AppVersion,
            WindowsBuild = _environment.Build,
            WindowsUbr = _environment.Ubr,
            CreatedUtc = _time.GetUtcNow(),
            BatchId = batchId,
            TweakIds = plan.Ready.Select(t => t.Id).ToList(),
        };
        var backupSetId = _store.CreateBackupSet(manifest, entries);

        var journal = _store.LoadJournal();
        var batch = new JournalBatch
        {
            BatchId = batchId,
            BackupSetId = backupSetId,
            CreatedUtc = _time.GetUtcNow(),
            Status = BatchStatus.Pending,
            Tweaks = plan.Ready.Select(t => new JournalTweak { Id = t.Id, Status = TweakJournalStatus.Pending }).ToList(),
        };
        journal.Batches.Add(batch);
        _store.SaveJournal(journal);
        return new BatchContext(batch, journal, entries);
    }

    internal void ApplyTweak(BatchContext context, TweakDefinition tweak)
    {
        context.Batch.Status = BatchStatus.Applying;
        context.JournalTweak(tweak.Id).Status = TweakJournalStatus.Applying;
        _store.SaveJournal(context.Journal);

        var written = new List<ActionCapture>();
        var started = _time.GetTimestamp();
        var planItems = tweak.Apply.Select(a => _executors.For(a).Describe(tweak.Id, a)).ToList();
        for (var i = 0; i < tweak.Apply.Count; i++)
        {
            var action = tweak.Apply[i];
            var executor = _executors.For(action);
            var before = context.Entries.Single(e => e.TweakId == tweak.Id && e.ActionIndex == i).Capture;

            // Vor dem Ausfuehren merken: auch eine halb ausgefuehrte Aktion wird beim Rollback zurueckgenommen.
            context.Touched.Push((tweak.Id, before));
            written.Add(executor.Apply(action, before));
        }

        for (var i = 0; i < tweak.Apply.Count; i++)
        {
            if (!_executors.For(tweak.Apply[i]).Verify(tweak.Apply[i]))
            {
                var target = _executors.For(tweak.Apply[i]).Describe(tweak.Id, tweak.Apply[i]);
                throw new AscendedTweaksException(
                    ErrorKind.ValueMismatchAfterWrite,
                    $"After writing, {target.Target} {target.ValueName} does not have the expected value (current {target.Current}). Windows or another program is blocking the change.",
                    tweak.Id);
            }
        }

        var detected = _detect.Evaluate(tweak.Detect);
        if (detected.State != TweakState.Applied)
        {
            throw new AscendedTweaksException(
                ErrorKind.ValueMismatchAfterWrite,
                $"State detection reports {detected.State} after applying.",
                tweak.Id);
        }

        var ledger = _store.LoadLedger();
        if (!ledger.Entries.Any(e => e.TweakId == tweak.Id))
        {
            // Bei erneutem Anwenden nach Drift bleibt der aeltere Eintrag mit dem echten Original erhalten.
            ledger.Entries.Add(new LedgerEntry
            {
                TweakId = tweak.Id,
                BatchId = context.Batch.BatchId,
                BackupSetId = context.Batch.BackupSetId,
                AppliedUtc = _time.GetUtcNow(),
                Captures = written,
            });
            _store.SaveLedger(ledger);
        }

        foreach (var item in planItems)
        {
            _log.Write(new LogRecord
            {
                Event = "TweakApplied",
                TweakId = tweak.Id,
                Target = item.ValueName is null ? item.Target : $"{item.Target} : {item.ValueName}",
                OldValue = item.Current,
                NewValue = item.New,
                Result = "Verified",
                DurationMs = (long)_time.GetElapsedTime(started).TotalMilliseconds,
            });
        }

        context.JournalTweak(tweak.Id).Status = TweakJournalStatus.Applied;
        _store.SaveJournal(context.Journal);
    }

    internal void Commit(BatchContext context)
    {
        _log.Write(new LogRecord { Event = "BatchCommitted", Target = context.Batch.BatchId, Result = string.Join(",", context.Batch.Tweaks.Select(t => t.Id)) });
        context.Batch.Status = BatchStatus.Committed;
        context.Batch.FinishedUtc = _time.GetUtcNow();
        _store.SaveJournal(context.Journal);
    }

    private List<string> Rollback(BatchContext context, string reason)
    {
        var errors = new List<string>();
        while (context.Touched.Count > 0)
        {
            var (tweakId, capture) = context.Touched.Pop();
            errors.AddRange(TryRestore(capture, tweakId));
            context.JournalTweak(tweakId).Status = TweakJournalStatus.RolledBack;
        }

        try
        {
            var ledger = _store.LoadLedger();
            ledger.Entries.RemoveAll(e => e.BatchId == context.Batch.BatchId);
            _store.SaveLedger(ledger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AscendedTweaksException)
        {
            errors.Add($"Ledger: {ex.Message}");
        }

        _log.Write(new LogRecord
        {
            Event = "BatchRolledBack",
            Level = LogLevel.Error,
            Target = context.Batch.BatchId,
            Result = errors.Count == 0 ? "RolledBack" : "RollbackFailed",
            Error = errors.Count == 0 ? reason : reason + " | " + string.Join(" | ", errors),
        });
        context.Batch.Status = errors.Count == 0 ? BatchStatus.RolledBack : BatchStatus.RollbackFailed;
        context.Batch.FinishedUtc = _time.GetUtcNow();
        context.Batch.Error = errors.Count == 0 ? reason : reason + " | Rollback: " + string.Join(" | ", errors);
        try
        {
            _store.SaveJournal(context.Journal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Journal: {ex.Message}");
        }

        return errors;
    }

    private List<string> TryRestore(ActionCapture capture, string tweakId)
    {
        try
        {
            var executor = _executors.ForCapture(capture);
            executor.Restore(capture);
            return executor.IsAtOriginalState(capture)
                ? []
                : [$"{tweakId}: {executor.View(capture).Target} does not match the original after resetting."];
        }
        catch (Exception ex)
        {
            return [$"{tweakId}: {ex.Message}"];
        }
    }

    private void RunExplicitRevert(TweakDefinition tweak)
    {
        foreach (var action in tweak.Revert.Actions!)
        {
            var executor = _executors.For(action);
            executor.Apply(action, executor.Capture(action));
            if (!executor.Verify(action))
            {
                throw new AscendedTweaksException(ErrorKind.ValueMismatchAfterWrite, "The reverse action did not take effect.", tweak.Id);
            }
        }
    }

    private PreflightEntry? CheckGates(TweakDefinition tweak, bool ignoreAdmin)
    {
        var support = SupportPolicy.Classify(_environment.Build);
        if (support == BuildSupport.Unsupported)
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.ReadOnlyMode, $"Windows build {_environment.Build} is not supported (read-only mode).");
        }

        if (support == BuildSupport.Untested && tweak.Risk == RiskLevel.Expert)
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.ExpertLockedOnUntestedBuild, $"Build {_environment.Build} is not tested; expert tweaks are locked.");
        }

        if (!tweak.SupportsBuild(_environment.Build))
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.NotSupportedOnBuild, $"Not intended for build {_environment.Build}.");
        }

        if (_environment.Edition is not { } edition || !tweak.Editions.Contains(edition))
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.EditionNotSupported, "Has no effect on this Windows edition.");
        }

        if (tweak.DesktopOnly && _environment.HasBattery)
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.Unavailable, "Only on PCs without a battery: it costs battery life on laptops.");
        }

        if (!ignoreAdmin && tweak.RequiresAdmin && !_environment.IsElevated)
        {
            return new PreflightEntry(tweak.Id, PreflightOutcome.AdminRequired, "Needs administrator rights.");
        }

        return null;
    }

    private static AscendedTweaksException Wrap(Exception ex, ErrorKind fallback, string message, string? tweakId = null)
    {
        return ex switch
        {
            AscendedTweaksException typed => typed,
            UnauthorizedAccessException or System.Security.SecurityException =>
                new AscendedTweaksException(ErrorKind.PermissionDenied, $"Access denied: {ex.Message}", tweakId, ex),
            _ => new AscendedTweaksException(fallback, message, tweakId, ex),
        };
    }

    private static string AppVersion =>
        typeof(TweakEngine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}

internal sealed class BatchContext
{
    public BatchContext(JournalBatch batch, JournalDocument journal, IReadOnlyList<BackupEntry> entries)
    {
        Batch = batch;
        Journal = journal;
        Entries = entries;
    }

    public JournalBatch Batch { get; }

    public JournalDocument Journal { get; }

    public IReadOnlyList<BackupEntry> Entries { get; }

    /// <summary>Alle begonnenen Aktionen in Ausfuehrungsreihenfolge; Rollback arbeitet sie rueckwaerts ab.</summary>
    public Stack<(string TweakId, ActionCapture Before)> Touched { get; } = new();

    public JournalTweak JournalTweak(string id) => Batch.Tweaks.First(t => t.Id == id);
}
