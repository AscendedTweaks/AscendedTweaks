using System.Security.AccessControl;
using System.Security.Principal;

namespace TweakPilot.Engine.Windows;

/// <summary>
/// ACL fuer %ProgramData%\TweakPilot (Abschnitt 7.1): Administratoren und SYSTEM Vollzugriff, Benutzer nur Lesen.
/// Vererbung von oben wird abgeschaltet, damit keine fremden Schreibrechte uebrig bleiben.
/// </summary>
public static class StateDirectorySecurity
{
    public static DirectorySecurity Create()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return security;
    }

    /// <summary>Legt den Ordner an und setzt die ACL. Nur im Helfer (Administrator) aufrufen.</summary>
    public static void Apply(string directory)
    {
        var info = Directory.CreateDirectory(directory);
        info.SetAccessControl(Create());
    }
}
