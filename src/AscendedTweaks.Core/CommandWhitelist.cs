namespace AscendedTweaks.Core;

/// <summary>
/// Ein erlaubter Befehl. Programme liegen immer im Systemverzeichnis, Argumente sind fest,
/// der einzige Platzhalter "{value}" wird nur durch einen Wert aus <see cref="AllowedValues"/> ersetzt.
/// </summary>
public sealed record CommandDefinition
{
    public const string ValuePlaceholder = "{value}";

    public required string Id { get; init; }

    /// <summary>Dateiname im Systemverzeichnis (System32), nie ein freier Pfad.</summary>
    public required string Executable { get; init; }

    public required IReadOnlyList<string> SetArguments { get; init; }

    public IReadOnlyList<string> QueryArguments { get; init; } = [];

    /// <summary>Regulaerer Ausdruck mit der benannten Gruppe "value" zum Lesen des Ist-Werts.</summary>
    public string QueryPattern { get; init; } = string.Empty;

    /// <summary>Statt eines Abfragebefehls wird der Ist-Wert ueber eine dokumentierte API gelesen (siehe <see cref="BuiltInQueries"/>).</summary>
    public string? BuiltInQuery { get; init; }

    /// <summary>Feste Werteliste; bei <see cref="ValueValidator"/> darf sie leer sein.</summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>Strenge Pruefung fuer strukturierte Werte (zum Beispiel DNS-Listen pro Adapter) statt einer festen Liste.</summary>
    public Func<string, bool>? ValueValidator { get; init; }

    /// <summary>Statt eines Prozesses setzt die Engine den Wert ueber eine dokumentierte API (siehe <see cref="BuiltInQueries"/>).</summary>
    public string? BuiltInSet { get; init; }

    /// <summary>Ist-Wert ist nur mit Administratorrechten lesbar (DISM, Energieverwaltung von Geraeten).</summary>
    public bool ReadRequiresAdmin { get; init; }

    /// <summary>Grund fuer "nicht verfuegbar", wenn der Ist-Wert fehlt (zum Beispiel: kein Adapter mit dieser Eigenschaft).</summary>
    public string? UnreadableReason { get; init; }

    public bool IsAllowed(string value) =>
        AllowedValues.Contains(value, StringComparer.Ordinal) || ValueValidator?.Invoke(value) == true;

    public IReadOnlyList<string> BuildSetArguments(string value)
    {
        if (!IsAllowed(value))
        {
            throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Value '{value}' is not allowed for command {Id}.");
        }

        // Der Platzhalter darf Teil eines Arguments sein (zum Beispiel "/State:{value}"); der Wert stammt nur aus der Whitelist.
        return SetArguments.Select(a => a.Replace(ValuePlaceholder, value, StringComparison.Ordinal)).ToList();
    }
}

public static class BuiltInQueries
{
    /// <summary>GetPwrCapabilities: Ruhezustand unterstuetzt und Ruhezustandsdatei vorhanden. Werte "on" oder "off".</summary>
    public const string Hibernation = "power.hibernation";

    /// <summary>DNS-Server aller physischen Adapter (AdapterState).</summary>
    public const string DnsServers = "network.dns";

    /// <summary>Autotuning-Stufe aus MSFT_NetTCPSetting.</summary>
    public const string Autotuning = "network.autotuning";

    /// <summary>Energiesparen der physischen Netzwerkadapter (AdapterState, "on"/"off").</summary>
    public const string AdapterPowerSaving = "network.powersaving";

    /// <summary>Energy-efficient ethernet (*EEE) der Adapter, die die Eigenschaft haben (AdapterState, "on"/"off").</summary>
    public const string AdapterEee = "network.eee";

    /// <summary>Defender ScanAvgCPULoadFactor (Prozent, 5 bis 100 oder 0) ueber MSFT_MpPreference.</summary>
    public const string DefenderScanLoad = "defender.scanload";
}

/// <summary>Fest eingebettete Befehls-Whitelist (Abschnitt 5.2, Aktionstyp Command).</summary>
public static class CommandWhitelist
{
    public static readonly IReadOnlyDictionary<string, CommandDefinition> All = new[]
    {
        new CommandDefinition
        {
            Id = "fsutil.disablelastaccess",
            Executable = "fsutil.exe",
            SetArguments = ["behavior", "set", "disablelastaccess", CommandDefinition.ValuePlaceholder],
            QueryArguments = ["behavior", "query", "disablelastaccess"],
            QueryPattern = @"DisableLastAccess\s*=\s*(?<value>[0-3])",
            AllowedValues = ["0", "1", "2", "3"],
        },
        new CommandDefinition
        {
            Id = "powercfg.hibernate",
            Executable = "powercfg.exe",
            SetArguments = ["/hibernate", CommandDefinition.ValuePlaceholder],
            BuiltInQuery = BuiltInQueries.Hibernation,
            AllowedValues = ["on", "off"],
        },
        new CommandDefinition
        {
            Id = "dns.servers",
            Executable = "netsh.exe",
            SetArguments = [],
            BuiltInQuery = BuiltInQueries.DnsServers,
            BuiltInSet = BuiltInQueries.DnsServers,
            ValueValidator = AdapterState.IsValidDnsState,
        },
        new CommandDefinition
        {
            Id = "tcp.autotuning",
            Executable = "netsh.exe",
            SetArguments = ["interface", "tcp", "set", "global", $"autotuninglevel={CommandDefinition.ValuePlaceholder}"],
            BuiltInQuery = BuiltInQueries.Autotuning,
            AllowedValues = ["normal", "disabled", "highlyrestricted", "restricted", "experimental"],
        },
        new CommandDefinition
        {
            Id = "nic.powersaving",
            Executable = "netsh.exe",
            SetArguments = [],
            BuiltInQuery = BuiltInQueries.AdapterPowerSaving,
            BuiltInSet = BuiltInQueries.AdapterPowerSaving,
            ValueValidator = AdapterState.IsValidPowerState,
            ReadRequiresAdmin = true,
        },
        new CommandDefinition
        {
            Id = "fsutil.disabledeletenotify",
            Executable = "fsutil.exe",
            SetArguments = ["behavior", "set", "DisableDeleteNotify", "NTFS", CommandDefinition.ValuePlaceholder],
            QueryArguments = ["behavior", "query", "DisableDeleteNotify"],
            QueryPattern = @"NTFS DisableDeleteNotify\s*=\s*(?<value>[01])",
            AllowedValues = ["0", "1"],
        },
        new CommandDefinition
        {
            // /English erzwingt eine sprachunabhaengige Ausgabe.
            Id = "dism.reservedstorage",
            Executable = "dism.exe",
            SetArguments = ["/Online", "/Set-ReservedStorageState", $"/State:{CommandDefinition.ValuePlaceholder}", "/English"],
            QueryArguments = ["/Online", "/Get-ReservedStorageState", "/English"],
            QueryPattern = @"Reserved storage is (?<value>enabled|disabled)",
            AllowedValues = ["enabled", "disabled"],
            ReadRequiresAdmin = true,
        },
        new CommandDefinition
        {
            Id = "nic.eee",
            Executable = "netsh.exe",
            SetArguments = [],
            BuiltInQuery = BuiltInQueries.AdapterEee,
            BuiltInSet = BuiltInQueries.AdapterEee,
            ValueValidator = AdapterState.IsValidPowerState,
            ReadRequiresAdmin = true,
            UnreadableReason = "No network adapter offers the \"Energy-efficient ethernet\" property.",
        },
        new CommandDefinition
        {
            // Nur die CPU-Last bei Scans; 50 ist der Windows-Standard, 0 schaltet die Begrenzung ab.
            Id = "defender.scanload",
            Executable = "powershell.exe",
            SetArguments = [],
            BuiltInQuery = BuiltInQueries.DefenderScanLoad,
            BuiltInSet = BuiltInQueries.DefenderScanLoad,
            ValueValidator = IsValidScanLoad,
            UnreadableReason = "Microsoft Defender is not the active antivirus (another antivirus is running), or a policy sets the scan load.",
        },
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);

    /// <summary>Gueltig nach Set-MpPreference: 0 (keine Begrenzung) oder 5 bis 100, nur Ziffern in Normalform.</summary>
    public static bool IsValidScanLoad(string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var percent)
        && value == percent.ToString(System.Globalization.CultureInfo.InvariantCulture)
        && (percent == 0 || percent is >= 5 and <= 100);
}
