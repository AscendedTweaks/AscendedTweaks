using TweakPilot.Core;

namespace TweakPilot.Engine.Insights;

/// <summary>Zustand einer Datenschutz-Freigabe, wie Windows sie im ConsentStore ablegt.</summary>
public enum ConsentState
{
    /// <summary>Kein eigener Eintrag; es gilt die uebergeordnete Einstellung.</summary>
    Inherited,
    Allow,
    Deny,

    /// <summary>Windows fragt beim ersten Zugriff nach.</summary>
    Prompt,
}

public sealed record PermissionApp(string Name, string? Path, bool IsDesktopApp, ConsentState State, DateTimeOffset? LastUsed);

public sealed record PermissionCapability(
    string Capability,
    ConsentState Device,
    ConsentState User,
    ConsentState DesktopApps,
    IReadOnlyList<PermissionApp> Apps);

/// <summary>
/// Berechtigungs-Uebersicht (Abschnitt 6.4, nur Anzeige): liest pro Faehigkeit die Freigaben aus
/// CapabilityAccessManager\ConsentStore. Diese Klasse schreibt nie; sie bekommt nur lesende Aufrufe.
/// </summary>
public sealed class PermissionOverview
{
    public const string ConsentStorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public static readonly IReadOnlyList<string> Capabilities = ["webcam", "microphone", "location"];

    private const string NonPackaged = "NonPackaged";

    private readonly IRegistryAccess _registry;
    private readonly string? _userProfile;

    public PermissionOverview(IRegistryAccess registry, string? userProfile = null)
    {
        _registry = registry;
        _userProfile = userProfile?.TrimEnd('\\');
    }

    public IReadOnlyList<PermissionCapability> Read()
    {
        return Capabilities.Select(ReadCapability).ToList();
    }

    private PermissionCapability ReadCapability(string capability)
    {
        var userKey = $"{ConsentStorePath}\\{capability}";
        var apps = new List<PermissionApp>();
        foreach (var package in _registry.GetSubKeyNames(RegHive.HKCU, userKey).Where(n => !n.Equals(NonPackaged, StringComparison.OrdinalIgnoreCase)))
        {
            var key = $"{userKey}\\{package}";
            apps.Add(new PermissionApp(PackageDisplayName(package), null, false, State(RegHive.HKCU, key), LastUsed(key)));
        }

        var desktopKey = $"{userKey}\\{NonPackaged}";
        foreach (var entry in _registry.GetSubKeyNames(RegHive.HKCU, desktopKey))
        {
            var key = $"{desktopKey}\\{entry}";
            var path = HidePrivatePath(entry.Replace('#', '\\'));
            apps.Add(new PermissionApp(System.IO.Path.GetFileName(path), path, true, State(RegHive.HKCU, key), LastUsed(key)));
        }

        return new PermissionCapability(
            capability,
            State(RegHive.HKLM, userKey),
            State(RegHive.HKCU, userKey),
            State(RegHive.HKCU, desktopKey),
            apps.OrderByDescending(a => a.LastUsed ?? DateTimeOffset.MinValue).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private ConsentState State(RegHive hive, string key)
    {
        return _registry.GetValue(hive, key, "Value")?.Text?.Trim() switch
        {
            { } t when t.Equals("Allow", StringComparison.OrdinalIgnoreCase) => ConsentState.Allow,
            { } t when t.Equals("Deny", StringComparison.OrdinalIgnoreCase) => ConsentState.Deny,
            { } t when t.Equals("Prompt", StringComparison.OrdinalIgnoreCase) => ConsentState.Prompt,
            _ => ConsentState.Inherited,
        };
    }

    private DateTimeOffset? LastUsed(string key)
    {
        var data = _registry.GetValue(RegHive.HKCU, key, "LastUsedTimeStart");
        if (data is null || data.Kind != RegValueKind.QWord || data.Number == 0 || data.Number > long.MaxValue)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime((long)data.Number);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Paketfamilienname ohne Herausgeber-Hash: "Microsoft.WindowsCamera_8wekyb3d8bbwe" wird "Microsoft.WindowsCamera".</summary>
    private static string PackageDisplayName(string package)
    {
        var underscore = package.LastIndexOf('_');
        return underscore > 0 ? package[..underscore] : package;
    }

    /// <summary>Der Benutzerordner wird wie im Protokoll durch %USERPROFILE% ersetzt (Bildschirmfotos ohne Namen).</summary>
    private string HidePrivatePath(string path)
    {
        return _userProfile is { Length: > 0 } && path.StartsWith(_userProfile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[_userProfile.Length..]
            : path;
    }
}
