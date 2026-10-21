using TweakPilot.Core;

namespace TweakPilot.Engine.Insights;

public enum SignatureStatus
{
    Valid,
    Unsigned,
    Invalid,
    FileMissing,
    Unknown,
}

public sealed record SignatureInfo(SignatureStatus Status, string? Publisher);

public interface ISignatureVerifier
{
    SignatureInfo Verify(string filePath);
}

public enum AutostartSource
{
    Run,
    RunOnce,
    StartupFolder,
    LogonTask,
}

/// <summary>Bewertung eines Autostart-Eintrags; die Begruendung steht im Textschluessel.</summary>
public enum AutostartRating
{
    /// <summary>Nicht aendern (Sicherheit, Treiber, Windows).</summary>
    Keep,

    /// <summary>Abschalten ist unkritisch, wenn das Programm nicht sofort nach der Anmeldung gebraucht wird.</summary>
    Optional,

    /// <summary>Herkunft unklar: nicht aendern, erst pruefen.</summary>
    Unknown,
}

public sealed record StartupFile(string FileName, string? TargetPath, string? Arguments, bool MachineWide);

public sealed record LogonTask(string TaskPath, string Command, bool Enabled);

public interface IAutostartSources
{
    IReadOnlyList<StartupFile> StartupFolderFiles();

    IReadOnlyList<LogonTask> LogonTasks();
}

public sealed record AutostartEntry
{
    public required string Name { get; init; }

    public required AutostartSource Source { get; init; }

    /// <summary>Ort fuer die Freigabe (StartupApproved); null bei RunOnce und Anmelde-Aufgaben (nur Anzeige).</summary>
    public AutostartLocation? Location { get; init; }

    public required bool MachineWide { get; init; }

    public required string Command { get; init; }

    public string? FilePath { get; init; }

    /// <summary>Freigabe laut StartupApproved oder Aufgabenzustand.</summary>
    public required bool Enabled { get; init; }

    public required SignatureInfo Signature { get; init; }

    public required AutostartRating Rating { get; init; }

    /// <summary>Textschluessel der Begruendung (autostart.reason.*).</summary>
    public required string ReasonKey { get; init; }

    /// <summary>ID des erzeugten Tweaks (PERF-026/...); null, wenn der Eintrag nicht umschaltbar ist.</summary>
    public string? TweakId { get; init; }

    /// <summary>Bekanntes Programm (AUTO-001 bis AUTO-010) mit Klartext-Beschreibung, erkannt per Datei und Signatur.</summary>
    public KnownStartupApp? Known { get; init; }

    /// <summary>Darf die App den Eintrag deaktivieren (Sicherheitsprogramme und unbekannte Eintraege nicht)?</summary>
    public bool CanDisable => TweakId is not null && Enabled && Rating != AutostartRating.Keep && Rating != AutostartRating.Unknown;
}

/// <summary>
/// Autostart-Uebersicht (Abschnitt 6.2): Run, RunOnce, Startordner, StartupApproved und Anmelde-Aufgaben.
/// Liest nur; Deaktivieren laeuft ueber den Tweak PERF-026 der Engine.
/// </summary>
public sealed class AutostartInventory
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string MachineRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineRun32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string MachineRunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    private static readonly string[] SecurityVendors =
    [
        "microsoft windows", "windows defender", "securityhealth", "bitdefender", "kaspersky", "norton", "nortonlifelock", "gen digital",
        "eset", "avast", "avg", "mcafee", "malwarebytes", "sophos", "trend micro", "f-secure", "g data", "avira", "webroot",
    ];

    private static readonly string[] DriverVendors =
    [
        "realtek", "nvidia", "advanced micro devices", "amd", "intel", "logitech", "synaptics", "elan", "dolby", "waves", "conexant",
        "razer", "corsair", "steelseries", "asus", "msi", "micro-star", "lenovo", "dell", "hp inc", "hewlett", "acer", "cirrus",
    ];

    private readonly IRegistryAccess _registry;
    private readonly IAutostartSources _sources;
    private readonly ISignatureVerifier _signatures;

    public AutostartInventory(IRegistryAccess registry, IAutostartSources sources, ISignatureVerifier signatures)
    {
        _registry = registry;
        _sources = sources;
        _signatures = signatures;
    }

    /// <summary>Pfad der ausfuehrbaren Datei aus einer Befehlszeile (Anfuehrungszeichen, sonst bis ".exe").</summary>
    public static string? ExtractPath(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            return text[..(exe + 4)];
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? text[..space] : text;
    }

    /// <summary>Freigabe aus StartupApproved: fehlt der Wert, ist der Eintrag aktiv; ungerades erstes Byte bedeutet deaktiviert.</summary>
    public static bool IsApproved(RegistryData? data)
    {
        var bytes = data?.GetBytes();
        return bytes is not { Length: > 0 } || (bytes[0] & 1) == 0;
    }

    public static (AutostartRating Rating, string ReasonKey) Rate(string name, string? filePath, SignatureInfo signature)
    {
        var publisher = signature.Publisher?.ToLowerInvariant() ?? string.Empty;
        var haystack = $"{name} {Path.GetFileName(filePath ?? string.Empty)}".ToLowerInvariant();

        if (signature.Status == SignatureStatus.FileMissing)
        {
            return (AutostartRating.Unknown, "autostart.reason.missing");
        }

        // Windows-Dateien sind meist ueber Kataloge signiert, die WinVerifyTrust fuer die Datei allein nicht sieht.
        // Dort schreiben nur Administratoren; die Eintraege gehoeren zu Windows und bleiben unangetastet.
        if (filePath is not null && IsInWindowsDirectory(filePath))
        {
            return (AutostartRating.Keep, "autostart.reason.windows");
        }

        if (signature.Status != SignatureStatus.Valid)
        {
            return (AutostartRating.Unknown, "autostart.reason.unsigned");
        }

        if (SecurityVendors.Any(v => publisher.Contains(v, StringComparison.Ordinal) && (v != "microsoft windows" || haystack.Contains("security", StringComparison.Ordinal)))
            || haystack.Contains("securityhealth", StringComparison.Ordinal))
        {
            return (AutostartRating.Keep, "autostart.reason.security");
        }

        if (DriverVendors.Any(v => publisher.Contains(v, StringComparison.Ordinal)))
        {
            return (AutostartRating.Keep, "autostart.reason.driver");
        }

        if (haystack.Contains("update", StringComparison.Ordinal))
        {
            return (AutostartRating.Optional, "autostart.reason.updater");
        }

        if (publisher.Contains("microsoft", StringComparison.Ordinal))
        {
            return (AutostartRating.Optional, "autostart.reason.microsoft");
        }

        return (AutostartRating.Optional, "autostart.reason.known");
    }

    private static bool IsInWindowsDirectory(string filePath)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windows))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(filePath));
            return full.StartsWith(windows.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public IReadOnlyList<AutostartEntry> Read()
    {
        var entries = new List<AutostartEntry>();
        AddRun(entries, RegHive.HKCU, RunPath, AutostartLocation.UserRun, AutostartSource.Run);
        AddRun(entries, RegHive.HKLM, MachineRunPath, AutostartLocation.MachineRun, AutostartSource.Run);
        AddRun(entries, RegHive.HKLM, MachineRun32Path, AutostartLocation.MachineRun32, AutostartSource.Run);
        AddRun(entries, RegHive.HKCU, RunOncePath, null, AutostartSource.RunOnce);
        AddRun(entries, RegHive.HKLM, MachineRunOncePath, null, AutostartSource.RunOnce);

        foreach (var file in _sources.StartupFolderFiles())
        {
            var location = file.MachineWide ? AutostartLocation.CommonStartupFolder : AutostartLocation.UserStartupFolder;
            var command = file.TargetPath is null ? file.FileName : $"\"{file.TargetPath}\" {file.Arguments}".TrimEnd();
            entries.Add(Create(file.FileName, AutostartSource.StartupFolder, location, file.MachineWide, command, file.TargetPath, Approved(location, file.FileName)));
        }

        foreach (var task in _sources.LogonTasks())
        {
            entries.Add(Create(task.TaskPath, AutostartSource.LogonTask, null, true, task.Command, ExtractPath(task.Command), task.Enabled));
        }

        return entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void AddRun(List<AutostartEntry> entries, RegHive hive, string path, AutostartLocation? location, AutostartSource source)
    {
        foreach (var name in _registry.GetValueNames(hive, path).Where(n => n.Length > 0))
        {
            var command = _registry.GetValue(hive, path, name)?.Text;
            if (command is null)
            {
                continue;
            }

            var enabled = location is not { } l || Approved(l, name);
            entries.Add(Create(name, source, location, hive == RegHive.HKLM, command, ExtractPath(command), enabled));
        }
    }

    private bool Approved(AutostartLocation location, string name)
    {
        var (hive, path) = AutostartTweaks.ApprovalKey(location);
        return IsApproved(_registry.GetValue(hive, path, name));
    }

    private AutostartEntry Create(string name, AutostartSource source, AutostartLocation? location, bool machineWide, string command, string? filePath, bool enabled)
    {
        var signature = filePath is null ? new SignatureInfo(SignatureStatus.Unknown, null) : _signatures.Verify(filePath);
        var (rating, reason) = Rate(name, filePath, signature);
        var known = KnownStartupApps.Match(filePath, command, signature);
        if (known is not null && rating == AutostartRating.Optional)
        {
            reason = $"autostart.known.{known.Id}";
        }

        return new AutostartEntry
        {
            Name = name,
            Source = source,
            Location = location,
            MachineWide = machineWide,
            Command = command,
            FilePath = filePath,
            Enabled = enabled,
            Signature = signature,
            Rating = rating,
            ReasonKey = reason,
            TweakId = location is { } l && AutostartTweaks.IsValidName(name) ? AutostartTweaks.IdFor(l, name) : null,
            Known = known,
        };
    }
}
