namespace AscendedTweaks.Engine.Insights;

/// <summary>Bildschirm: aktuelle und hoechste Bildwiederholrate bei der aktuellen Aufloesung (EnumDisplaySettings).</summary>
public sealed record DisplayFacts(int CurrentHz, int MaxHz);

/// <summary>Arbeitsspeicher-Module aus Win32_PhysicalMemory: Nenntakt (Speed) und konfigurierter Takt (ConfiguredClockSpeed) in MHz.</summary>
public sealed record MemoryModule(int? RatedMhz, int? ConfiguredMhz);

/// <summary>Nur lesend ermittelte Fakten fuer die Gaming-Checks (V2 Abschnitt 5.3). Nicht lesbare Werte sind null.</summary>
public sealed record GamingFacts
{
    public DisplayFacts? Display { get; init; }

    public Guid? ActivePowerPlan { get; init; }

    public bool HasBattery { get; init; }

    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];

    public IReadOnlyList<MemoryModule>? MemoryModules { get; init; }

    public int? SystemDriveFreePercent { get; init; }

    public bool? GameMode { get; init; }

    public int? Autostarts { get; init; }

    public ProcessGroup? TopProcess { get; init; }

    public SecurityFacts? Security { get; init; }

    public DateOnly Today { get; init; }
}

/// <summary>Wohin ein Check springt; die Oberflaeche ordnet das der Seite des aktuellen Modus zu.</summary>
public enum GamingJump
{
    None,
    Cleanup,
    Autostart,
    AntiCheat,
    GameProfiles,
}

/// <summary>
/// Ein Check: Ergebnis in einem Satz (Textschluessel mit Platzhaltern), Zustand, "Behoben?"-Anleitung, optional ein Link
/// auf eine ms-settings-Seite oder eine Herstellerseite und ein Sprung in der App. Checks aendern nie etwas.
/// </summary>
public sealed record GamingCheck(
    string Id,
    CheckState State,
    string TextKey,
    IReadOnlyList<(string Name, object Value)> Args,
    string? HowToKey = null,
    string? Link = null,
    GamingJump Jump = GamingJump.None);

public static class GamingChecks
{
    /// <summary>GUID des Windows-Energiesparplans "Energiesparmodus" (Power saver).</summary>
    public static readonly Guid PowerSaverPlan = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");

    public const int LowDiskPercent = 15;

    public const int OldDriverDays = 365;

    public static IReadOnlyList<GamingCheck> Evaluate(GamingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return
        [
            Refresh(facts.Display),
            Power(facts.ActivePowerPlan),
            Hybrid(facts),
            MemorySpeed(facts.MemoryModules),
            MemoryChannels(facts.MemoryModules),
            Disk(facts.SystemDriveFreePercent),
            GameMode(facts.GameMode),
            Driver(facts.Gpus, facts.Today),
            Background(facts.Autostarts, facts.TopProcess),
            AntiCheat(facts.Security),
        ];
    }

    private static GamingCheck Unknown(string id) => new(id, CheckState.Unknown, "check.unknown", []);

    private static GamingCheck Refresh(DisplayFacts? display)
    {
        if (display is not { CurrentHz: > 1, MaxHz: > 1 })
        {
            return Unknown("refresh");
        }

        return display.CurrentHz < display.MaxHz
            ? new("refresh", CheckState.Attention, "check.refresh.low", [("current", display.CurrentHz), ("max", display.MaxHz)], "check.refresh.howto", "ms-settings:display-advanced")
            : new("refresh", CheckState.Good, "check.refresh.ok", [("current", display.CurrentHz)]);
    }

    private static GamingCheck Power(Guid? plan) => plan switch
    {
        null => Unknown("power"),
        { } p when p == PowerSaverPlan => new("power", CheckState.Attention, "check.power.saver", [], "check.power.howto", "ms-settings:powersleep"),
        _ => new("power", CheckState.Good, "check.power.ok", []),
    };

    /// <summary>Notebook mit integrierter und eigener Grafikkarte: Spiele koennen auf der sparsamen GPU landen.</summary>
    private static GamingCheck Hybrid(GamingFacts facts)
    {
        var vendors = facts.Gpus.Select(g => g.Vendor).Where(v => v is "Intel" or "AMD" or "NVIDIA").ToList();
        var hybrid = facts.HasBattery && vendors.Count >= 2;
        return hybrid
            ? new("hybrid", CheckState.Info, "check.hybrid.yes", [], "check.hybrid.howto", "ms-settings:display-advancedgraphics", GamingJump.GameProfiles)
            : new("hybrid", CheckState.Good, "check.hybrid.no", []);
    }

    /// <summary>Nur bei eindeutigem Messwert: alle Module melden beide Takte, und alle liegen gleich.</summary>
    private static GamingCheck MemorySpeed(IReadOnlyList<MemoryModule>? modules)
    {
        if (modules is not { Count: > 0 } || modules.Any(m => m.RatedMhz is not > 0 || m.ConfiguredMhz is not > 0)
            || modules.Select(m => (m.RatedMhz, m.ConfiguredMhz)).Distinct().Count() != 1)
        {
            return Unknown("ramspeed");
        }

        var (rated, configured) = (modules[0].RatedMhz!.Value, modules[0].ConfiguredMhz!.Value);
        return configured < rated
            ? new("ramspeed", CheckState.Info, "check.ramspeed.low", [("configured", configured), ("rated", rated)], "check.ramspeed.howto")
            : new("ramspeed", CheckState.Good, "check.ramspeed.ok", [("configured", configured)]);
    }

    private static GamingCheck MemoryChannels(IReadOnlyList<MemoryModule>? modules) => modules switch
    {
        null or { Count: 0 } => Unknown("ramcount"),
        { Count: 1 } => new("ramcount", CheckState.Info, "check.ramcount.one", [], "check.ramcount.howto"),
        _ => new("ramcount", CheckState.Good, "check.ramcount.ok", [("n", modules.Count)]),
    };

    private static GamingCheck Disk(int? freePercent) => freePercent switch
    {
        null => Unknown("disk"),
        < LowDiskPercent => new("disk", CheckState.Attention, "check.disk.low", [("percent", freePercent.Value)], "check.disk.howto", null, GamingJump.Cleanup),
        _ => new("disk", CheckState.Good, "check.disk.ok", [("percent", freePercent.Value)]),
    };

    private static GamingCheck GameMode(bool? on) => on switch
    {
        null => Unknown("gamemode"),
        true => new("gamemode", CheckState.Good, "check.gamemode.on", [], null, "ms-settings:gaming-gamemode"),
        false => new("gamemode", CheckState.Info, "check.gamemode.off", [], "check.gamemode.howto", "ms-settings:gaming-gamemode"),
    };

    /// <summary>Aeltester Treiber einer Grafikkarte von AMD, NVIDIA oder Intel; Link nur zur Herstellerseite, kein Download durch die App.</summary>
    private static GamingCheck Driver(IReadOnlyList<GpuInfo> gpus, DateOnly today)
    {
        var dated = gpus.Where(g => g.DriverDate is not null && VendorPage(g.Vendor) is not null).ToList();
        if (dated.Count == 0)
        {
            return Unknown("driver");
        }

        var oldest = dated.OrderBy(g => g.DriverDate).First();
        var days = today.DayNumber - oldest.DriverDate!.Value.DayNumber;
        return days > OldDriverDays
            ? new("driver", CheckState.Info, "check.driver.old", [("name", oldest.Name), ("months", days / 30)], "check.driver.howto", VendorPage(oldest.Vendor))
            : new("driver", CheckState.Good, "check.driver.ok", [("name", oldest.Name)]);
    }

    public static string? VendorPage(string vendor) => vendor switch
    {
        "NVIDIA" => "https://www.nvidia.com/Download/index.aspx",
        "AMD" => "https://www.amd.com/en/support/download/drivers.html",
        "Intel" => "https://www.intel.com/content/www/us/en/download-center/home.html",
        _ => null,
    };

    private static GamingCheck Background(int? autostarts, ProcessGroup? top)
    {
        if (autostarts is null)
        {
            return Unknown("background");
        }

        return top is null
            ? new("background", CheckState.Info, "check.background.count", [("n", autostarts.Value)], null, null, GamingJump.Autostart)
            : new("background", CheckState.Info, "check.background.top", [("n", autostarts.Value), ("name", top.Name), ("mb", top.WorkingSetBytes / 1024 / 1024)], null, null, GamingJump.Autostart);
    }

    /// <summary>Zusammenfassung der bestehenden Anti-Cheat-Pruefung (GAME-020).</summary>
    private static GamingCheck AntiCheat(SecurityFacts? security)
    {
        if (security is null)
        {
            return Unknown("anticheat");
        }

        var checks = AntiCheatCheck.Evaluate(security);
        return checks.Any(c => c.State == CheckState.Attention)
            ? new("anticheat", CheckState.Attention, "check.anticheat.attention", [], "check.anticheat.howto", null, GamingJump.AntiCheat)
            : new("anticheat", CheckState.Good, "check.anticheat.ok", [], null, null, GamingJump.AntiCheat);
    }
}
