using System.Text.Json;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Cleanup;
using AscendedTweaks.Engine.Debloat;

namespace AscendedTweaks.Ipc;

/// <summary>
/// Befehle des Helfers. Fuehrt ausschliesslich Maschinen-Tweaks und Benutzer-Richtlinien aus dem eingebetteten Katalog aus;
/// keine freien Befehle, Pfade oder Skripte (Abschnitt 9.3). Die Engine je Bereich liefert die Fabrik.
/// </summary>
public sealed class EngineHelperHandler : IHelperHandler
{
    private readonly ITweakCatalog _catalog;
    private readonly Func<IRestorePointService, TweakScope, TweakEngine> _engineFactory;
    private readonly IRestorePointService _restorePoints;
    private readonly bool _elevated;
    private readonly CleanupEngine? _cleanup;
    private readonly AppRemover? _apps;
    private readonly AscendedTweaks.Engine.Tools.Maintenance? _maintenance;
    private readonly AscendedTweaks.Engine.Tools.IDiskHealthSource? _diskHealth;

    /// <param name="cleanup">Bereinigung fuer Maschinen-Kategorien; null, wenn dieser Helfer keine Bereinigung anbietet.</param>
    public EngineHelperHandler(
        ITweakCatalog catalog,
        Func<IRestorePointService, TweakScope, TweakEngine> engineFactory,
        IRestorePointService restorePoints,
        bool elevated,
        CleanupEngine? cleanup = null,
        AppRemover? apps = null,
        AscendedTweaks.Engine.Tools.Maintenance? maintenance = null,
        AscendedTweaks.Engine.Tools.IDiskHealthSource? diskHealth = null)
    {
        _apps = apps;
        _maintenance = maintenance;
        _diskHealth = diskHealth;
        _catalog = catalog;
        _engineFactory = engineFactory;
        _restorePoints = restorePoints;
        _elevated = elevated;
        _cleanup = cleanup;
    }

    public object? Handle(string command, JsonElement? payload)
    {
        switch (command)
        {
            case IpcCommands.Ping:
                return new PingResponse { ProcessId = Environment.ProcessId, Elevated = _elevated };

            case IpcCommands.ApplyTweaks:
                {
                    var request = IpcProtocol.Deserialize<ApplyTweaksRequest>(payload);
                    if (request.Ids.Count is 0 or > IpcProtocol.MaxTweaksPerRequest)
                    {
                        throw new IpcProtocolException("Invalid number of tweaks.");
                    }

                    var scopes = request.Ids.Select(id => RequireHelperTweak(id).Scope).Distinct().ToList();
                    if (scopes.Count != 1)
                    {
                        throw new IpcProtocolException("A request may only contain tweaks of one scope.");
                    }

                    var restorePoints = request.RestorePointHandled
                        ? new PresetRestorePointService(new RestorePointResult(RestorePointStatus.HandledSeparately, "Handled by the caller."))
                        : _restorePoints;
                    var result = _engineFactory(restorePoints, scopes[0]).ApplyBatch(request.Ids, new ApplyOptions
                    {
                        DryRun = request.DryRun,
                        ConfirmWithoutRestorePoint = _ => request.ProceedWithoutRestorePoint,
                    });
                    return BatchResultDto.From(result);
                }

            case IpcCommands.RevertTweak:
                {
                    var request = IpcProtocol.Deserialize<RevertTweakRequest>(payload);
                    var tweak = RequireHelperTweak(request.Id);
                    var pending = new List<RevertConflict>();
                    var result = _engineFactory(_restorePoints, tweak.Scope).RevertTweak(tweak.Id, conflict =>
                    {
                        if (request.Decisions.TryGetValue(conflict.CaptureIndex, out var decision))
                        {
                            return decision;
                        }

                        pending.Add(conflict);
                        return ConflictDecision.Cancel;
                    });
                    return new RevertTweakResponse
                    {
                        Outcome = result.Outcome,
                        ErrorKind = result.Error?.Kind,
                        Error = result.Error?.Message,
                        PendingConflicts = pending,
                    };
                }

            case IpcCommands.Capture:
                {
                    var request = IpcProtocol.Deserialize<TweakIdRequest>(payload);
                    var tweak = RequireHelperTweak(request.Id);
                    return _engineFactory(_restorePoints, tweak.Scope).GetStatus(tweak.Id);
                }

            case IpcCommands.CreateRestorePoint:
                {
                    var request = IpcProtocol.Deserialize<RestorePointRequest>(payload);
                    if (request.Name.Length is 0 or > 64 || request.Name.Any(char.IsControl))
                    {
                        throw new IpcProtocolException("Invalid name for the restore point.");
                    }

                    return _restorePoints.TryCreate(request.Name);
                }

            case IpcCommands.RecoverInterrupted:
                {
                    var request = IpcProtocol.Deserialize<RecoverRequest>(payload);
                    if (request.BatchId.Length != 32 || !request.BatchId.All(Uri.IsHexDigit) || request.Scope == TweakScope.User)
                    {
                        throw new IpcProtocolException("Invalid batch ID or invalid scope.");
                    }

                    return _engineFactory(_restorePoints, request.Scope).RecoverInterrupted(request.BatchId).ToList();
                }

            case IpcCommands.ScanCleanup:
                return RequireCleanup(payload, out var scanRequest).Scan(scanRequest.CategoryId);

            case IpcCommands.RunCleanup:
                return RequireCleanup(payload, out var runRequest).Run(runRequest.CategoryId, runRequest.Mode);

            case IpcCommands.RunMaintenance:
                {
                    var request = IpcProtocol.Deserialize<MaintenanceRequest>(payload);
                    if (_maintenance is null || !Enum.IsDefined(request.Action) || (request.Drive is { } d && !char.IsAsciiLetterUpper(d)))
                    {
                        throw new IpcProtocolException("Invalid maintenance request.");
                    }

                    return _maintenance.Run(request.Action, request.Drive);
                }

            case IpcCommands.ReadDiskHealth:
                return (_diskHealth ?? throw new IpcProtocolException("SMART values are not available in this helper.")).Read().ToList();

            case IpcCommands.RemoveApps:
                {
                    var request = IpcProtocol.Deserialize<RemoveAppsRequest>(payload);
                    if (_apps is null)
                    {
                        throw new IpcProtocolException("Removing apps is not available in this helper.");
                    }

                    if (request.Names.Count is 0 or > RemoveAppsRequest.MaxApps || request.Names.Any(n => !DebloatCatalog.IsValidPackageName(n)))
                    {
                        throw new IpcProtocolException("Invalid package names.");
                    }

                    return _apps.Remove(request.Names, request.Deprovision).ToList();
                }

            default:
                throw new IpcProtocolException($"Command {command} is not allowed.");
        }
    }

    private CleanupEngine RequireCleanup(JsonElement? payload, out CleanupRequest request)
    {
        request = IpcProtocol.Deserialize<CleanupRequest>(payload);
        if (_cleanup is null)
        {
            throw new IpcProtocolException("Cleanup is not available in this helper.");
        }

        if (!CleanupCategories.TryGet(request.CategoryId, out var category) || !Enum.IsDefined(request.Mode))
        {
            throw new IpcProtocolException($"Unknown cleanup category {request.CategoryId}.");
        }

        return category.Scope == TweakScope.Machine
            ? _cleanup
            : throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"{category.Id} is a user category; the helper only cleans system folders.");
    }

    private TweakDefinition RequireHelperTweak(string id)
    {
        if (!IpcProtocol.IsValidTweakId(id) || !_catalog.TryGet(id, out var tweak))
        {
            throw new IpcProtocolException($"Unknown tweak ID {id}.");
        }

        return tweak.Scope != TweakScope.User
            ? tweak
            : throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"{id} is a user tweak; the helper only runs machine tweaks and user policies.", id);
    }
}
