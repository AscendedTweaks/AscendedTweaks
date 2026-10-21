using TweakPilot.Core;
using TweakPilot.Engine.Executors;

namespace TweakPilot.Engine;

/// <summary>Liest den Ist-Zustand eines Tweaks. Veraendert nichts. Fehler fuehren zu "nicht verfuegbar" (Regel S12).</summary>
public sealed class DetectEvaluator
{
    private readonly ExecutorSet _executors;
    private readonly bool _elevated;

    public DetectEvaluator(ExecutorSet executors, bool elevated = true)
    {
        _executors = executors;
        _elevated = elevated;
    }

    public DetectResult Evaluate(DetectCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        try
        {
            return EvaluateCore(condition);
        }
        catch (TweakPilotException ex)
        {
            return new DetectResult(TweakState.Unavailable, ex.Message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new DetectResult(TweakState.Unavailable, $"State not readable: {ex.Message}");
        }
    }

    private DetectResult EvaluateCore(DetectCondition condition)
    {
        var registry = _executors.Registry;
        switch (condition)
        {
            case RegistryValueEqualsCondition c:
                var value = registry.GetValue(c.Hive, c.Path, c.Name);
                if (value is null)
                {
                    return DetectResult.Of(c.DefaultWhenMissing);
                }

                return DetectResult.Of(c.Value.Matches(value) ? TweakState.Applied : TweakState.NotApplied);

            case RegistryValueAbsentCondition c:
                return DetectResult.Of(registry.GetValue(c.Hive, c.Path, c.Name) is null ? TweakState.Applied : TweakState.NotApplied);

            case RegistryKeyExistsCondition c:
                return DetectResult.Of(registry.KeyExists(c.Hive, c.Path) ? TweakState.Applied : TweakState.NotApplied);

            case RegistryKeyAbsentCondition c:
                return DetectResult.Of(registry.KeyExists(c.Hive, c.Path) ? TweakState.NotApplied : TweakState.Applied);

            case ServiceStartTypeEqualsCondition c:
                var config = _executors.Services.GetConfig(c.Service);
                if (config is null)
                {
                    return new DetectResult(TweakState.Unavailable, $"Service {c.Service} is not installed.");
                }

                var (type, delayed) = ServiceExecutor.Map(c.Value);
                return DetectResult.Of(config.StartType == type && config.DelayedAutoStart == delayed ? TweakState.Applied : TweakState.NotApplied);

            case PowerPlanActiveCondition c:
                if (!_executors.Power.ListSchemes().Contains(c.SchemeGuid))
                {
                    return new DetectResult(c.MissingIs, $"Power plan {c.SchemeGuid} does not exist.");
                }

                return DetectResult.Of(_executors.Power.GetActive() == c.SchemeGuid ? TweakState.Applied : TweakState.NotApplied);

            case PowerSettingEqualsCondition c:
                var setting = _executors.PowerSettings.ReadActive(c.Subgroup, c.Setting);
                return setting is null
                    ? new DetectResult(TweakState.Unavailable, "This power setting does not exist on this device.")
                    : DetectResult.Of(setting == c.Value ? TweakState.Applied : TweakState.NotApplied);

            case CommandQueryEqualsCondition c:
                var current = _executors.Commands.Query(c.Command);
                if (current is null)
                {
                    var needsElevation = CommandWhitelist.All.TryGetValue(c.Command, out var definition) && definition.ReadRequiresAdmin && !_elevated;
                    return new DetectResult(
                        TweakState.Unavailable,
                        needsElevation
                            ? "The state can only be read with administrator rights; it is checked when applying."
                            : definition?.UnreadableReason ?? $"Current value for {c.Command} is not readable.")
                    {
                        NeedsElevation = needsElevation,
                    };
                }

                return DetectResult.Of(string.Equals(current, c.Value, StringComparison.Ordinal) ? TweakState.Applied : TweakState.NotApplied);

            case ScheduledTaskEnabledEqualsCondition c:
                var enabled = _executors.Tasks.Read(c.TaskPath);
                if (enabled is null)
                {
                    return new DetectResult(TweakState.Unavailable, $"Task {c.TaskPath} does not exist.");
                }

                return DetectResult.Of(enabled == c.Value ? TweakState.Applied : TweakState.NotApplied);

            case SystemParameterEqualsCondition c:
                return DetectResult.Of(_executors.Parameters.Read(c.Parameter) == c.Value ? TweakState.Applied : TweakState.NotApplied);

            case RegistryListEntryEqualsCondition c:
                var entry = _executors.ListEntries.ReadEntry(c.Hive, c.Path, c.Name, c.Format, c.Key);
                return DetectResult.Of(entry == c.Value ? TweakState.Applied : TweakState.NotApplied);

            case AllOfCondition c:
                var results = c.Conditions.Select(EvaluateCore).ToList();
                var unavailable = results.FirstOrDefault(r => r.State == TweakState.Unavailable);
                if (unavailable is not null)
                {
                    return unavailable;
                }

                if (results.All(r => r.State == TweakState.Applied))
                {
                    return DetectResult.Of(TweakState.Applied);
                }

                return DetectResult.Of(results.All(r => r.State == TweakState.NotApplied) ? TweakState.NotApplied : TweakState.Mixed);

            default:
                return new DetectResult(TweakState.Unavailable, $"Unknown condition {condition.GetType().Name}.");
        }
    }
}
