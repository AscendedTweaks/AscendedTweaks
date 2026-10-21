namespace TweakPilot.Engine.Insights;

/// <summary>
/// Bekanntes Programm im Autostart (V2 Abschnitt 5.2, AUTO-001 bis AUTO-010). Erkannt wird per Dateiname und
/// Herausgeber aus der gueltigen Signatur, nie per Eintragsname. Squirrel-Programme (Discord, klassisches Teams)
/// starten ueber "Update.exe --processStart Programm.exe"; dann zaehlt das Programm hinter --processStart.
/// </summary>
public sealed record KnownStartupApp(string Id, IReadOnlyList<string> FileNames, string Publisher, string ProcessName);

public static class KnownStartupApps
{
    public static readonly IReadOnlyList<KnownStartupApp> All =
    [
        new("AUTO-001", ["steam.exe"], "valve", "steam"),
        new("AUTO-002", ["EpicGamesLauncher.exe"], "epic games", "EpicGamesLauncher"),
        new("AUTO-003", ["Discord.exe"], "discord", "Discord"),
        new("AUTO-004", ["Battle.net Launcher.exe", "Battle.net.exe"], "blizzard", "Battle.net"),
        new("AUTO-005", ["EALauncher.exe", "EADesktop.exe"], "electronic arts", "EADesktop"),
        new("AUTO-006", ["UbisoftConnect.exe", "upc.exe"], "ubisoft", "UbisoftConnect"),
        new("AUTO-007", ["Spotify.exe"], "spotify", "Spotify"),
        new("AUTO-008", ["Teams.exe", "ms-teams.exe"], "microsoft", "ms-teams"),
        new("AUTO-009", ["OneDrive.exe"], "microsoft", "OneDrive"),
        new("AUTO-010", ["AdobeARM.exe"], "adobe", "AdobeARM"),
    ];

    /// <summary>Bekanntes Programm oder null. Ohne gueltige Signatur wird nie etwas erkannt.</summary>
    public static KnownStartupApp? Match(string? filePath, string command, SignatureInfo signature)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Status != SignatureStatus.Valid || signature.Publisher is not { } publisher || filePath is null)
        {
            return null;
        }

        var file = Path.GetFileName(filePath);
        var started = ProcessStartTarget(file, command);
        return All.FirstOrDefault(app =>
            publisher.Contains(app.Publisher, StringComparison.OrdinalIgnoreCase)
            && app.FileNames.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase) || string.Equals(f, started, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Programm hinter "Update.exe --processStart X.exe" oder null.</summary>
    public static string? ProcessStartTarget(string file, string command)
    {
        if (!string.Equals(file, "Update.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = Array.FindIndex(parts, p => string.Equals(p, "--processStart", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < parts.Length ? parts[index + 1].Trim('"') : null;
    }

    /// <summary>Arbeitssatz aller laufenden Prozesse des Programms; null, wenn es gerade nicht laeuft (dann keine Zahl, V2 5.2).</summary>
    public static long? RunningMemory(KnownStartupApp app, IReadOnlyList<ProcessSample> processes)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(processes);
        var names = app.FileNames.Select(Path.GetFileNameWithoutExtension).Append(app.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = processes.Where(p => names.Contains(p.Name)).ToList();
        return mine.Count == 0 ? null : mine.Sum(p => p.WorkingSetBytes);
    }
}

/// <summary>
/// Kleine eingebettete Liste bekannter Windows-Prozesse fuer den Hintergrund-Bericht (V2 Abschnitt 5.2). Unbekannte
/// Prozesse heissen "unbekannt, nicht anfassen"; es gibt weder "Prozess beenden" noch "RAM bereinigen".
/// </summary>
public static class KnownProcesses
{
    /// <summary>Prozessname (ohne .exe, Kleinschreibung) zu Textschluessel "process.known.&lt;Schluessel&gt;".</summary>
    public static readonly IReadOnlyDictionary<string, string> Windows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["system"] = "kernel",
        ["registry"] = "kernel",
        ["memory compression"] = "memorycompression",
        ["smss"] = "session",
        ["csrss"] = "session",
        ["wininit"] = "session",
        ["winlogon"] = "session",
        ["services"] = "services",
        ["svchost"] = "svchost",
        ["lsass"] = "lsass",
        ["explorer"] = "explorer",
        ["dwm"] = "dwm",
        ["searchhost"] = "search",
        ["searchindexer"] = "search",
        ["startmenuexperiencehost"] = "shell",
        ["shellexperiencehost"] = "shell",
        ["textinputhost"] = "shell",
        ["sihost"] = "shell",
        ["taskhostw"] = "shell",
        ["ctfmon"] = "shell",
        ["runtimebroker"] = "runtimebroker",
        ["msmpeng"] = "defender",
        ["nissrv"] = "defender",
        ["securityhealthservice"] = "defender",
        ["audiodg"] = "audio",
        ["fontdrvhost"] = "font",
        ["spoolsv"] = "spooler",
        ["wmiprvse"] = "wmi",
        ["dllhost"] = "com",
        ["conhost"] = "console",
        ["msedgewebview2"] = "webview",
    };

    /// <summary>Textschluessel fuer einen Prozess: Windows-Prozess, bekanntes Programm oder "unbekannt".</summary>
    public static string KeyFor(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName);
        if (Windows.TryGetValue(processName, out var key))
        {
            return "process.known." + key;
        }

        var app = KnownStartupApps.All.FirstOrDefault(a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
        return app is null ? "process.unknown" : $"autostart.known.{app.Id}.short";
    }
}
