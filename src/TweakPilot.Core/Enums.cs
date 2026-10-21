namespace TweakPilot.Core;

public enum RiskLevel
{
    Safe,
    Caution,
    Expert,
}

/// <summary>Wirkung fuer den Nutzer (V2, Abschnitt 5.1): "Spuerbar" nur mit Messbeleg, "Unklar" nur im Experten-Modus.</summary>
public enum ImpactLevel
{
    Noticeable,
    Small,
    Unclear,
}

public enum EvidenceLevel
{
    Proven,
    Minor,
    Disputed,

    /// <summary>Nur zur Dokumentation von Ausschluessen im Linter erlaubt, wird nie ausgeliefert.</summary>
    Placebo,
}

public enum TweakScope
{
    /// <summary>Benutzereinstellung unter HKCU, ohne Admin, im Prozess der App.</summary>
    User,

    /// <summary>Maschinenweit (HKLM, Dienste, Energieplaene, Befehle), im Helfer.</summary>
    Machine,

    /// <summary>
    /// Benutzer-Richtlinie unter HKCU\Software\Policies: Diesen Zweig duerfen nur Administratoren schreiben.
    /// Der Helfer schreibt dafuer gezielt nach HKEY_USERS\SID des anfragenden Benutzers, nie nach HKCU.
    /// </summary>
    UserPolicy,
}

public enum WindowsEdition
{
    Home,
    Pro,
    Enterprise,
    Education,
}

public enum TweakState
{
    Applied,
    NotApplied,
    Mixed,
    Unavailable,
}

/// <summary>Ziel-Starttyp eines Dienstes, wie er im Katalog angegeben wird.</summary>
public enum ServiceStartSetting
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
}

/// <summary>Roher Starttyp laut Dienststeuerung (Werte 0 bis 4 wie in der Registry).</summary>
public enum ServiceStartType
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}
