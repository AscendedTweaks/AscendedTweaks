using System.Text.RegularExpressions;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Tests;

/// <summary>In-Memory-Registry mit Schreibzaehler und Fehlerinjektion.</summary>
internal sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, RegistryData>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCount { get; private set; }

    public List<string> WriteLog { get; } = [];

    /// <summary>Wirft PermissionDenied bei Schreibzugriffen auf passende Pfade.</summary>
    public Func<string, bool>? FailWrite { get; set; }

    /// <summary>Simuliert einen Schutztreiber: Schreibzugriffe werden still ignoriert.</summary>
    public Func<string, bool>? IgnoreWrite { get; set; }

    public void Seed(RegHive hive, string path, string name, RegistryData data)
    {
        foreach (var key in RegistryPaths.Chain(path).Where(k => !IsAliasRoot(hive, k)))
        {
            _keys.TryAdd(Key(hive, key), new Dictionary<string, RegistryData>(StringComparer.OrdinalIgnoreCase));
        }

        _keys[Key(hive, path)][name] = data;
    }

    public void SeedKey(RegHive hive, string path)
    {
        foreach (var key in RegistryPaths.Chain(path).Where(k => !IsAliasRoot(hive, k)))
        {
            _keys.TryAdd(Key(hive, key), new Dictionary<string, RegistryData>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Normalisierter Gesamtzustand fuer Vorher/Nachher-Vergleiche.</summary>
    public string Snapshot()
    {
        return string.Join('\n', _keys
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(k => new[] { "K " + k.Key.ToUpperInvariant() }
                .Concat(k.Value.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(v => $"V {k.Key.ToUpperInvariant()}|{v.Key.ToUpperInvariant()}={v.Value.Display()}"))));
    }

    public RegistryData? GetValue(RegHive hive, string path, string name)
    {
        return _keys.TryGetValue(Key(hive, path), out var values) && values.TryGetValue(name, out var data) ? data : null;
    }

    public bool KeyExists(RegHive hive, string path) => _keys.ContainsKey(Key(hive, path));

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path)
    {
        return _keys.TryGetValue(Key(hive, path), out var values) ? values.Keys.ToList() : [];
    }

    public IReadOnlyList<string> GetSubKeyNames(RegHive hive, string path)
    {
        var prefix = Key(hive, path) + "\\";
        return _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !k[prefix.Length..].Contains('\\', StringComparison.Ordinal))
            .Select(k => k[prefix.Length..])
            .ToList();
    }

    public void SetValue(RegHive hive, string path, string name, RegistryData data)
    {
        if (BeginWrite(hive, path, $"set {path}|{name}"))
        {
            Seed(hive, path, name, data);
        }
    }

    public void DeleteValue(RegHive hive, string path, string name)
    {
        if (BeginWrite(hive, path, $"delete {path}|{name}") && _keys.TryGetValue(Key(hive, path), out var values))
        {
            values.Remove(name);
        }
    }

    public void CreateKey(RegHive hive, string path)
    {
        if (BeginWrite(hive, path, $"create {path}"))
        {
            SeedKey(hive, path);
        }
    }

    public void DeleteEmptyKey(RegHive hive, string path)
    {
        if (BeginWrite(hive, path, $"delete-key {path}"))
        {
            if (GetSubKeyNames(hive, path).Count > 0)
            {
                throw new InvalidOperationException("Schluessel hat Unterschluessel.");
            }

            _keys.Remove(Key(hive, path));
        }
    }

    public void DeleteKeyTree(RegHive hive, string path)
    {
        if (BeginWrite(hive, path, $"delete-tree {path}"))
        {
            var key = Key(hive, path);
            foreach (var k in _keys.Keys.Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) || k.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _keys.Remove(k);
            }
        }
    }

    private bool BeginWrite(RegHive hive, string path, string description)
    {
        WriteCount++;
        WriteLog.Add(description);
        if (FailWrite?.Invoke(path) == true)
        {
            throw new AscendedTweaksException(ErrorKind.PermissionDenied, $"Zugriff auf {path} verweigert (Test).");
        }

        return IgnoreWrite?.Invoke(path) != true;
    }

    /// <summary>Wie Windows: HKEY_USERS\SID ist dieselbe Hive wie HKCU des angemeldeten Benutzers.</summary>
    public string? HkuAliasSid { get; set; }

    private bool IsAliasRoot(RegHive hive, string path) => hive == RegHive.HKU && string.Equals(path, HkuAliasSid, StringComparison.OrdinalIgnoreCase);

    private string Key(RegHive hive, string path)
    {
        if (hive == RegHive.HKU && HkuAliasSid is not null && path.StartsWith(HkuAliasSid + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return $"{RegHive.HKCU}\\{path[(HkuAliasSid.Length + 1)..]}";
        }

        return $"{hive}\\{path}";
    }
}

internal sealed class FakeServices : IServiceAccess
{
    public Dictionary<string, ServiceConfig> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCount { get; private set; }

    public bool FailStart { get; set; }

    public ServiceConfig? GetConfig(string serviceName) => Services.TryGetValue(serviceName, out var config) ? config : null;

    public void SetStartType(string serviceName, ServiceStartType startType, bool delayedAutoStart)
    {
        WriteCount++;
        Services[serviceName] = Services[serviceName] with { StartType = startType, DelayedAutoStart = delayedAutoStart };
    }

    public void Stop(string serviceName, TimeSpan timeout)
    {
        WriteCount++;
        Services[serviceName] = Services[serviceName] with { Running = false };
    }

    public void Start(string serviceName, TimeSpan timeout)
    {
        WriteCount++;
        if (FailStart)
        {
            throw new InvalidOperationException("Dienst startet nicht (Test).");
        }

        Services[serviceName] = Services[serviceName] with { Running = true };
    }
}

internal sealed class FakePower : IPowerPlanAccess
{
    public List<Guid> Schemes { get; } = [];

    public Guid Active { get; set; }

    public int WriteCount { get; private set; }

    public Guid GetActive() => Active;

    public IReadOnlyList<Guid> ListSchemes() => Schemes.ToList();

    public void SetActive(Guid scheme)
    {
        WriteCount++;
        if (!Schemes.Contains(scheme))
        {
            throw new AscendedTweaksException(ErrorKind.General, "Plan fehlt (Test).");
        }

        Active = scheme;
    }

    public void Duplicate(Guid source, Guid destination)
    {
        WriteCount++;
        Schemes.Add(destination);
    }

    public void Delete(Guid scheme)
    {
        WriteCount++;
        Schemes.Remove(scheme);
    }

    /// <summary>Null: Geraet ohne Ruhezustand.</summary>
    public bool? Hibernation { get; set; } = true;

    public bool? IsHibernationEnabled() => Hibernation;

    /// <summary>Unter-Einstellungen je Plan (Netzbetrieb); fehlende Eintraege gibt es "auf dem Geraet" nicht.</summary>
    public Dictionary<(Guid Scheme, Guid Subgroup, Guid Setting), uint> AcValues { get; } = [];

    public int Reactivations { get; private set; }

    public uint? ReadAcValue(Guid scheme, Guid subgroup, Guid setting) =>
        AcValues.TryGetValue((scheme, subgroup, setting), out var value) ? value : null;

    public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        WriteCount++;
        AcValues[(scheme, subgroup, setting)] = value;
        if (scheme == Active)
        {
            Reactivations++;
        }
    }
}

internal sealed class FakeDefender : IDefenderAccess
{
    public int? Load { get; set; } = 50;

    public int? GetScanAvgCpuLoad() => Load;

    public void SetScanAvgCpuLoad(int percent) => Load = percent;
}

internal sealed class FakeTasks : IScheduledTaskAccess
{
    public Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCount { get; private set; }

    public bool FailWrite { get; set; }

    public bool? GetEnabled(string taskPath) => Tasks.TryGetValue(taskPath, out var enabled) ? enabled : null;

    public void SetEnabled(string taskPath, bool enabled)
    {
        WriteCount++;
        if (FailWrite || !Tasks.ContainsKey(taskPath))
        {
            throw new AscendedTweaksException(ErrorKind.General, "schtasks fehlgeschlagen (Test).");
        }

        Tasks[taskPath] = enabled;
    }
}

internal sealed class FakeParameters : ISystemParametersAccess
{
    public Dictionary<string, uint> Values { get; } = new(StringComparer.Ordinal)
    {
        ["ClientAreaAnimation"] = 1,
        ["MinimizeMaximizeAnimation"] = 1,
        ["MenuShowDelay"] = 400,
        ["MouseAcceleration"] = 6 | 10 << 8 | 1 << 16,
        ["StickyKeysHotkey"] = 1,
        ["FilterKeysHotkey"] = 1,
        ["ToggleKeysHotkey"] = 1,
    };

    public int WriteCount { get; private set; }

    public uint Get(string parameter) => Values[parameter];

    public void Set(string parameter, uint value)
    {
        WriteCount++;
        Values[parameter] = value;
    }
}

/// <summary>Simuliert fsutil behavior disablelastaccess.</summary>
internal sealed class FakeCommands : ICommandRunner
{
    public string LastAccessValue { get; set; } = "2";

    public int WriteCount { get; private set; }

    public bool BrokenOutput { get; set; }

    public List<(string Exe, IReadOnlyList<string> Args)> Calls { get; } = [];

    /// <summary>Wirkung von "powercfg /hibernate on|off" auf die Fake-Energieverwaltung.</summary>
    public Action<bool>? OnHibernate { get; set; }

    public CommandResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        Calls.Add((executable, arguments));
        if (arguments.SequenceEqual(["behavior", "query", "disablelastaccess"]))
        {
            return BrokenOutput
                ? new CommandResult(0, "unerwartete Ausgabe", string.Empty)
                : new CommandResult(0, $"DisableLastAccess = {LastAccessValue}  (Test)", string.Empty);
        }

        if (executable.EndsWith("powercfg.exe", StringComparison.OrdinalIgnoreCase) && arguments.Count == 2 && arguments[0] == "/hibernate")
        {
            WriteCount++;
            OnHibernate?.Invoke(arguments[1] == "on");
            return new CommandResult(0, string.Empty, string.Empty);
        }

        if (arguments.Count == 4 && arguments[1] == "set" && Regex.IsMatch(arguments[3], "^[0-3]$"))
        {
            WriteCount++;
            LastAccessValue = arguments[3];
            return new CommandResult(0, string.Empty, string.Empty);
        }

        return new CommandResult(1, string.Empty, "unbekannt");
    }
}

internal sealed class FakeEnvironment : ISystemEnvironment
{
    public int Build { get; set; } = 26100;

    public int Ubr { get; set; } = 1;

    public WindowsEdition? Edition { get; set; } = WindowsEdition.Pro;

    public bool IsElevated { get; set; } = true;

    public string SystemDirectory => @"C:\Windows\System32";

    public bool HasBattery { get; set; }
}

internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        _now = _now.AddSeconds(1);
        return _now;
    }
}

internal sealed class FakeRestorePoints : IRestorePointService
{
    public RestorePointStatus Status { get; set; } = RestorePointStatus.Created;

    public int Calls { get; private set; }

    public RestorePointResult TryCreate(string description)
    {
        Calls++;
        return new RestorePointResult(Status, description);
    }
}

internal sealed class ListCatalog : ITweakCatalog
{
    public ListCatalog(params TweakDefinition[] tweaks) => Tweaks = tweaks;

    public IReadOnlyList<TweakDefinition> Tweaks { get; }

    public bool TryGet(string id, out TweakDefinition tweak)
    {
        tweak = Tweaks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return tweak is not null;
    }
}
