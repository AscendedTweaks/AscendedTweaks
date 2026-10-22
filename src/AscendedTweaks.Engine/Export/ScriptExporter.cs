using System.Globalization;
using System.Text;
using AscendedTweaks.Core;
using AscendedTweaks.Engine.Executors;

namespace AscendedTweaks.Engine.Export;

/// <summary>Ergebnis eines Exports: Skripte zum Anwenden und Zuruecksetzen; .reg nur, wenn reine Registry-Tweaks dabei sind.</summary>
public sealed record ScriptExport(
    string ApplyPs1,
    string RevertPs1,
    string? ApplyReg,
    string? RevertReg,
    IReadOnlyList<string> Exported,
    IReadOnlyList<(string Id, string Reason)> Skipped);

/// <summary>
/// Export als lesbares PowerShell-Skript und als .reg-Datei (Abschnitt 10.4). Die Originalwerte werden beim Export gelesen;
/// das Ruecksetz-Skript stellt genau diesen Stand wieder her. Kein versteckter Code, keine Netzwerkzugriffe, keine automatische
/// Rechteanforderung. Tweaks mit Aktionen, die sich nicht ohne Hilfsprogramm darstellen lassen, werden vollstaendig
/// ausgelassen (nie halb exportiert) und im Kopf der Skripte genannt.
/// </summary>
public sealed class ScriptExporter
{
    private readonly ExecutorSet _executors;
    private readonly ITweakCatalog _catalog;

    public ScriptExporter(ExecutorSet executors, ITweakCatalog catalog)
    {
        _executors = executors;
        _catalog = catalog;
    }

    public ScriptExport Export(IReadOnlyList<string> tweakIds, Func<string, string> name, string title, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tweakIds);
        var apply = new List<(TweakDefinition Tweak, string Ps, string? Reg)>();
        var revert = new List<(TweakDefinition Tweak, string Ps, string? Reg)>();
        var skipped = new List<(string, string)>();
        foreach (var id in tweakIds)
        {
            if (!_catalog.TryGet(id, out var tweak))
            {
                skipped.Add((id, "unknown tweak"));
                continue;
            }

            var reason = tweak.Apply.Select(Unsupported).FirstOrDefault(r => r is not null);
            if (reason is not null)
            {
                skipped.Add((id, reason));
                continue;
            }

            var applyPs = new StringBuilder();
            var revertPs = new StringBuilder();
            var regOnly = tweak.Apply.All(a => a is RegistrySetValueAction or RegistryDeleteValueAction or RegistryCreateKeyAction);
            var applyReg = regOnly ? new StringBuilder() : null;
            var revertReg = regOnly ? new StringBuilder() : null;
            foreach (var action in tweak.Apply)
            {
                Write(action, applyPs, revertPs, applyReg, revertReg);
            }

            apply.Add((tweak, Block(tweak, name, applyPs.ToString()), applyReg?.ToString()));
            revert.Add((tweak, Block(tweak, name, revertPs.ToString()), revertReg?.ToString()));
        }

        revert.Reverse();
        var admin = apply.Any(a => a.Tweak.Scope != TweakScope.User);
        var regApply = apply.Where(a => a.Reg is not null).ToList();
        return new ScriptExport(
            Script(title, "Apply", now, admin, apply.Select(a => a.Ps), skipped, name),
            Script(title, "Reset to the state at export time", now, admin, revert.Select(a => a.Ps), skipped, name),
            regApply.Count == 0 ? null : Reg(title, "Apply", now, regApply.Select(a => (a.Tweak, a.Reg!)), name),
            regApply.Count == 0 ? null : Reg(title, "Reset to the state at export time", now, revert.Where(r => r.Reg is not null).Select(r => (r.Tweak, r.Reg!)), name),
            apply.Select(a => a.Tweak.Id).ToList(),
            skipped);
    }

    public static string Ps(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string? Unsupported(TweakAction action) => action switch
    {
        RegistryDeleteKeyAction => "deletes a whole key; resetting is only possible in the app",
        SystemParametersInfoAction => "uses SystemParametersInfo; only possible in the app",
        RegistryListEntryAction => "changes an entry in a shared list; only possible in the app",
        CommandAction c when CommandExecutor.Definition(c.Command).BuiltInSet is not null => "uses a Windows interface (network adapter or Defender); only possible in the app",
        RegistrySetValueAction { Hive: RegHive.HKU } or RegistryDeleteValueAction { Hive: RegHive.HKU } or RegistryCreateKeyAction { Hive: RegHive.HKU } => "internal user area",
        _ => null,
    };

    private static string Block(TweakDefinition tweak, Func<string, string> name, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# ---- {tweak.Id}: {OneLine(name(tweak.Id))}");
        foreach (var source in tweak.Sources)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"# Source: {OneLine(source)}");
        }

        var builds = string.Join(", ", tweak.Builds.Select(r => r.Max is { } max ? $"{r.Min} bis {max}" : $"ab {r.Min}"));
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Applies to: {string.Join(", ", tweak.Editions)}; Build {builds}");
        if (tweak.Scope == TweakScope.UserPolicy)
        {
            builder.AppendLine(@"# User policy: run in the account of the affected user; HKCU\Software\Policies can only be written with administrator rights.");
        }

        builder.Append(body);
        return builder.ToString();
    }

    private static string Script(string title, string purpose, DateTimeOffset now, bool admin, IEnumerable<string> blocks, IReadOnlyList<(string Id, string Reason)> skipped, Func<string, string> name)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# {ProductInfo.Name}-Export: {OneLine(title)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Purpose: {purpose}. Created {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
        builder.AppendLine("# This script changes only the values listed below. It downloads nothing and does not request rights.");
        builder.AppendLine("# No backup and no restore point: for a safe reset, use the app instead.");
        foreach (var (id, reason) in skipped)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"# Not exported: {id} ({OneLine(name(id))}): {reason}.");
        }

        builder.AppendLine("#Requires -Version 5.1");
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        if (admin)
        {
            builder.AppendLine("$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()");
            builder.AppendLine("if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {");
            builder.AppendLine("    Write-Error 'This script changes system settings and needs administrator rights. Start PowerShell as administrator.'");
            builder.AppendLine("    exit 2");
            builder.AppendLine("}");
        }

        foreach (var block in blocks)
        {
            builder.AppendLine();
            builder.Append(block);
        }

        return builder.ToString();
    }

    private static string Reg(string title, string purpose, DateTimeOffset now, IEnumerable<(TweakDefinition Tweak, string Body)> blocks, Func<string, string> name)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Windows Registry Editor Version 5.00");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"; {ProductInfo.Name}-Export: {OneLine(title)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"; Purpose: {purpose}. Created {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC. Pure registry tweaks only.");
        foreach (var (tweak, body) in blocks)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"; {tweak.Id}: {OneLine(name(tweak.Id))}");
            builder.Append(body);
        }

        return builder.ToString();
    }

    private void Write(TweakAction action, StringBuilder apply, StringBuilder revert, StringBuilder? applyReg, StringBuilder? revertReg)
    {
        switch (action)
        {
            case RegistrySetValueAction set:
                {
                    var target = set.Value.ToData(set.Kind);
                    var before = _executors.Registry.GetValue(set.Hive, set.Path, set.Name);
                    var keyExisted = _executors.Registry.KeyExists(set.Hive, set.Path);
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: {RegPath(set.Hive, set.Path)}\\{set.Name} = {Describe(before)}");
                    apply.Append(PsSet(set.Hive, set.Path, set.Name, target));
                    revert.Append(before is null ? PsDelete(set.Hive, set.Path, set.Name) : PsSet(set.Hive, set.Path, set.Name, before));
                    applyReg?.Append(RegKey(set.Hive, set.Path)).AppendLine(RegValue(set.Name, target));
                    revertReg?.Append(RegKey(set.Hive, set.Path)).AppendLine(before is null ? RegName(set.Name) + "=-" : RegValue(set.Name, before));
                    if (!keyExisted)
                    {
                        // Wie die App: vorher fehlende Schluessel (auch neu angelegte Elternschluessel) nur entfernen, wenn sie leer sind.
                        foreach (var created in MissingKeys(set.Hive, set.Path))
                        {
                            revert.AppendLine(CultureInfo.InvariantCulture, $"$key = Get-Item -LiteralPath {Ps(PsPath(set.Hive, created))} -ErrorAction SilentlyContinue");
                            revert.AppendLine("if ($key -and $key.Property.Count -eq 0 -and $key.SubKeyCount -eq 0) { Remove-Item -LiteralPath $key.PSPath }");
                        }

                        revertReg?.AppendLine(CultureInfo.InvariantCulture, $"; The key {RegPath(set.Hive, set.Path)} did not exist before; a .reg file can only delete it with its contents, so it is kept.");
                    }

                    break;
                }

            case RegistryDeleteValueAction delete:
                {
                    var before = _executors.Registry.GetValue(delete.Hive, delete.Path, delete.Name);
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: {RegPath(delete.Hive, delete.Path)}\\{delete.Name} = {Describe(before)}");
                    apply.Append(PsDelete(delete.Hive, delete.Path, delete.Name));
                    if (before is not null)
                    {
                        revert.Append(PsSet(delete.Hive, delete.Path, delete.Name, before));
                    }

                    applyReg?.Append(RegKey(delete.Hive, delete.Path)).AppendLine(RegName(delete.Name) + "=-");
                    if (before is not null)
                    {
                        revertReg?.Append(RegKey(delete.Hive, delete.Path)).AppendLine(RegValue(delete.Name, before));
                    }

                    break;
                }

            case RegistryCreateKeyAction create:
                {
                    var existed = _executors.Registry.KeyExists(create.Hive, create.Path);
                    var path = Ps(PsPath(create.Hive, create.Path));
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Before: key {(existed ? "present" : "missing")}");
                    apply.AppendLine(CultureInfo.InvariantCulture, $"if (-not (Test-Path -LiteralPath {path})) {{ New-Item -Path {path} -Force | Out-Null }}");
                    if (create.DefaultValue is { } value)
                    {
                        apply.AppendLine(CultureInfo.InvariantCulture, $"Set-Item -LiteralPath {path} -Value {Ps(value)}");
                    }

                    if (!existed)
                    {
                        revert.AppendLine(CultureInfo.InvariantCulture, $"if (Test-Path -LiteralPath {path}) {{ Remove-Item -LiteralPath {path} -Recurse -Force }}");
                    }

                    applyReg?.Append(RegKey(create.Hive, create.Path));
                    if (create.DefaultValue is { } defaultValue)
                    {
                        applyReg?.AppendLine("@=\"" + RegEscape(defaultValue) + "\"");
                    }

                    if (!existed)
                    {
                        revertReg?.AppendLine(CultureInfo.InvariantCulture, $"[-{RegPath(create.Hive, create.Path)}]");
                    }

                    break;
                }

            case ServiceSetStartTypeAction service:
                {
                    var before = _executors.Services.GetConfig(service.Service);
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: service {service.Service} = {(before is null ? "not installed" : ServiceExecutor.Display(before.StartType, before.DelayedAutoStart))}");
                    apply.Append(PsService(service.Service, ServiceExecutor.Map(service.StartType)));
                    if (service.StopNow)
                    {
                        apply.AppendLine(CultureInfo.InvariantCulture, $"Stop-Service -Name {Ps(service.Service)} -Force -ErrorAction SilentlyContinue");
                    }

                    revert.Append(before is null || before.StartType is ServiceStartType.Boot or ServiceStartType.System
                        ? $"# Service {service.Service}: original value cannot be restored.{Environment.NewLine}"
                        : PsService(service.Service, (before.StartType, before.DelayedAutoStart)));
                    break;
                }

            case ScheduledTaskSetEnabledAction task:
                {
                    var before = _executors.Tasks.Read(task.TaskPath);
                    var split = task.TaskPath.LastIndexOf('\\');
                    var folder = task.TaskPath[..(split + 1)];
                    var taskName = task.TaskPath[(split + 1)..];
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: task {task.TaskPath} = {(before is null ? "not present" : ScheduledTaskExecutor.Display(before.Value))}");
                    apply.AppendLine(CultureInfo.InvariantCulture, $"{(task.Enabled ? "Enable" : "Disable")}-ScheduledTask -TaskPath {Ps(folder)} -TaskName {Ps(taskName)} | Out-Null");
                    if (before is { } enabled)
                    {
                        revert.AppendLine(CultureInfo.InvariantCulture, $"{(enabled ? "Enable" : "Disable")}-ScheduledTask -TaskPath {Ps(folder)} -TaskName {Ps(taskName)} | Out-Null");
                    }

                    break;
                }

            case PowerPlanSetAction plan:
                {
                    var before = _executors.Power.GetActive();
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: active power plan {before:D}");
                    if (plan.DuplicateFrom is { } source)
                    {
                        apply.AppendLine(CultureInfo.InvariantCulture, $"if (-not (& \"$env:SystemRoot\\System32\\powercfg.exe\" /list | Select-String -SimpleMatch '{plan.SchemeGuid:D}')) {{ & \"$env:SystemRoot\\System32\\powercfg.exe\" /duplicatescheme {source:D} {plan.SchemeGuid:D} | Out-Null }}");
                    }

                    apply.AppendLine(CultureInfo.InvariantCulture, $"& \"$env:SystemRoot\\System32\\powercfg.exe\" /setactive {plan.SchemeGuid:D}");
                    revert.AppendLine(CultureInfo.InvariantCulture, $"& \"$env:SystemRoot\\System32\\powercfg.exe\" /setactive {before:D}");
                    break;
                }

            case PowerSettingSetAction setting:
                {
                    var scheme = _executors.Power.GetActive();
                    var before = _executors.Power.ReadAcValue(scheme, setting.Subgroup, setting.Setting);
                    var powercfg = "& \"$env:SystemRoot\\System32\\powercfg.exe\"";
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: power plan {scheme:D}, {setting.Setting:D} (plugged in) = {before?.ToString(CultureInfo.InvariantCulture) ?? "not present"}");
                    apply.AppendLine(CultureInfo.InvariantCulture, $"{powercfg} /setacvalueindex {scheme:D} {setting.Subgroup:D} {setting.Setting:D} {setting.Value}");
                    apply.AppendLine(CultureInfo.InvariantCulture, $"{powercfg} /setactive {scheme:D}");
                    if (before is { } value)
                    {
                        revert.AppendLine(CultureInfo.InvariantCulture, $"{powercfg} /setacvalueindex {scheme:D} {setting.Subgroup:D} {setting.Setting:D} {value}");
                        revert.AppendLine(CultureInfo.InvariantCulture, $"{powercfg} /setactive {scheme:D}");
                    }

                    break;
                }

            case CommandAction command:
                {
                    var definition = CommandExecutor.Definition(command.Command);
                    var before = _executors.Commands.Query(command.Command);
                    apply.AppendLine(CultureInfo.InvariantCulture, $"# Original value: {command.Command} = {before ?? "unknown"}");
                    apply.AppendLine(PsCommand(definition, command.Value));
                    revert.AppendLine(before is not null && definition.IsAllowed(before)
                        ? PsCommand(definition, before)
                        : $"# {command.Command}: original value unknown or readable only with administrator rights; reset in the app.");
                    break;
                }

            default:
                throw new InvalidOperationException("Action cannot be exported: " + action.GetType().Name);
        }
    }

    /// <summary>Der Pfad und alle Elternschluessel, die beim Export fehlen, vom tiefsten zum hoechsten.</summary>
    private IEnumerable<string> MissingKeys(RegHive hive, string path)
    {
        var current = path;
        while (current.Length > 0 && !_executors.Registry.KeyExists(hive, current))
        {
            yield return current;
            var split = current.LastIndexOf('\\');
            current = split < 0 ? string.Empty : current[..split];
        }
    }

    private static string PsCommand(CommandDefinition definition, string value) =>
        $"& \"$env:SystemRoot\\System32\\{definition.Executable}\" " + string.Join(' ', definition.BuildSetArguments(value).Select(Ps));

    private static string PsService(string service, (ServiceStartType Type, bool Delayed) start)
    {
        if (start.Type == ServiceStartType.Automatic && start.Delayed)
        {
            return $"& \"$env:SystemRoot\\System32\\sc.exe\" config {Ps(service)} start= delayed-auto | Out-Null{Environment.NewLine}";
        }

        var type = start.Type switch
        {
            ServiceStartType.Automatic => "Automatic",
            ServiceStartType.Manual => "Manual",
            _ => "Disabled",
        };
        return $"Set-Service -Name {Ps(service)} -StartupType {type}{Environment.NewLine}";
    }

    private static string PsSet(RegHive hive, string path, string name, RegistryData data)
    {
        var key = Ps(PsPath(hive, path));
        var (type, value) = data.Kind switch
        {
            RegValueKind.DWord => ("DWord", unchecked((int)(uint)data.Number).ToString(CultureInfo.InvariantCulture)),
            RegValueKind.QWord => ("QWord", unchecked((long)data.Number).ToString(CultureInfo.InvariantCulture)),
            RegValueKind.String => ("String", Ps(data.Text ?? string.Empty)),
            RegValueKind.ExpandString => ("ExpandString", Ps(data.Text ?? string.Empty)),
            RegValueKind.MultiString => ("MultiString", "@(" + string.Join(", ", (data.Lines ?? []).Select(Ps)) + ")"),
            _ => ("Binary", "([byte[]](" + string.Join(", ", (data.GetBytes() ?? []).Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture))) + "))"),
        };
        return $"if (-not (Test-Path -LiteralPath {key})) {{ New-Item -Path {key} -Force | Out-Null }}{Environment.NewLine}"
            + $"New-ItemProperty -LiteralPath {key} -Name {Ps(name)} -PropertyType {type} -Value {value} -Force | Out-Null{Environment.NewLine}";
    }

    private static string PsDelete(RegHive hive, string path, string name) =>
        $"Remove-ItemProperty -LiteralPath {Ps(PsPath(hive, path))} -Name {Ps(name)} -ErrorAction SilentlyContinue{Environment.NewLine}";

    private static string PsPath(RegHive hive, string path) => (hive == RegHive.HKLM ? "HKLM:\\" : "HKCU:\\") + path;

    private static string RegPath(RegHive hive, string path) => (hive == RegHive.HKLM ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + path;

    private static string RegKey(RegHive hive, string path) => $"[{RegPath(hive, path)}]{Environment.NewLine}";

    private static string RegName(string name) => name.Length == 0 ? "@" : "\"" + RegEscape(name) + "\"";

    private static string RegEscape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string RegValue(string name, RegistryData data) => RegName(name) + "=" + data.Kind switch
    {
        RegValueKind.DWord => "dword:" + ((uint)data.Number).ToString("x8", CultureInfo.InvariantCulture),
        RegValueKind.String => "\"" + RegEscape(data.Text ?? string.Empty) + "\"",
        RegValueKind.ExpandString => "hex(2):" + Hex(data.ToRaw()),
        RegValueKind.MultiString => "hex(7):" + Hex(data.ToRaw()),
        RegValueKind.QWord => "hex(b):" + Hex(data.ToRaw()),
        _ => "hex:" + Hex(data.ToRaw()),
    };

    private static string Hex(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    private static string Describe(RegistryData? data) => data is null
        ? "missing"
        : data.Kind switch
        {
            RegValueKind.DWord or RegValueKind.QWord => $"{data.Number} ({data.Kind})",
            RegValueKind.String or RegValueKind.ExpandString => $"\"{OneLine(data.Text ?? string.Empty)}\" ({data.Kind})",
            RegValueKind.MultiString => $"{data.Lines?.Count ?? 0} lines (MultiString)",
            _ => $"{data.GetBytes()?.Length ?? 0} Bytes (Binary)",
        };

    /// <summary>Texte in Kommentaren immer einzeilig: ein Zeilenumbruch im Namen koennte sonst Code ausserhalb des Kommentars erzeugen.</summary>
    private static string OneLine(string text) => new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
