using AscendedTweaks.Core;
using AscendedTweaks.Engine.Windows;

namespace AscendedTweaks.Engine.Cleanup;

/// <summary>Setzt die Bereinigung mit den echten Windows-Zugriffen zusammen.</summary>
public static class CleanupFactory
{
    /// <summary>Benutzer-Kategorien im eigenen Prozess.</summary>
    public static CleanupEngine User(ISystemEnvironment environment) => Create(environment, CleanupBases.User(), TweakScope.User);

    /// <summary>Systemordner ohne Administratorrechte, nur zum Schaetzen (Run laeuft immer im Helfer).</summary>
    public static CleanupEngine MachineEstimator(ISystemEnvironment environment) => Create(environment, CleanupBases.Machine(), TweakScope.Machine);

    private static CleanupEngine Create(ISystemEnvironment environment, IReadOnlyDictionary<CleanupBase, string> bases, TweakScope scope)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return new CleanupEngine(
            new WindowsCleanupFileSystem(),
            new WindowsRecycler(),
            new WindowsProcessProbe(),
            new WindowsCleanupSpecials(new WindowsRegistryAccess(), new ProcessCommandRunner(), environment.SystemDirectory),
            new WindowsServiceAccess(),
            bases,
            CleanupBases.Protected(),
            scope,
            TimeProvider.System);
    }
}
