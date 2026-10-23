using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Executors;

/// <summary>Ordnet Aktionen und Sicherungen ihrem Executor zu.</summary>
public sealed class ExecutorSet
{
    private readonly Dictionary<Type, IActionExecutor> _byAction;
    private readonly Dictionary<Type, IActionExecutor> _byCapture = [];

    public ExecutorSet(
        IRegistryAccess registry,
        IServiceAccess services,
        IPowerPlanAccess power,
        ICommandRunner commands,
        string systemDirectory,
        IScheduledTaskAccess? tasks = null,
        ISystemParametersAccess? parameters = null,
        INetworkAccess? network = null,
        IDefenderAccess? defender = null,
        IDisplayAccess? display = null)
    {
        Registry = registry;
        Services = services;
        Power = power;
        Commands = new CommandExecutor(commands, systemDirectory, power, network, defender);
        PowerSettings = new PowerSettingExecutor(power);
        Tasks = new ScheduledTaskExecutor(tasks ?? new UnavailableScheduledTasks());
        Parameters = new SystemParameterExecutor(parameters ?? new UnavailableSystemParameters());
        ListEntries = new RegistryListEntryExecutor(registry);
        Display = new DisplayRefreshExecutor(display ?? new UnavailableDisplay());

        IActionExecutor[] all =
        [
            new RegistrySetValueExecutor(registry),
            new RegistryDeleteValueExecutor(registry),
            new RegistryCreateKeyExecutor(registry),
            new RegistryDeleteKeyExecutor(registry),
            new ServiceExecutor(services),
            new PowerPlanExecutor(power),
            PowerSettings,
            Commands,
            Tasks,
            Parameters,
            ListEntries,
            Display,
        ];
        _byAction = all.ToDictionary(e => e.ActionType);

        // Mehrere Executors teilen sich einen Sicherungstyp; die Wiederherstellung ist dort identisch.
        foreach (var executor in all)
        {
            _byCapture.TryAdd(executor.CaptureType, executor);
        }
    }

    internal IRegistryAccess Registry { get; }

    internal IServiceAccess Services { get; }

    internal IPowerPlanAccess Power { get; }

    internal CommandExecutor Commands { get; }

    internal PowerSettingExecutor PowerSettings { get; }

    internal ScheduledTaskExecutor Tasks { get; }

    internal SystemParameterExecutor Parameters { get; }

    internal RegistryListEntryExecutor ListEntries { get; }

    internal DisplayRefreshExecutor Display { get; }

    public IActionExecutor For(TweakAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _byAction.TryGetValue(action.GetType(), out var executor)
            ? executor
            : throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"No executor for {action.GetType().Name}.");
    }

    public IActionExecutor ForCapture(ActionCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return _byCapture.TryGetValue(capture.GetType(), out var executor)
            ? executor
            : throw new AscendedTweaksException(ErrorKind.StateCorrupt, $"No executor for backup {capture.GetType().Name}.");
    }
}
