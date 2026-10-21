namespace TweakPilot.Core;

/// <summary>Pruefung von Registry-Pfaden und Allowlist fuer das Anlegen und Loeschen von Schluesseln.</summary>
public static class RegistryPaths
{
    /// <summary>
    /// Nur unterhalb dieser Wurzeln duerfen Schluessel angelegt oder geloescht werden.
    /// Jede Phase erweitert die Liste nur um verifizierte Bereiche.
    /// </summary>
    public static readonly IReadOnlyList<(RegHive Hive, string Prefix)> KeyAllowlist =
    [
        (RegHive.HKCU, @"Software\Classes\CLSID\"),
        (RegHive.HKCU, @"Software\Microsoft\"),
        (RegHive.HKCU, @"Software\Policies\Microsoft\"),
        (RegHive.HKCU, @"Control Panel\"),
        (RegHive.HKCU, @"System\GameConfigStore\"),
        (RegHive.HKLM, @"SOFTWARE\Policies\Microsoft\"),
        (RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\"),
    ];

    public static bool IsWellFormed(string path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
        }
        else if (path.StartsWith('\\') || path.EndsWith('\\'))
        {
            error = "Path must not start or end with '\\'.";
        }
        else if (path.Contains('/', StringComparison.Ordinal))
        {
            error = "Path must not contain '/'.";
        }
        else if (path.Contains(@"\\", StringComparison.Ordinal))
        {
            error = "Path contains empty segments.";
        }
        else if (path.Split('\\').Any(s => s is "." or ".." || s.Trim().Length != s.Length))
        {
            error = "Path contains invalid segments.";
        }
        else if (path.Any(char.IsControl))
        {
            error = "Path contains control characters.";
        }
        else if (path.Length > 1024)
        {
            error = "Path is too long.";
        }

        return error is null;
    }

    public static bool IsKeyOperationAllowed(RegHive hive, string path)
    {
        if (!IsWellFormed(path, out _))
        {
            return false;
        }

        // Der Schluessel muss echt unterhalb einer Wurzel liegen, nie die Wurzel selbst.
        return KeyAllowlist.Any(a => a.Hive == hive
            && path.Length > a.Prefix.Length
            && path.StartsWith(a.Prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string Display(RegHive hive, string path) => $"{hive}\\{path}";

    public const string PolicyPrefix = @"Software\Policies\";

    /// <summary>Benutzer-Richtlinie: HKCU unterhalb von Software\Policies (nur fuer Administratoren beschreibbar).</summary>
    public static bool IsUserPolicyPath(RegHive hive, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return hive == RegHive.HKCU && path.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bereich, in dem eine Registry-Aktion ausgefuehrt werden muss.</summary>
    public static TweakScope ScopeFor(RegHive hive, string path)
    {
        return hive switch
        {
            RegHive.HKCU => IsUserPolicyPath(hive, path) ? TweakScope.UserPolicy : TweakScope.User,
            _ => TweakScope.Machine,
        };
    }

    /// <summary>Alle Vorfahren-Pfade, vom kuerzesten zum laengsten, inklusive des Pfads selbst.</summary>
    public static IReadOnlyList<string> Chain(string path)
    {
        var segments = path.Split('\\');
        var result = new List<string>(segments.Length);
        for (var i = 1; i <= segments.Length; i++)
        {
            result.Add(string.Join('\\', segments.Take(i)));
        }

        return result;
    }
}
