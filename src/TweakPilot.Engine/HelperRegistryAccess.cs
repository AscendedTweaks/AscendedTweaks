using TweakPilot.Core;

namespace TweakPilot.Engine;

/// <summary>
/// Registry-Zugriff fuer den Helfer (Abschnitt 9.2): HKLM direkt. HKCU nie direkt, weil der Helfer unter einem anderen
/// Konto laufen kann. Benutzer-Richtlinien (HKCU\Software\Policies) werden gezielt nach HKEY_USERS\SID des
/// anfragenden Benutzers umgeleitet; jeder andere HKCU-Zugriff wird abgelehnt.
/// </summary>
public sealed class HelperRegistryAccess : IRegistryAccess
{
    private readonly IRegistryAccess _inner;
    private static readonly HashSet<string> PolicyAncestors = new(StringComparer.OrdinalIgnoreCase) { "Software", RegistryPaths.PolicyPrefix.TrimEnd('\\') };

    private readonly string? _policyUserSid;

    public HelperRegistryAccess(IRegistryAccess inner, string? policyUserSid = null)
    {
        _inner = inner;
        _policyUserSid = policyUserSid;
    }

    public RegistryData? GetValue(RegHive hive, string path, string name)
    {
        var (h, p) = Map(hive, path, write: false);
        return _inner.GetValue(h, p, name);
    }

    public bool KeyExists(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: false);
        return _inner.KeyExists(h, p);
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: false);
        return _inner.GetValueNames(h, p);
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: false);
        return _inner.GetSubKeyNames(h, p);
    }

    public void SetValue(RegHive hive, string path, string name, RegistryData data)
    {
        var (h, p) = Map(hive, path, write: true);
        _inner.SetValue(h, p, name, data);
    }

    public void DeleteValue(RegHive hive, string path, string name)
    {
        var (h, p) = Map(hive, path, write: true);
        _inner.DeleteValue(h, p, name);
    }

    public void CreateKey(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: true);
        _inner.CreateKey(h, p);
    }

    public void DeleteEmptyKey(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: true);
        _inner.DeleteEmptyKey(h, p);
    }

    public void DeleteKeyTree(RegHive hive, string path)
    {
        var (h, p) = Map(hive, path, write: true);
        _inner.DeleteKeyTree(h, p);
    }

    private (RegHive Hive, string Path) Map(RegHive hive, string path, bool write)
    {
        if (hive == RegHive.HKLM)
        {
            return (hive, path);
        }

        // Die Vorfahren Software und Software\Policies duerfen nur gelesen werden (Pruefung, welche Schluessel fehlen).
        var policyAncestor = !write && hive == RegHive.HKCU && PolicyAncestors.Contains(path);
        if (_policyUserSid is not null && (policyAncestor || RegistryPaths.IsUserPolicyPath(hive, path)))
        {
            return (RegHive.HKU, $"{_policyUserSid}\\{path}");
        }

        throw new TweakPilotException(ErrorKind.PermissionDenied, "The helper process never accesses HKCU directly; only user policies of the requesting user are allowed.");
    }
}
