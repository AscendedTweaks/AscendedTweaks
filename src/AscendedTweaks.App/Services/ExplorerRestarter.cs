using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace AscendedTweaks.App.Services;

public interface IExplorerRestarter
{
    void Restart();
}

/// <summary>
/// Startet den Windows-Explorer des angemeldeten Benutzers neu (nur nach Rueckfrage). Keine Admin-Rechte noetig.
/// Windows startet die Shell nach dem Beenden meist selbst neu; nur wenn nicht, startet die App sie.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Ends the real Windows shell; testable manually only.")]
public sealed class ExplorerRestarter : IExplorerRestarter
{
    public void Restart()
    {
        var session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName("explorer").Where(p => p.SessionId == session))
        {
            using (process)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }

        for (var i = 0; i < 20; i++)
        {
            Thread.Sleep(250);
            if (Process.GetProcessesByName("explorer").Any(p => p.SessionId == session))
            {
                return;
            }
        }

        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false });
    }
}
