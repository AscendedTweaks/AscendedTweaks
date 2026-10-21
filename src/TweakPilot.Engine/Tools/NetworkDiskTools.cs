using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TweakPilot.Core;

namespace TweakPilot.Engine.Tools;

public sealed record PingStats(string Target, int Sent, int Received, double? MinMs, double? MedianMs, double? MaxMs, double? JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : (Sent - Received) * 100.0 / Sent;
}

public interface IPinger
{
    /// <summary>Antwortzeit in Millisekunden oder null bei Zeitueberschreitung.</summary>
    Task<double?> PingAsync(IPAddress target, TimeSpan timeout, CancellationToken cancellation);
}

/// <summary>NET-007: Ping- und Jitter-Test (Abschnitt 6.7): 50 Pakete im Abstand von 200 ms.</summary>
public sealed class PingTest
{
    public const int Count = 50;
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);
    public static readonly IReadOnlyList<string> DefaultTargets = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    private readonly IPinger _pinger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public PingTest(IPinger pinger, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _pinger = pinger;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Jitter = mittlere absolute Abweichung aufeinanderfolgender Antwortzeiten (nur beantwortete Pakete).</summary>
    public static PingStats Summarize(string target, IReadOnlyList<double?> replies)
    {
        ArgumentNullException.ThrowIfNull(replies);
        var ok = replies.Where(r => r is not null).Select(r => r!.Value).ToList();
        if (ok.Count == 0)
        {
            return new PingStats(target, replies.Count, 0, null, null, null, null);
        }

        var sorted = ok.Order().ToList();
        var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
        double? jitter = ok.Count < 2 ? null : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new PingStats(target, replies.Count, ok.Count, sorted[0], median, sorted[^1], jitter);
    }

    public async Task<PingStats> RunAsync(string target, IProgress<int>? progress, CancellationToken cancellation)
    {
        if (DnsServers.Parse(target) is not [var address] || !DnsServers.IsUsable(address))
        {
            throw new TweakPilotException(ErrorKind.General, $"Invalid target {target}.");
        }

        var replies = new List<double?>();
        for (var i = 0; i < Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            replies.Add(await _pinger.PingAsync(address, TimeSpan.FromSeconds(1), cancellation).ConfigureAwait(false));
            progress?.Report(i + 1);
            if (i < Count - 1)
            {
                await _delay(Interval, cancellation).ConfigureAwait(false);
            }
        }

        return Summarize(target, replies);
    }
}

public enum MediaKind
{
    Unknown,
    Hdd,
    Ssd,
}

public sealed record VolumeInfo(char Drive, MediaKind Media, long SizeBytes, long FreeBytes, string? Model);

public interface IVolumeInventory
{
    IReadOnlyList<VolumeInfo> FixedVolumes();
}

public enum MaintenanceAction
{
    /// <summary>NET-008: netsh winsock reset und netsh int ip reset; Neustart noetig.</summary>
    NetworkReset,

    /// <summary>DISK-002: defrag /L (ReTrim), nur SSD.</summary>
    ReTrim,

    /// <summary>DISK-006: klassische Defragmentierung, nur HDD; auf SSD gesperrt.</summary>
    Defrag,
}

public sealed record MaintenanceResult(bool Ok, bool RebootRequired, string? Message);

/// <summary>Wartungsfunktionen im Helfer. Feste Aktionen, Laufwerk nur als Buchstabe eines festen Laufwerks.</summary>
public sealed class Maintenance
{
    private readonly ICommandRunner _runner;
    private readonly IVolumeInventory _volumes;
    private readonly string _systemDirectory;

    public Maintenance(ICommandRunner runner, IVolumeInventory volumes, string systemDirectory)
    {
        _runner = runner;
        _volumes = volumes;
        _systemDirectory = systemDirectory;
    }

    public MaintenanceResult Run(MaintenanceAction action, char? drive)
    {
        switch (action)
        {
            case MaintenanceAction.NetworkReset:
                var winsock = Exec("netsh.exe", ["winsock", "reset"], TimeSpan.FromMinutes(2));
                var ip = Exec("netsh.exe", ["interface", "ip", "reset"], TimeSpan.FromMinutes(2));
                return new MaintenanceResult(winsock && ip, RebootRequired: true, winsock && ip ? null : "netsh reported an error.");

            case MaintenanceAction.ReTrim:
                var ssd = RequireVolume(drive);
                if (ssd.Media != MediaKind.Ssd)
                {
                    throw new TweakPilotException(ErrorKind.General, $"Drive {ssd.Drive}: is not an SSD; ReTrim is only meant for SSDs.");
                }

                return new MaintenanceResult(Exec("defrag.exe", [$"{ssd.Drive}:", "/L"], TimeSpan.FromMinutes(60)), false, null);

            case MaintenanceAction.Defrag:
                var hdd = RequireVolume(drive);
                if (hdd.Media != MediaKind.Hdd)
                {
                    // Auch "unbekannt" ist gesperrt: im Zweifel nie eine SSD defragmentieren (Abschnitt 6.8).
                    throw new TweakPilotException(ErrorKind.General, $"Drive {hdd.Drive}: is not a detected hard disk (HDD); defragmenting is locked.");
                }

                return new MaintenanceResult(Exec("defrag.exe", [$"{hdd.Drive}:", "/D"], TimeSpan.FromHours(4)), false, null);

            default:
                throw new TweakPilotException(ErrorKind.General, $"Unknown maintenance action {action}.");
        }
    }

    private VolumeInfo RequireVolume(char? drive)
    {
        if (drive is not { } d || !char.IsAsciiLetterUpper(d))
        {
            throw new TweakPilotException(ErrorKind.General, "Invalid drive.");
        }

        return _volumes.FixedVolumes().FirstOrDefault(v => v.Drive == d)
            ?? throw new TweakPilotException(ErrorKind.General, $"Drive {d}: is not a fixed drive.");
    }

    private bool Exec(string executable, IReadOnlyList<string> arguments, TimeSpan timeout) =>
        _runner.Run(Path.Combine(_systemDirectory, executable), arguments, timeout).ExitCode == 0;
}

public sealed record DiskHealth(string Model, MediaKind Media, int? TemperatureCelsius, int? WearPercent, long? PowerOnHours, long? ReadErrors, bool? FailurePredicted);

public interface IDiskHealthSource
{
    IReadOnlyList<DiskHealth> Read();
}

public sealed record LargeFile(string Path, long SizeBytes, DateTimeOffset LastWriteUtc);

/// <summary>DISK-005: grosse Dateien finden (nur lesend, Abzweigungen werden nicht verfolgt, der Windows-Ordner wird ausgelassen).</summary>
public sealed class LargeFileFinder
{
    public const long Threshold = 500L * 1024 * 1024;
    public const int MaxEntries = 2_000_000;
    public const int MaxResults = 200;

    private readonly Cleanup.ICleanupFileSystem _files;
    private readonly IReadOnlyList<string> _skip;

    public LargeFileFinder(Cleanup.ICleanupFileSystem files, IReadOnlyList<string> skipFolders)
    {
        _files = files;
        _skip = skipFolders;
    }

    public (IReadOnlyList<LargeFile> Files, bool Truncated) Find(IReadOnlyList<string> roots, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var found = new List<LargeFile>();
        var seen = 0;
        var stack = new Stack<string>(roots.Where(_files.DirectoryExists));
        while (stack.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            IEnumerable<Cleanup.FsEntry> entries;
            try
            {
                entries = _files.List(directory).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (++seen > MaxEntries)
                {
                    return (Top(found), true);
                }

                if (entry.IsReparsePoint)
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    if (!_skip.Any(s => string.Equals(s, entry.FullPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        stack.Push(entry.FullPath);
                    }
                }
                else if (entry.Size >= Threshold)
                {
                    found.Add(new LargeFile(entry.FullPath, entry.Size, entry.LastWriteUtc));
                }
            }
        }

        return (Top(found), false);
    }

    private static IReadOnlyList<LargeFile> Top(IEnumerable<LargeFile> files) => files.OrderByDescending(f => f.SizeBytes).Take(MaxResults).ToList();
}

public sealed record DohStatus(bool Supported, IReadOnlyList<string> CurrentServers, IReadOnlyList<string> ServersWithDoh);

/// <summary>NET-005: DoH nur mit Funktionserkennung (bekannte DoH-Server in der Registry), sonst "nicht verfuegbar".</summary>
public static partial class DohCheck
{
    public static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cloudflare"] = "https://cloudflare-dns.com/dns-query",
        ["google"] = "https://dns.google/dns-query",
        ["quad9"] = "https://dns.quad9.net/dns-query",
    };

    /// <summary>Nur https mit gueltigem Hostnamen (Abschnitt 9.4).</summary>
    public static bool IsValidTemplate(string template) =>
        Uri.TryCreate(template, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && HostPattern().IsMatch(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    public static DohStatus Evaluate(IReadOnlyCollection<string> wellKnownServers, IReadOnlyList<string> currentServers)
    {
        ArgumentNullException.ThrowIfNull(wellKnownServers);
        var supported = wellKnownServers.Count > 0;
        return new DohStatus(supported, currentServers, currentServers.Where(s => wellKnownServers.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList());
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9\-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HostPattern();
}

public static class Formatting
{
    public static string Ms(double? value) => value is { } v ? v.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "-";
}
