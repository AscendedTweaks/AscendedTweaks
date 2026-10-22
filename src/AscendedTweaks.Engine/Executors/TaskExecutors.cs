using System.Globalization;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Executors;

internal sealed class ScheduledTaskExecutor : ActionExecutor<ScheduledTaskSetEnabledAction, ScheduledTaskCapture>
{
    private readonly IScheduledTaskAccess _tasks;

    public ScheduledTaskExecutor(IScheduledTaskAccess tasks) => _tasks = tasks;

    public static string Display(bool enabled) => enabled ? "Enabled" : "Disabled";

    public bool? Read(string taskPath)
    {
        Guard(taskPath);
        return _tasks.GetEnabled(taskPath);
    }

    protected override string TargetKey(ScheduledTaskSetEnabledAction action) => $"task:{action.TaskPath}".ToUpperInvariant();

    protected override ScheduledTaskCapture Capture(ScheduledTaskSetEnabledAction action)
    {
        return new ScheduledTaskCapture { TaskPath = action.TaskPath, BeforeEnabled = GetRequired(action.TaskPath) };
    }

    protected override ScheduledTaskCapture Apply(ScheduledTaskSetEnabledAction action, ScheduledTaskCapture before)
    {
        Guard(action.TaskPath);
        _tasks.SetEnabled(action.TaskPath, action.Enabled);
        return before with { Applied = true, WrittenEnabled = action.Enabled };
    }

    protected override bool Verify(ScheduledTaskSetEnabledAction action) => Read(action.TaskPath) == action.Enabled;

    protected override void Restore(ScheduledTaskCapture capture)
    {
        if (GetRequired(capture.TaskPath) != capture.BeforeEnabled)
        {
            _tasks.SetEnabled(capture.TaskPath, capture.BeforeEnabled);
        }
    }

    protected override bool IsAtWrittenState(ScheduledTaskCapture capture) => Read(capture.TaskPath) == capture.WrittenEnabled;

    protected override bool IsAtOriginalState(ScheduledTaskCapture capture) => Read(capture.TaskPath) == capture.BeforeEnabled;

    protected override PlanItem Describe(string tweakId, ScheduledTaskSetEnabledAction action)
    {
        var current = Read(action.TaskPath);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = $"Task {action.TaskPath}",
            Current = current is { } c ? Display(c) : "(not present)",
            New = Display(action.Enabled),
        };
    }

    protected override CaptureView View(ScheduledTaskCapture capture)
    {
        var current = Read(capture.TaskPath);
        return new CaptureView(
            $"Task {capture.TaskPath}",
            current is { } c ? Display(c) : "(not present)",
            Display(capture.BeforeEnabled),
            capture.WrittenEnabled is { } w ? Display(w) : EngineText.Missing);
    }

    private static void Guard(string taskPath)
    {
        if (!ScheduledTaskPaths.IsAllowed(taskPath))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Task path {taskPath} is not allowed.");
        }
    }

    private bool GetRequired(string taskPath)
    {
        return Read(taskPath)
            ?? throw new AscendedTweaksException(ErrorKind.Unavailable, $"Task {taskPath} does not exist.");
    }
}

internal sealed class SystemParameterExecutor : ActionExecutor<SystemParametersInfoAction, SystemParameterCapture>
{
    private readonly ISystemParametersAccess _parameters;

    public SystemParameterExecutor(ISystemParametersAccess parameters) => _parameters = parameters;

    public uint Read(string parameter)
    {
        SystemParameterWhitelist.Get(parameter);
        return _parameters.Get(parameter);
    }

    protected override string TargetKey(SystemParametersInfoAction action) => $"spi:{action.Parameter}".ToUpperInvariant();

    protected override SystemParameterCapture Capture(SystemParametersInfoAction action)
    {
        return new SystemParameterCapture { Parameter = action.Parameter, BeforeValue = Read(action.Parameter) };
    }

    protected override SystemParameterCapture Apply(SystemParametersInfoAction action, SystemParameterCapture before)
    {
        Set(action.Parameter, action.Value);
        return before with { Applied = true, WrittenValue = action.Value };
    }

    protected override bool Verify(SystemParametersInfoAction action) => Read(action.Parameter) == action.Value;

    protected override void Restore(SystemParameterCapture capture)
    {
        if (Read(capture.Parameter) != capture.BeforeValue)
        {
            Set(capture.Parameter, capture.BeforeValue);
        }
    }

    protected override bool IsAtWrittenState(SystemParameterCapture capture) => Read(capture.Parameter) == capture.WrittenValue;

    protected override bool IsAtOriginalState(SystemParameterCapture capture) => Read(capture.Parameter) == capture.BeforeValue;

    protected override PlanItem Describe(string tweakId, SystemParametersInfoAction action)
    {
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = $"Systemeinstellung {action.Parameter}",
            Current = Read(action.Parameter).ToString(CultureInfo.InvariantCulture),
            New = action.Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    protected override CaptureView View(SystemParameterCapture capture)
    {
        return new CaptureView(
            $"Systemeinstellung {capture.Parameter}",
            Read(capture.Parameter).ToString(CultureInfo.InvariantCulture),
            capture.BeforeValue.ToString(CultureInfo.InvariantCulture),
            capture.WrittenValue?.ToString(CultureInfo.InvariantCulture) ?? EngineText.Missing);
    }

    private void Set(string parameter, uint value)
    {
        var definition = SystemParameterWhitelist.Get(parameter);
        if (value < definition.Min || value > definition.Max)
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Value {value} is not allowed for {parameter}.");
        }

        _parameters.Set(parameter, value);
    }
}

/// <summary>Platzhalter, wenn eine Umgebung keine Aufgabenplanung bereitstellt (zum Beispiel aeltere Tests).</summary>
internal sealed class UnavailableScheduledTasks : IScheduledTaskAccess
{
    public bool? GetEnabled(string taskPath) => null;

    public void SetEnabled(string taskPath, bool enabled) =>
        throw new AscendedTweaksException(ErrorKind.Unavailable, "Task Scheduler is not available.");
}

internal sealed class UnavailableSystemParameters : ISystemParametersAccess
{
    public uint Get(string parameter) =>
        throw new AscendedTweaksException(ErrorKind.Unavailable, "SystemParametersInfo is not available.");

    public void Set(string parameter, uint value) => Get(parameter);
}
