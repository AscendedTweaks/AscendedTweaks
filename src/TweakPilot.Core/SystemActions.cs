using System.Text.RegularExpressions;

namespace TweakPilot.Core;

/// <summary>Geplante Aufgabe aktivieren oder deaktivieren (Abschnitt 5.2, Aktionstyp ScheduledTaskSetEnabled).</summary>
public sealed class ScheduledTaskSetEnabledAction : TweakAction
{
    /// <summary>Vollstaendiger Aufgabenpfad, zum Beispiel "\Microsoft\Windows\...\Consolidator".</summary>
    public required string TaskPath { get; init; }

    public required bool Enabled { get; init; }
}

/// <summary>Systemeinstellung ueber SystemParametersInfo (Abschnitt 5.2), nur Parameter aus der Whitelist.</summary>
public sealed class SystemParametersInfoAction : TweakAction
{
    public required string Parameter { get; init; }

    public required uint Value { get; init; }
}

public sealed class ScheduledTaskEnabledEqualsCondition : DetectCondition
{
    public required string TaskPath { get; init; }

    public required bool Value { get; init; }
}

public sealed class SystemParameterEqualsCondition : DetectCondition
{
    public required string Parameter { get; init; }

    public required uint Value { get; init; }
}

/// <summary>Zugriff auf die Aufgabenplanung.</summary>
public interface IScheduledTaskAccess
{
    /// <summary>Liefert null, wenn die Aufgabe nicht existiert oder nicht lesbar ist.</summary>
    bool? GetEnabled(string taskPath);

    void SetEnabled(string taskPath, bool enabled);
}

/// <summary>Zugriff auf SystemParametersInfo; Parameter-IDs stammen aus <see cref="SystemParameterWhitelist"/>.</summary>
public interface ISystemParametersAccess
{
    uint Get(string parameter);

    /// <summary>Setzt den Wert dauerhaft fuer den Benutzer und benachrichtigt laufende Programme.</summary>
    void Set(string parameter, uint value);
}

public enum SystemParameterKind
{
    /// <summary>BOOL; Lesen ueber pvParam (Zeiger auf BOOL), Schreiben ueber pvParam (Wert selbst).</summary>
    BoolInPointer,

    /// <summary>ANIMATIONINFO.iMinAnimate.</summary>
    AnimationInfo,

    /// <summary>DWORD; Lesen ueber pvParam (Zeiger), Schreiben ueber uiParam.</summary>
    UIntInUiParam,

    /// <summary>SPI_GETMOUSE/SETMOUSE: drei Werte (Schwelle 1, Schwelle 2, Beschleunigung), gepackt als t1 | t2 &lt;&lt; 8 | acc &lt;&lt; 16.</summary>
    MouseTriple,

    /// <summary>Nur das Bit "Tastenkuerzel aktiv" (0x4) in STICKYKEYS/TOGGLEKEYS (8 Byte) oder FILTERKEYS (24 Byte); Wert 0 oder 1.</summary>
    AccessibilityHotkey,
}

/// <summary>Ein erlaubter SystemParametersInfo-Parameter mit seinem Registry-Abdruck (fuer Doku und Integrationstest).</summary>
public sealed record SystemParameterDefinition
{
    public required string Id { get; init; }

    public required uint GetAction { get; init; }

    public required uint SetAction { get; init; }

    public required SystemParameterKind Kind { get; init; }

    public required uint Min { get; init; }

    public required uint Max { get; init; }

    /// <summary>Registry-Stelle, die Windows beim dauerhaften Setzen (SPIF_UPDATEINIFILE) beschreibt.</summary>
    public required string RegistryFootprint { get; init; }
}

/// <summary>Fest eingebettete Whitelist (Abschnitt 5.2, Aktionstyp SystemParametersInfo).</summary>
public static class SystemParameterWhitelist
{
    public static readonly IReadOnlyDictionary<string, SystemParameterDefinition> All = new[]
    {
        new SystemParameterDefinition
        {
            Id = "ClientAreaAnimation",
            GetAction = 0x1042,
            SetAction = 0x1043,
            Kind = SystemParameterKind.BoolInPointer,
            Min = 0,
            Max = 1,
            RegistryFootprint = @"HKCU\Control Panel\Desktop\UserPreferencesMask",
        },
        new SystemParameterDefinition
        {
            Id = "MinimizeMaximizeAnimation",
            GetAction = 0x0048,
            SetAction = 0x0049,
            Kind = SystemParameterKind.AnimationInfo,
            Min = 0,
            Max = 1,
            RegistryFootprint = @"HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate",
        },
        new SystemParameterDefinition
        {
            Id = "MouseAcceleration",
            GetAction = 0x0003,
            SetAction = 0x0004,
            Kind = SystemParameterKind.MouseTriple,
            Min = 0,
            Max = 0xFFFFFF,
            RegistryFootprint = @"HKCU\Control Panel\Mouse\MouseSpeed, MouseThreshold1, MouseThreshold2",
        },
        new SystemParameterDefinition
        {
            Id = "StickyKeysHotkey",
            GetAction = 0x003A,
            SetAction = 0x003B,
            Kind = SystemParameterKind.AccessibilityHotkey,
            Min = 0,
            Max = 1,
            RegistryFootprint = @"HKCU\Control Panel\Accessibility\StickyKeys\Flags",
        },
        new SystemParameterDefinition
        {
            Id = "FilterKeysHotkey",
            GetAction = 0x0032,
            SetAction = 0x0033,
            Kind = SystemParameterKind.AccessibilityHotkey,
            Min = 0,
            Max = 1,
            RegistryFootprint = @"HKCU\Control Panel\Accessibility\Keyboard Response\Flags",
        },
        new SystemParameterDefinition
        {
            Id = "ToggleKeysHotkey",
            GetAction = 0x0034,
            SetAction = 0x0035,
            Kind = SystemParameterKind.AccessibilityHotkey,
            Min = 0,
            Max = 1,
            RegistryFootprint = @"HKCU\Control Panel\Accessibility\ToggleKeys\Flags",
        },
        new SystemParameterDefinition
        {
            Id = "MenuShowDelay",
            GetAction = 0x006A,
            SetAction = 0x006B,
            Kind = SystemParameterKind.UIntInUiParam,
            Min = 0,
            Max = 4000,
            RegistryFootprint = @"HKCU\Control Panel\Desktop\MenuShowDelay",
        },
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);

    public static SystemParameterDefinition Get(string id)
    {
        return All.TryGetValue(id, out var definition)
            ? definition
            : throw new TweakPilotException(ErrorKind.CatalogInvalid, $"Parameter {id} is not on the allowlist.");
    }
}

public static partial class ScheduledTaskPaths
{
    /// <summary>Nur Aufgaben unter \Microsoft\Windows\, ohne Platzhalter und ohne relative Teile.</summary>
    public static bool IsAllowed(string taskPath)
    {
        ArgumentNullException.ThrowIfNull(taskPath);
        return taskPath.Length <= 260
            && taskPath.StartsWith(@"\Microsoft\Windows\", StringComparison.Ordinal)
            && AllowedPattern().IsMatch(taskPath)
            && !taskPath.Split('\\').Any(s => s is "." or "..");
    }

    [GeneratedRegex(@"^(\\[A-Za-z0-9 ._()\-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();
}
