using TweakPilot.Core;
using TweakPilot.Engine.Executors;
using TweakPilot.Engine.State;
using TweakPilot.Engine.Windows;

namespace TweakPilot.Engine;

/// <summary>Setzt die Engine mit den echten Windows-Zugriffen zusammen.</summary>
public static class EngineFactory
{
    /// <summary>
    /// Engine mit Windows-Zugriffen. <paramref name="machineOnly"/> sperrt jeden HKCU-Zugriff (Helfer-Prozess).
    /// Ohne <paramref name="restorePoints"/> werden keine Wiederherstellungspunkte erstellt (Benutzer-Tweaks, Vorschau).
    /// </summary>
    public static TweakEngine CreateForWindows(
        ITweakCatalog catalog,
        string stateRoot,
        ISystemEnvironment environment,
        ILogSink? log = null,
        IRestorePointService? restorePoints = null,
        bool machineOnly = false,
        string? policyUserSid = null)
    {
        IRegistryAccess registry = new WindowsRegistryAccess();
        if (machineOnly)
        {
            registry = new HelperRegistryAccess(registry, policyUserSid);
        }

        var commands = new ProcessCommandRunner();
        var executors = new ExecutorSet(
            registry,
            new WindowsServiceAccess(),
            new WindowsPowerPlanAccess(),
            commands,
            environment.SystemDirectory,
            new WindowsScheduledTaskAccess(commands, environment.SystemDirectory),
            machineOnly ? null : new WindowsSystemParametersAccess(),
            new WindowsNetworkAccess(commands, environment.SystemDirectory),
            new WindowsDefenderAccess(registry));
        return new TweakEngine(
            catalog,
            executors,
            new StateStore(stateRoot, TimeProvider.System),
            environment,
            restorePoints ?? new NoRestorePointService(),
            TimeProvider.System,
            log);
    }
}
