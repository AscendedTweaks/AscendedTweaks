using System.Text.RegularExpressions;
using AscendedTweaks.Core;

namespace AscendedTweaks.Engine.Executors;

internal sealed class ServiceExecutor : ActionExecutor<ServiceSetStartTypeAction, ServiceCapture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IServiceAccess _services;

    public ServiceExecutor(IServiceAccess services) => _services = services;

    public static (ServiceStartType Type, bool Delayed) Map(ServiceStartSetting setting)
    {
        return setting switch
        {
            ServiceStartSetting.Automatic => (ServiceStartType.Automatic, false),
            ServiceStartSetting.AutomaticDelayed => (ServiceStartType.Automatic, true),
            ServiceStartSetting.Manual => (ServiceStartType.Manual, false),
            _ => (ServiceStartType.Disabled, false),
        };
    }

    public static string Display(ServiceStartType type, bool delayed) => delayed ? $"{type} (delayed)" : type.ToString();

    protected override string TargetKey(ServiceSetStartTypeAction action) => $"service:{action.Service}".ToUpperInvariant();

    protected override ServiceCapture Capture(ServiceSetStartTypeAction action)
    {
        var config = GetRequired(action.Service);
        return new ServiceCapture
        {
            Service = action.Service,
            BeforeStartType = config.StartType,
            BeforeDelayed = config.DelayedAutoStart,
            BeforeRunning = config.Running,
        };
    }

    protected override ServiceCapture Apply(ServiceSetStartTypeAction action, ServiceCapture before)
    {
        var (type, delayed) = Map(action.StartType);
        _services.SetStartType(action.Service, type, delayed);
        if (action.StopNow && type is ServiceStartType.Manual or ServiceStartType.Disabled && GetRequired(action.Service).Running)
        {
            _services.Stop(action.Service, Timeout);
        }

        return before with { Applied = true, WrittenStartType = type, WrittenDelayed = delayed };
    }

    protected override bool Verify(ServiceSetStartTypeAction action)
    {
        var (type, delayed) = Map(action.StartType);
        var config = _services.GetConfig(action.Service);
        return config is not null
            && config.StartType == type
            && config.DelayedAutoStart == delayed
            && !(action.StopNow && type is ServiceStartType.Manual or ServiceStartType.Disabled && config.Running);
    }

    protected override void Restore(ServiceCapture capture)
    {
        var config = GetRequired(capture.Service);
        if (config.StartType != capture.BeforeStartType || config.DelayedAutoStart != capture.BeforeDelayed)
        {
            _services.SetStartType(capture.Service, capture.BeforeStartType, capture.BeforeDelayed);
        }

        if (capture.BeforeRunning && !GetRequired(capture.Service).Running)
        {
            _services.Start(capture.Service, Timeout);
        }
    }

    protected override bool IsAtWrittenState(ServiceCapture capture)
    {
        var config = _services.GetConfig(capture.Service);
        return config is not null && config.StartType == capture.WrittenStartType && config.DelayedAutoStart == capture.WrittenDelayed;
    }

    protected override bool IsAtOriginalState(ServiceCapture capture)
    {
        var config = _services.GetConfig(capture.Service);
        return config is not null
            && config.StartType == capture.BeforeStartType
            && config.DelayedAutoStart == capture.BeforeDelayed
            && (!capture.BeforeRunning || config.Running);
    }

    protected override PlanItem Describe(string tweakId, ServiceSetStartTypeAction action)
    {
        var config = _services.GetConfig(action.Service);
        var (type, delayed) = Map(action.StartType);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = $"Service {action.Service}",
            ValueName = "Starttyp",
            Current = config is null ? "(not installed)" : Display(config.StartType, config.DelayedAutoStart) + (config.Running ? ", running" : ", stopped"),
            New = Display(type, delayed) + (action.StopNow ? ", will be stopped" : string.Empty),
        };
    }

    protected override CaptureView View(ServiceCapture capture)
    {
        var config = _services.GetConfig(capture.Service);
        return new CaptureView(
            $"Service {capture.Service}",
            config is null ? "(not installed)" : Display(config.StartType, config.DelayedAutoStart),
            Display(capture.BeforeStartType, capture.BeforeDelayed),
            Display(capture.WrittenStartType, capture.WrittenDelayed));
    }

    private ServiceConfig GetRequired(string service)
    {
        return _services.GetConfig(service)
            ?? throw new AscendedTweaksException(ErrorKind.Unavailable, $"Service {service} is not installed.");
    }
}

internal sealed class PowerPlanExecutor : ActionExecutor<PowerPlanSetAction, PowerPlanCapture>
{
    private readonly IPowerPlanAccess _power;

    public PowerPlanExecutor(IPowerPlanAccess power) => _power = power;

    protected override string TargetKey(PowerPlanSetAction action) => "power:active-scheme";

    protected override PowerPlanCapture Capture(PowerPlanSetAction action)
    {
        return new PowerPlanCapture { BeforeActive = _power.GetActive(), Target = action.SchemeGuid };
    }

    protected override PowerPlanCapture Apply(PowerPlanSetAction action, PowerPlanCapture before)
    {
        Guid? created = null;
        if (!_power.ListSchemes().Contains(action.SchemeGuid))
        {
            if (action.DuplicateFrom is not { } source)
            {
                throw new AscendedTweaksException(ErrorKind.Unavailable, $"Power plan {action.SchemeGuid} does not exist.");
            }

            _power.Duplicate(source, action.SchemeGuid);
            created = action.SchemeGuid;
        }

        _power.SetActive(action.SchemeGuid);
        return before with { Applied = true, CreatedScheme = created };
    }

    protected override bool Verify(PowerPlanSetAction action) => _power.GetActive() == action.SchemeGuid;

    protected override void Restore(PowerPlanCapture capture)
    {
        if (_power.GetActive() != capture.BeforeActive)
        {
            if (!_power.ListSchemes().Contains(capture.BeforeActive))
            {
                throw new AscendedTweaksException(ErrorKind.RollbackFailed, $"Original power plan {capture.BeforeActive} no longer exists.");
            }

            _power.SetActive(capture.BeforeActive);
        }

        if (capture.CreatedScheme is { } created && created != capture.BeforeActive && _power.ListSchemes().Contains(created))
        {
            _power.Delete(created);
        }
    }

    protected override bool IsAtWrittenState(PowerPlanCapture capture) => _power.GetActive() == capture.Target;

    protected override bool IsAtOriginalState(PowerPlanCapture capture)
    {
        return _power.GetActive() == capture.BeforeActive
            && (capture.CreatedScheme is not { } created || !_power.ListSchemes().Contains(created));
    }

    protected override PlanItem Describe(string tweakId, PowerPlanSetAction action)
    {
        var exists = _power.ListSchemes().Contains(action.SchemeGuid);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = "Active power plan",
            Current = _power.GetActive().ToString(),
            New = exists || action.DuplicateFrom is null
                ? action.SchemeGuid.ToString()
                : $"{action.SchemeGuid} (created from {action.DuplicateFrom})",
        };
    }

    protected override CaptureView View(PowerPlanCapture capture)
    {
        return new CaptureView("Active power plan", _power.GetActive().ToString(), capture.BeforeActive.ToString(), capture.Target.ToString());
    }
}

internal sealed class PowerSettingExecutor : ActionExecutor<PowerSettingSetAction, PowerSettingCapture>
{
    private readonly IPowerPlanAccess _power;

    public PowerSettingExecutor(IPowerPlanAccess power) => _power = power;

    public uint? ReadActive(Guid subgroup, Guid setting) => _power.ReadAcValue(_power.GetActive(), subgroup, setting);

    protected override string TargetKey(PowerSettingSetAction action) => $"power:{action.Subgroup:D}/{action.Setting:D}";

    protected override PowerSettingCapture Capture(PowerSettingSetAction action)
    {
        var scheme = _power.GetActive();
        var before = _power.ReadAcValue(scheme, action.Subgroup, action.Setting)
            ?? throw new AscendedTweaksException(ErrorKind.Unavailable, $"Power setting {action.Setting} does not exist on this device.");
        return new PowerSettingCapture { Scheme = scheme, Subgroup = action.Subgroup, Setting = action.Setting, Before = before };
    }

    protected override PowerSettingCapture Apply(PowerSettingSetAction action, PowerSettingCapture before)
    {
        _power.WriteAcValue(before.Scheme, action.Subgroup, action.Setting, action.Value);
        return before with { Applied = true, Written = action.Value };
    }

    protected override bool Verify(PowerSettingSetAction action) => ReadActive(action.Subgroup, action.Setting) == action.Value;

    /// <summary>Schreibt den Originalwert in den Plan, der beim Anwenden aktiv war, auch wenn inzwischen ein anderer aktiv ist.</summary>
    protected override void Restore(PowerSettingCapture capture)
    {
        if (!_power.ListSchemes().Contains(capture.Scheme))
        {
            throw new AscendedTweaksException(ErrorKind.RollbackFailed, $"Power plan {capture.Scheme} no longer exists.");
        }

        if (_power.ReadAcValue(capture.Scheme, capture.Subgroup, capture.Setting) != capture.Before)
        {
            _power.WriteAcValue(capture.Scheme, capture.Subgroup, capture.Setting, capture.Before);
        }
    }

    protected override bool IsAtWrittenState(PowerSettingCapture capture) =>
        _power.ReadAcValue(capture.Scheme, capture.Subgroup, capture.Setting) == capture.Written;

    protected override bool IsAtOriginalState(PowerSettingCapture capture) =>
        !_power.ListSchemes().Contains(capture.Scheme) || _power.ReadAcValue(capture.Scheme, capture.Subgroup, capture.Setting) == capture.Before;

    protected override PlanItem Describe(string tweakId, PowerSettingSetAction action)
    {
        var current = ReadActive(action.Subgroup, action.Setting);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = $"Active power plan, plugged in: {action.Subgroup:D} / {action.Setting:D}",
            Current = current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(not present)",
            New = action.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    protected override CaptureView View(PowerSettingCapture capture)
    {
        var current = _power.ListSchemes().Contains(capture.Scheme) ? _power.ReadAcValue(capture.Scheme, capture.Subgroup, capture.Setting) : null;
        return new CaptureView(
            $"Power plan {capture.Scheme:D}: {capture.Setting:D}",
            current?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? EngineText.Missing,
            capture.Before.ToString(System.Globalization.CultureInfo.InvariantCulture),
            capture.Written.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed class CommandExecutor : ActionExecutor<CommandAction, CommandCapture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ICommandRunner _runner;
    private readonly string _systemDirectory;
    private readonly IPowerPlanAccess _power;
    private readonly INetworkAccess? _network;
    private readonly IDefenderAccess? _defender;

    public CommandExecutor(ICommandRunner runner, string systemDirectory, IPowerPlanAccess power, INetworkAccess? network = null, IDefenderAccess? defender = null)
    {
        _runner = runner;
        _systemDirectory = systemDirectory;
        _power = power;
        _network = network;
        _defender = defender;
    }

    private INetworkAccess Network => _network ?? throw new AscendedTweaksException(ErrorKind.Unavailable, "Network access is not available in this process.");

    private IDefenderAccess Defender => _defender ?? throw new AscendedTweaksException(ErrorKind.Unavailable, "Defender access is not available in this process.");

    public static CommandDefinition Definition(string id)
    {
        return CommandWhitelist.All.TryGetValue(id, out var definition)
            ? definition
            : throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Command {id} is not on the allowlist.");
    }

    /// <summary>Liest den Ist-Wert ueber den Abfragebefehl; null, wenn die Ausgabe nicht auswertbar ist.</summary>
    public string? Query(string commandId)
    {
        var definition = Definition(commandId);
        if (definition.BuiltInQuery is { } builtIn)
        {
            return builtIn switch
            {
                BuiltInQueries.Hibernation => _power.IsHibernationEnabled() switch
                {
                    true => "on",
                    false => "off",
                    null => null,
                },
                BuiltInQueries.DnsServers => QueryAdapters(a => Network.GetDnsServers(a) is { Count: > 0 } servers ? string.Join(',', servers) : AdapterState.Dhcp),
                BuiltInQueries.AdapterPowerSaving => QueryAdapters(a => Network.GetPowerSaving(a) switch { true => "on", false => "off", null => null }),
                BuiltInQueries.Autotuning => Network.GetAutotuning(),
                BuiltInQueries.AdapterEee => QueryEee(),
                BuiltInQueries.DefenderScanLoad => _defender?.GetScanAvgCpuLoad()?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Unknown query {builtIn}."),
            };
        }

        var result = _runner.Run(ExecutablePath(definition), definition.QueryArguments, Timeout);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var match = Regex.Match(result.StandardOutput, definition.QueryPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups["value"].Value : null;
    }

    protected override string TargetKey(CommandAction action) => $"command:{action.Command}".ToUpperInvariant();

    protected override CommandCapture Capture(CommandAction action)
    {
        var current = Query(action.Command)
            ?? throw new AscendedTweaksException(ErrorKind.Unavailable, $"Current value for {action.Command} is not readable.");
        return new CommandCapture { Command = action.Command, BeforeValue = current };
    }

    protected override CommandCapture Apply(CommandAction action, CommandCapture before)
    {
        Set(action.Command, action.Value);
        return before with { Applied = true, WrittenValue = Expected(action.Command, action.Value) };
    }

    protected override bool Verify(CommandAction action) => string.Equals(Query(action.Command), Expected(action.Command, action.Value), StringComparison.Ordinal);

    /// <summary>The value Query returns after writing <paramref name="value"/>; differs only for DNS on adapters without IPv6 or IPv4.</summary>
    public string Expected(string commandId, string value) =>
        Definition(commandId).BuiltInQuery == BuiltInQueries.DnsServers && AdapterState.IsValidDnsState(value)
            ? AdapterState.ReachableDns(value, Network.PhysicalAdapters(), Network.SupportsFamily)
            : value;

    protected override void Restore(CommandCapture capture)
    {
        if (!string.Equals(Query(capture.Command), capture.BeforeValue, StringComparison.Ordinal))
        {
            Set(capture.Command, capture.BeforeValue);
        }
    }

    protected override bool IsAtWrittenState(CommandCapture capture) => string.Equals(Query(capture.Command), capture.WrittenValue, StringComparison.Ordinal);

    protected override bool IsAtOriginalState(CommandCapture capture) => string.Equals(Query(capture.Command), capture.BeforeValue, StringComparison.Ordinal);

    protected override PlanItem Describe(string tweakId, CommandAction action)
    {
        var definition = Definition(action.Command);
        return new PlanItem
        {
            TweakId = tweakId,
            ActionType = NameOf(action),
            Target = $"{definition.Executable} {string.Join(' ', definition.BuildSetArguments(action.Value))}",
            Current = Query(action.Command) ?? "(not readable)",
            New = action.Value,
        };
    }

    protected override CaptureView View(CommandCapture capture)
    {
        return new CaptureView(capture.Command, Query(capture.Command) ?? "(not readable)", capture.BeforeValue, capture.WrittenValue ?? EngineText.Missing);
    }

    /// <summary>Zustand ueber alle physischen Adapter; null, wenn es keinen gibt oder ein Wert nicht lesbar ist.</summary>
    private string? QueryAdapters(Func<string, string?> read)
    {
        var adapters = Network.PhysicalAdapters();
        if (adapters.Count == 0)
        {
            return null;
        }

        var values = adapters.ToDictionary(a => a, read, StringComparer.OrdinalIgnoreCase);
        return values.Values.Any(v => v is null) ? null : AdapterState.Combine(values!);
    }

    /// <summary>Adapter mit der Eigenschaft *EEE; Adapter ohne sie zaehlen nicht (V2: "nur wenn die Eigenschaft beim Treiber existiert").</summary>
    private List<string> EeeAdapters() => Network.PhysicalAdapters().Where(a => Network.GetEnergyEfficientEthernet(a) is not null).ToList();

    private string? QueryEee()
    {
        var adapters = EeeAdapters();
        return adapters.Count == 0
            ? null
            : AdapterState.Combine(adapters.ToDictionary(a => a, a => Network.GetEnergyEfficientEthernet(a)!, StringComparer.OrdinalIgnoreCase));
    }

    private void SetAdapters(string value, Action<string, string> write)
    {
        foreach (var (adapter, adapterValue) in AdapterState.Split(value, Network.PhysicalAdapters()))
        {
            // Nur Adapter, die es noch gibt; ein entfernter Adapter kann nicht zurueckgesetzt werden.
            if (Network.PhysicalAdapters().Contains(adapter, StringComparer.OrdinalIgnoreCase))
            {
                write(adapter, adapterValue);
            }
        }
    }

    private void Set(string commandId, string value)
    {
        var definition = Definition(commandId);
        if (definition.BuiltInSet is { } builtIn)
        {
            if (!definition.IsAllowed(value))
            {
                throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Value '{value}' is not allowed for command {commandId}.");
            }

            switch (builtIn)
            {
                case BuiltInQueries.DnsServers:
                    SetAdapters(value, (a, v) => Network.SetDnsServers(a, v == AdapterState.Dhcp ? [] : v.Split(',')));
                    return;
                case BuiltInQueries.AdapterPowerSaving:
                    SetAdapters(value, (a, v) => Network.SetPowerSaving(a, v == "on"));
                    return;
                case BuiltInQueries.AdapterEee:
                    foreach (var (adapter, adapterValue) in AdapterState.Split(value, EeeAdapters()))
                    {
                        // Nur Adapter, die die Eigenschaft noch haben; ein entfernter Adapter kann nicht zurueckgesetzt werden.
                        if (Network.GetEnergyEfficientEthernet(adapter) is not null)
                        {
                            Network.SetEnergyEfficientEthernet(adapter, adapterValue == "on");
                        }
                    }

                    return;
                case BuiltInQueries.DefenderScanLoad:
                    Defender.SetScanAvgCpuLoad(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new AscendedTweaksException(ErrorKind.CatalogInvalid, $"Unknown implementation {builtIn}.");
            }
        }

        var result = _runner.Run(ExecutablePath(definition), definition.BuildSetArguments(value), Timeout);
        if (result.ExitCode != 0)
        {
            throw new AscendedTweaksException(
                result.ExitCode == 5 ? ErrorKind.PermissionDenied : ErrorKind.General,
                $"{definition.Executable} exited with code {result.ExitCode}: {result.StandardError.Trim()}{result.StandardOutput.Trim()}");
        }
    }

    private string ExecutablePath(CommandDefinition definition) => Path.Combine(_systemDirectory, definition.Executable);
}
