using System.Text.Json;
using System.Text.RegularExpressions;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Debloat;

public enum AppGroup
{
    News,
    Office,
    Communication,
    Games,
    Media,
    DeveloperAi,
    Other,
}

/// <summary>Ein Appx-Paket, wie Windows es meldet.</summary>
public sealed record AppxPackageInfo(
    string Name,
    string FullName,
    string FamilyName,
    string PublisherDisplayName,
    bool IsMicrosoft,
    bool IsFramework,
    bool IsResource,
    bool IsSystemSigned,
    string? InstallLocation);

public enum AppxRestoreOutcome
{
    Registered,
    Failed,
}

/// <summary>Zugriff auf PackageManager (Windows.Management.Deployment); Implementierung im Projekt AscendedTweaks.Appx.</summary>
public interface IAppxAccess
{
    /// <summary>Pakete des angemeldeten Benutzers (ohne Administratorrechte).</summary>
    IReadOnlyList<AppxPackageInfo> ListForCurrentUser();

    /// <summary>Pakete mit diesem Namen fuer alle Benutzer (Administratorrechte).</summary>
    IReadOnlyList<AppxPackageInfo> FindForAllUsers(string name);

    bool IsProvisioned(string familyName);

    void RemoveForAllUsers(string fullName);

    void Deprovision(string familyName);

    AppxRestoreOutcome RegisterByFamilyName(string familyName);

    AppxRestoreOutcome RegisterManifest(string manifestPath);
}

public sealed record DebloatApp(string Id, IReadOnlyList<string> PackageNames, AppGroup Group, RiskLevel Risk, int MinBuild = 19045);

/// <summary>Kandidaten DEB-001 bis DEB-026 und die Sperrliste (Abschnitt 6.6).</summary>
public static partial class DebloatCatalog
{
    public static readonly IReadOnlyList<DebloatApp> Apps =
    [
        new("DEB-001", ["Microsoft.BingNews"], AppGroup.News, RiskLevel.Safe),
        new("DEB-002", ["Microsoft.BingWeather"], AppGroup.News, RiskLevel.Safe),
        new("DEB-003", ["Microsoft.GetHelp"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-004", ["Microsoft.Getstarted"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-005", ["Microsoft.MicrosoftOfficeHub"], AppGroup.Office, RiskLevel.Safe),
        new("DEB-006", ["Microsoft.MicrosoftSolitaireCollection"], AppGroup.Games, RiskLevel.Safe),
        new("DEB-007", ["Microsoft.People"], AppGroup.Communication, RiskLevel.Safe),
        new("DEB-008", ["Microsoft.WindowsFeedbackHub"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-009", ["Microsoft.YourPhone"], AppGroup.Communication, RiskLevel.Caution),
        new("DEB-010", ["Microsoft.ZuneVideo"], AppGroup.Media, RiskLevel.Safe),
        new("DEB-011", ["Microsoft.ZuneMusic"], AppGroup.Media, RiskLevel.Caution),
        new("DEB-012", ["Microsoft.WindowsMaps"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-013", ["Microsoft.MixedReality.Portal"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-014", ["Microsoft.Microsoft3DViewer"], AppGroup.Media, RiskLevel.Safe),
        new("DEB-015", ["Microsoft.SkypeApp"], AppGroup.Communication, RiskLevel.Safe),
        new("DEB-016", ["Clipchamp.Clipchamp"], AppGroup.Media, RiskLevel.Safe),
        new("DEB-017", ["MicrosoftTeams", "MSTeams"], AppGroup.Communication, RiskLevel.Caution),
        new("DEB-018", ["Microsoft.Todos"], AppGroup.Office, RiskLevel.Safe),
        new("DEB-019", ["Microsoft.PowerAutomateDesktop"], AppGroup.DeveloperAi, RiskLevel.Safe),
        new("DEB-020", ["Microsoft.OutlookForWindows"], AppGroup.Office, RiskLevel.Caution),
        new("DEB-021", ["Microsoft.Copilot"], AppGroup.DeveloperAi, RiskLevel.Caution, 26100),
        new("DEB-022", ["Microsoft.GamingApp"], AppGroup.Games, RiskLevel.Caution),
        new("DEB-023", ["Microsoft.MicrosoftStickyNotes"], AppGroup.Office, RiskLevel.Caution),
        new("DEB-024", ["Microsoft.WindowsAlarms"], AppGroup.Other, RiskLevel.Safe),
        new("DEB-025", ["Microsoft.WindowsSoundRecorder"], AppGroup.Media, RiskLevel.Safe),
        new("DEB-026", ["Microsoft.Windows.DevHome"], AppGroup.DeveloperAi, RiskLevel.Safe),
    ];

    /// <summary>
    /// Systemapps: nie auswaehlbar (Abschnitt 6.6). Ergaenzt um die Einstellungen-App und den App-Installer (winget),
    /// deren Entfernung Windows-Funktionen beschaedigt.
    /// </summary>
    private static readonly string[] LockedPatterns =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.SecHealthUI", "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.Windows.Search", "MicrosoftWindows.Client.*", "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl", "Microsoft.UI.Xaml.*", "Microsoft.VCLibs.*", "Microsoft.NET.Native.*", "Microsoft.MicrosoftEdge*",
        "Microsoft.Win32WebViewHost", "Microsoft.WebView2*", "windows.immersivecontrolpanel", "Microsoft.DesktopAppInstaller",
        "Microsoft.WindowsAppRuntime.*", "MicrosoftCorporationII.WindowsAppRuntime.*",
    ];

    public static bool IsLocked(string packageName)
    {
        ArgumentNullException.ThrowIfNull(packageName);
        return LockedPatterns.Any(p => p.EndsWith('*')
            ? packageName.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(packageName, p, StringComparison.OrdinalIgnoreCase));
    }

    public static DebloatApp? Find(string packageName) =>
        Apps.FirstOrDefault(a => a.PackageNames.Contains(packageName, StringComparer.OrdinalIgnoreCase));

    public static bool IsValidPackageName(string name) => PackageNamePattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]{2,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNamePattern();
}

public sealed record AppRow(string PackageName, DebloatApp? Catalog, AppxPackageInfo? Installed, bool Locked);

/// <summary>Inventar fuer die Anzeige: Katalog-Apps (installiert oder nicht) und "Weitere Apps" anderer Herausgeber.</summary>
public static class AppInventory
{
    public static IReadOnlyList<AppRow> Build(IReadOnlyList<AppxPackageInfo> installed, int build)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var rows = new List<AppRow>();
        foreach (var app in DebloatCatalog.Apps.Where(a => build >= a.MinBuild))
        {
            var match = installed.FirstOrDefault(p => app.PackageNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            rows.Add(new AppRow(match?.Name ?? app.PackageNames[0], app, match, Locked: false));
        }

        // Weitere Apps: nicht von Microsoft, keine Frameworks/Ressourcen, nicht systemsigniert, nicht gesperrt (standardmaessig nicht ausgewaehlt).
        foreach (var package in installed
            .Where(p => !p.IsMicrosoft && !p.IsFramework && !p.IsResource && !p.IsSystemSigned && DebloatCatalog.Find(p.Name) is null)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()))
        {
            rows.Add(new AppRow(package.Name, null, package, DebloatCatalog.IsLocked(package.Name)));
        }

        // Gesperrte Systemapps sichtbar mit Schloss (nur die installierten, je Name einmal).
        foreach (var package in installed.Where(p => DebloatCatalog.IsLocked(p.Name) && !p.IsFramework && !p.IsResource).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            if (!rows.Any(r => string.Equals(r.PackageName, package.Name, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(new AppRow(package.Name, null, package, Locked: true));
            }
        }

        return rows;
    }
}

public sealed record RemovedApp
{
    public required string PackageName { get; init; }

    public required string FamilyName { get; init; }

    public required string FullName { get; init; }

    public string? InstallLocation { get; init; }

    public bool WasProvisioned { get; init; }

    public bool Deprovisioned { get; init; }

    public bool Ok { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset RemovedUtc { get; init; }

    /// <summary>Wiederhergestellt (neu registriert); null, solange nicht versucht.</summary>
    public bool? Restored { get; set; }
}

/// <summary>
/// Entfernen im Helfer: nur gueltige Paketnamen, nie gesperrte Systemapps, und "Weitere Apps" nur, wenn sie nicht von
/// Microsoft und nicht systemsigniert sind. Der Helfer liest die Pakete selbst; vom Client kommt nur der Name.
/// </summary>
public sealed class AppRemover
{
    private readonly IAppxAccess _appx;
    private readonly TimeProvider _time;

    public AppRemover(IAppxAccess appx, TimeProvider time)
    {
        _appx = appx;
        _time = time;
    }

    public IReadOnlyList<RemovedApp> Remove(IReadOnlyList<string> packageNames, bool deprovision)
    {
        ArgumentNullException.ThrowIfNull(packageNames);
        var results = new List<RemovedApp>();
        foreach (var name in packageNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!DebloatCatalog.IsValidPackageName(name) || DebloatCatalog.IsLocked(name))
            {
                throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"Package {name} must not be removed.");
            }

            var packages = _appx.FindForAllUsers(name).Where(p => !p.IsFramework && !p.IsResource).ToList();
            if (packages.Count == 0)
            {
                continue;
            }

            if (DebloatCatalog.Find(name) is null && packages.Any(p => p.IsMicrosoft || p.IsSystemSigned))
            {
                throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"{name} is not a catalog package and comes from Microsoft or is system-signed.");
            }

            foreach (var package in packages.GroupBy(p => p.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                var provisioned = SafeProvisioned(package.FamilyName);
                try
                {
                    _appx.RemoveForAllUsers(package.FullName);
                    var deprovisioned = false;
                    if (deprovision && provisioned)
                    {
                        _appx.Deprovision(package.FamilyName);
                        deprovisioned = true;
                    }

                    results.Add(Record(name, package, provisioned, deprovisioned, ok: true, error: null));
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or AscendedTweaksException)
                {
                    results.Add(Record(name, package, provisioned, false, ok: false, error: ex.Message));
                }
            }
        }

        return results;
    }

    private bool SafeProvisioned(string familyName)
    {
        try
        {
            return _appx.IsProvisioned(familyName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private RemovedApp Record(string name, AppxPackageInfo package, bool provisioned, bool deprovisioned, bool ok, string? error) => new()
    {
        PackageName = name,
        FamilyName = package.FamilyName,
        FullName = package.FullName,
        InstallLocation = package.InstallLocation,
        WasProvisioned = provisioned,
        Deprovisioned = deprovisioned,
        Ok = ok,
        Error = error,
        RemovedUtc = _time.GetUtcNow(),
    };
}

public enum RestoreOutcome
{
    Registered,
    StoreOpened,
    Failed,
}

/// <summary>
/// Wiederherstellung im Benutzerkontext (Abschnitt 6.6): (a) Neuregistrierung des noch vorhandenen Pakets,
/// (b) sonst Store-Seite oeffnen. Nicht jede Wiederherstellung ist garantiert; das sagt die Oberflaeche offen.
/// </summary>
public sealed class AppRestorer
{
    private readonly IAppxAccess _appx;
    private readonly Action<string> _openUri;
    private readonly Func<string, bool> _fileExists;

    public AppRestorer(IAppxAccess appx, Action<string> openUri, Func<string, bool> fileExists)
    {
        _appx = appx;
        _openUri = openUri;
        _fileExists = fileExists;
    }

    public static string StoreUri(string familyName) => $"ms-windows-store://pdp/?PFN={Uri.EscapeDataString(familyName)}";

    public RestoreOutcome Restore(RemovedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (Try(() => _appx.RegisterByFamilyName(app.FamilyName)))
        {
            return RestoreOutcome.Registered;
        }

        if (app.InstallLocation is { } folder && Path.Combine(folder, "AppxManifest.xml") is var manifest && _fileExists(manifest)
            && Try(() => _appx.RegisterManifest(manifest)))
        {
            return RestoreOutcome.Registered;
        }

        try
        {
            _openUri(StoreUri(app.FamilyName));
            return RestoreOutcome.StoreOpened;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return RestoreOutcome.Failed;
        }
    }

    private static bool Try(Func<AppxRestoreOutcome> action)
    {
        try
        {
            return action() == AppxRestoreOutcome.Registered;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Liste entfernter Apps im Benutzer-Zustandsordner (fuer die Wiederherstellungsseite).</summary>
public sealed class RemovedAppsStore
{
    private static readonly JsonSerializerOptions Options = new(CoreJson.Options) { WriteIndented = true };

    public RemovedAppsStore(string path) => FilePath = path;

    public string FilePath { get; }

    public List<RemovedApp> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<RemovedApp>>(File.ReadAllText(FilePath), Options) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<RemovedApp> apps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(apps.ToList(), Options));
    }
}
