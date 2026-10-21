namespace TweakPilot.Core;

/// <summary>Zugriff auf die Registry (64-Bit-Ansicht). Ein leerer Name bezeichnet den Standardwert.</summary>
public interface IRegistryAccess
{
    /// <summary>Liefert null, wenn Schluessel oder Wert fehlt.</summary>
    RegistryData? GetValue(RegHive hive, string path, string name);

    bool KeyExists(RegHive hive, string path);

    IReadOnlyList<string> GetValueNames(RegHive hive, string path);

    IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path);

    /// <summary>Schreibt den Wert, fehlende Schluessel werden angelegt.</summary>
    void SetValue(RegHive hive, string path, string name, RegistryData data);

    void DeleteValue(RegHive hive, string path, string name);

    void CreateKey(RegHive hive, string path);

    /// <summary>Loescht einen leeren Schluessel ohne Unterschluessel.</summary>
    void DeleteEmptyKey(RegHive hive, string path);

    void DeleteKeyTree(RegHive hive, string path);
}

public sealed record ServiceConfig(ServiceStartType StartType, bool DelayedAutoStart, bool Running);

public interface IServiceAccess
{
    /// <summary>Liefert null, wenn der Dienst nicht installiert ist.</summary>
    ServiceConfig? GetConfig(string serviceName);

    void SetStartType(string serviceName, ServiceStartType startType, bool delayedAutoStart);

    void Stop(string serviceName, TimeSpan timeout);

    void Start(string serviceName, TimeSpan timeout);
}

public interface IPowerPlanAccess
{
    Guid GetActive();

    IReadOnlyList<Guid> ListSchemes();

    void SetActive(Guid scheme);

    void Duplicate(Guid source, Guid destination);

    void Delete(Guid scheme);

    /// <summary>Ruhezustand aktiv (Datei vorhanden); null, wenn das Geraet keinen Ruhezustand unterstuetzt.</summary>
    bool? IsHibernationEnabled();

    /// <summary>Wert einer Unter-Einstellung bei Netzbetrieb (PowerReadACValueIndex); null, wenn es sie auf dem Geraet nicht gibt.</summary>
    uint? ReadAcValue(Guid scheme, Guid subgroup, Guid setting);

    /// <summary>Schreibt den Wert (PowerWriteACValueIndex); ist der Plan aktiv, wird er neu aktiviert, damit der Wert gilt.</summary>
    void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value);
}

/// <summary>
/// Microsoft Defender Antivirus, nur die eine Einstellung "CPU-Last bei Scans" (DEFN-001, V2 Abschnitt 5.2).
/// Keine Schutzfunktion wird gelesen oder geaendert.
/// </summary>
public interface IDefenderAccess
{
    /// <summary>ScanAvgCPULoadFactor aus MSFT_MpPreference; null, wenn nicht lesbar oder per Richtlinie festgelegt.</summary>
    int? GetScanAvgCpuLoad();

    void SetScanAvgCpuLoad(int percent);
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ICommandRunner
{
    /// <summary>Startet ein Programm ohne Shell, Argumente werden einzeln uebergeben.</summary>
    CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout);
}

public interface ISystemEnvironment
{
    int Build { get; }

    int Ubr { get; }

    /// <summary>Null, wenn die Edition nicht zu den unterstuetzten gehoert.</summary>
    WindowsEdition? Edition { get; }

    bool IsElevated { get; }

    string SystemDirectory { get; }

    /// <summary>Geraet hat einen Akku (Notebook-Sperre fuer Energie-Tweaks mit "desktopOnly").</summary>
    bool HasBattery => false;
}

public interface ITweakCatalog
{
    IReadOnlyList<TweakDefinition> Tweaks { get; }

    bool TryGet(string id, out TweakDefinition tweak);
}
