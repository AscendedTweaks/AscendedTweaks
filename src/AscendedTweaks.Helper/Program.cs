using System.Globalization;
using System.Security.Principal;
using AscendedTweaks.Catalog;
using AscendedTweaks.Core;
using AscendedTweaks.Engine;
using AscendedTweaks.Engine.Logging;
using AscendedTweaks.Engine.State;
using AscendedTweaks.Engine.Windows;
using AscendedTweaks.Ipc;

namespace AscendedTweaks.Helper;

/// <summary>
/// Privilegierter Helfer (Abschnitt 9). Wird nur von App oder CLI per UAC gestartet, fuehrt nur Whitelist-Befehle
/// fuer Maschinen-Tweaks aus und beendet sich nach 120 Sekunden Leerlauf oder wenn der startende Prozess endet.
/// Legt nie Autostart, Dienst oder geplante Aufgabe an.
/// </summary>
internal static class Program
{
    private const int Success = 0;
    private const int InvalidArguments = 5;
    private const int Failure = 1;

    private static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out var pipeName, out var parentPid, out var clientUser))
        {
            return InvalidArguments;
        }

        var root = ProductInfo.DataDirectory(Environment.SpecialFolder.CommonApplicationData);
        ILogSink log = NullLogSink.Instance;
        try
        {
            StateDirectorySecurity.Apply(root);
            log = new JsonLinesLog(JsonLinesLog.DefaultMachineDirectory, "helper", TimeProvider.System);
            log.Write(new LogRecord { Event = "HelperStarted", Target = parentPid.ToString(CultureInfo.InvariantCulture) });

            var catalog = new CompositeCatalog(CatalogLoader.LoadEmbedded());
            var environment = new WindowsSystemEnvironment();
            var restorePoints = new WindowsRestorePointService();
            var handler = new EngineHelperHandler(
                catalog,
                (rp, scope) => scope == TweakScope.UserPolicy
                    ? EngineFactory.CreateForWindows(catalog, StateStore.UserPolicyRoot(clientUser.Value), environment, log, rp, machineOnly: true, policyUserSid: clientUser.Value)
                    : EngineFactory.CreateForWindows(catalog, StateStore.DefaultMachineRoot, environment, log, rp, machineOnly: true),
                restorePoints,
                environment.IsElevated,
                new AscendedTweaks.Engine.Cleanup.CleanupEngine(
                    new WindowsCleanupFileSystem(),
                    new WindowsRecycler(),
                    new WindowsProcessProbe(),
                    new WindowsCleanupSpecials(new WindowsRegistryAccess(), new ProcessCommandRunner(), environment.SystemDirectory),
                    new WindowsServiceAccess(),
                    CleanupBases.Machine(),
                    AscendedTweaks.Engine.Cleanup.PathGuard.ProtectedFolders(Environment.GetFolderPath(Environment.SpecialFolder.Windows), null),
                    TweakScope.Machine,
                    TimeProvider.System),
                new AscendedTweaks.Engine.Debloat.AppRemover(new AscendedTweaks.Appx.WindowsAppxAccess(), TimeProvider.System),
                new AscendedTweaks.Engine.Tools.Maintenance(new ProcessCommandRunner(), new WindowsVolumeInventory(), environment.SystemDirectory),
                new WindowsDiskHealth());

            var verifier = new WindowsClientVerifier(new ClientVerifierOptions
            {
                ExpectedProcessId = parentPid,
                ExpectedUser = clientUser,
                AllowedDirectories = WindowsClientVerifier.DirectoriesFor(AppContext.BaseDirectory),
#if DEBUG
                RequireSignature = false,
#else
                // A signed helper accepts only a client signed by the same publisher. An unsigned build has no publisher to
                // compare, so it relies on the checks above (launching process, install folder, user) and on the app checking
                // the helper's SHA-256 before starting it. Without this, every unsigned release refused every client.
                RequireSignature = Authenticode.IsTrusted(Environment.ProcessPath!),
#endif
            });

            var server = new HelperServer(
                new HelperServerOptions { PipeName = pipeName, ClientUser = clientUser, Verifier = verifier, ParentProcessId = parentPid },
                handler,
                log);
            await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
            log.Write(new LogRecord { Event = "HelperStopped" });
            return Success;
        }
        catch (Exception ex)
        {
            log.Write(new LogRecord { Event = "HelperCrashed", Level = LogLevel.Error, Error = ex.ToString() });
            return Failure;
        }
    }

    private static bool TryParse(string[] args, out string pipeName, out int parentPid, out SecurityIdentifier clientUser)
    {
        pipeName = string.Empty;
        parentPid = 0;
        clientUser = null!;
        if (args.Length != 6 || args[0] != "--pipe" || args[2] != "--parent" || args[4] != "--client-sid")
        {
            return false;
        }

        try
        {
            clientUser = new SecurityIdentifier(args[5]);
        }
        catch (ArgumentException)
        {
            return false;
        }

        pipeName = args[1];
        return int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out parentPid)
            && parentPid > 0
            && IpcProtocol.IsValidPipeName(pipeName, clientUser);
    }
}
