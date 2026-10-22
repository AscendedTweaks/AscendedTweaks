using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AscendedTweaks.Core;

/// <summary>Netzwerk-Einstellungen pro physischem Adapter (Abschnitt 6.7). Lesen ohne, Schreiben mit Administratorrechten.</summary>
public interface INetworkAccess
{
    /// <summary>GUIDs der aktiven physischen Adapter (keine VPN-, Tunnel- oder virtuellen Adapter).</summary>
    IReadOnlyList<string> PhysicalAdapters();

    /// <summary>DNS-Server eines Adapters; leer bedeutet automatisch (DHCP).</summary>
    IReadOnlyList<string> GetDnsServers(string adapter);

    /// <summary>Setzt statische DNS-Server; eine leere Liste stellt "automatisch (DHCP)" ein.</summary>
    void SetDnsServers(string adapter, IReadOnlyList<string> servers);

    /// <summary>Autotuning-Stufe des TCP-Empfangsfensters (normal, disabled, highlyrestricted, restricted, experimental) oder null.</summary>
    string? GetAutotuning();

    /// <summary>"Computer darf das Geraet ausschalten" fuer einen Adapter; null, wenn nicht lesbar.</summary>
    bool? GetPowerSaving(string adapter);

    void SetPowerSaving(string adapter, bool enabled);

    /// <summary>"Energy-efficient ethernet" (Schluesselwort *EEE) als "on"/"off"; null, wenn der Treiber die Eigenschaft nicht hat.</summary>
    string? GetEnergyEfficientEthernet(string adapter) => null;

    void SetEnergyEfficientEthernet(string adapter, bool enabled) =>
        throw new AscendedTweaksException(ErrorKind.Unavailable, "Energy-efficient ethernet is not available here.");
}

/// <summary>
/// Zustand ueber alle physischen Adapter als Zeichenkette. Einheitlich: nur der Wert ("1.1.1.1,1.0.0.1", "dhcp", "on");
/// sonst pro Adapter "{guid}=wert;{guid}=wert". So kann die Engine den Vorher-Zustand sichern und exakt wiederherstellen.
/// </summary>
public static partial class AdapterState
{
    public const string Dhcp = "dhcp";

    public static string Combine(IReadOnlyDictionary<string, string> perAdapter)
    {
        ArgumentNullException.ThrowIfNull(perAdapter);
        var distinct = perAdapter.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1
            ? distinct[0]
            : string.Join(';', perAdapter.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>Zerlegt den Zustand; ein einheitlicher Wert gilt fuer alle angegebenen Adapter.</summary>
    public static IReadOnlyDictionary<string, string> Split(string state, IReadOnlyList<string> adapters)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(adapters);
        if (!state.Contains('=', StringComparison.Ordinal))
        {
            return adapters.ToDictionary(a => a, _ => state, StringComparer.OrdinalIgnoreCase);
        }

        return state.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsAdapterId(string id) => AdapterPattern().IsMatch(id);

    /// <summary>Gueltiger DNS-Zustand: einheitliche Liste/DHCP oder pro Adapter mit gueltigen GUIDs.</summary>
    public static bool IsValidDnsState(string state) => IsValid(state, IsValidDnsValue);

    public static bool IsValidPowerState(string state) => IsValid(state, v => v is "on" or "off");

    public static bool IsValidDnsValue(string value) =>
        value == Dhcp || (DnsServers.Parse(value) is { Count: > 0 and <= 8 } list && list.All(DnsServers.IsUsable));

    private static bool IsValid(string state, Func<string, bool> valueCheck)
    {
        if (string.IsNullOrEmpty(state) || state.Length > 2000)
        {
            return false;
        }

        if (!state.Contains('=', StringComparison.Ordinal))
        {
            return valueCheck(state);
        }

        return state.Split(';', StringSplitOptions.RemoveEmptyEntries).All(p => p.Split('=', 2) is [var id, var value] && IsAdapterId(id) && valueCheck(value));
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$", RegexOptions.CultureInvariant)]
    private static partial Regex AdapterPattern();
}

public static class DnsServers
{
    public const string Cloudflare = "1.1.1.1,1.0.0.1,2606:4700:4700::1111,2606:4700:4700::1001";
    public const string Google = "8.8.8.8,8.8.4.4,2001:4860:4860::8888,2001:4860:4860::8844";
    public const string Quad9 = "9.9.9.9,149.112.112.112,2620:fe::fe,2620:fe::9";

    /// <summary>Kommagetrennte Adressen; null bei einem ungueltigen Eintrag (strikt mit IPAddress.TryParse, Abschnitt 9.4).</summary>
    public static IReadOnlyList<IPAddress>? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new List<IPAddress>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            // Nur Ziffern, Punkte, Doppelpunkte und Hex: verhindert Zonen-IDs ("%") und andere Sonderformen.
            if (part.Length is 0 or > 45 || part.Any(c => !(char.IsAsciiHexDigit(c) || c is '.' or ':')) || !IPAddress.TryParse(part, out var address))
            {
                return null;
            }

            // IPv4 nur in Normalform (vier Oktette ohne fuehrende Nullen): TryParse akzeptiert sonst Kurzformen wie "1.1.1".
            if (address.AddressFamily == AddressFamily.InterNetwork && !string.Equals(address.ToString(), part, StringComparison.Ordinal))
            {
                return null;
            }

            result.Add(address);
        }

        return result;
    }

    /// <summary>Unbrauchbar sind Loopback, Multicast, unspezifizierte Adressen und Broadcast.</summary>
    public static bool IsUsable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? !address.IsIPv6Multicast
            : address.GetAddressBytes()[0] is < 224 and not 0;
    }

    /// <summary>Private oder link-lokale Adressen: erlaubt, aber nur nach ausdruecklicher Bestaetigung in der Oberflaeche.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    public static string Normalize(IEnumerable<IPAddress> addresses) => string.Join(',', addresses.Select(a => a.ToString()));
}

/// <summary>NET-004: eigene DNS-Server als erzeugter Tweak "NET-004/&lt;Liste&gt;".</summary>
public static class CustomDnsTweaks
{
    public const string BaseId = "NET-004";

    public static string IdFor(IReadOnlyList<IPAddress> servers) => $"{BaseId}/{DnsServers.Normalize(servers)}";

    public static bool TryCreate(string id, out TweakDefinition tweak)
    {
        tweak = null!;
        if (!id.StartsWith(BaseId + "/", StringComparison.Ordinal))
        {
            return false;
        }

        var value = id[(BaseId.Length + 1)..];
        if (DnsServers.Parse(value) is not { Count: > 0 and <= 8 } list || !list.All(DnsServers.IsUsable) || DnsServers.Normalize(list) != value)
        {
            return false;
        }

        tweak = new TweakDefinition
        {
            Id = id,
            Category = "Network.Dns",
            Impact = ImpactLevel.Small,
            ImpactNoteKey = $"tweak.{BaseId}.impact",
            PlainTitleKey = $"tweak.{BaseId}.plain.title",
            PlainDescKey = $"tweak.{BaseId}.plain.desc",
            NameKey = $"tweak.{BaseId}.name",
            DescriptionKey = $"tweak.{BaseId}.desc",
            SideEffectsKey = $"tweak.{BaseId}.side",
            Risk = RiskLevel.Caution,
            Evidence = EvidenceLevel.Proven,
            Builds = [new BuildRange { Min = 19045 }],
            Editions = [WindowsEdition.Home, WindowsEdition.Pro, WindowsEdition.Enterprise, WindowsEdition.Education],
            Scope = TweakScope.Machine,
            RequiresAdmin = true,
            RequiresReboot = false,
            Detect = new CommandQueryEqualsCondition { Command = "dns.servers", Value = value },
            Apply = [new CommandAction { Command = "dns.servers", Value = value }],
            Revert = RevertSpec.RestoreFromBackup,
            Sources = ["Microsoft Learn: 'netsh interface ip' (set dnsservers, add dnsservers). Custom addresses are checked with IPAddress.TryParse; loopback, multicast and broadcast are blocked, private addresses only after confirmation."],
        };
        return true;
    }
}
