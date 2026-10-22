using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AscendedTweaks.Engine.Insights;

/// <summary>Momentaufnahme eines Prozesses; CPU-Zeit fehlt bei geschuetzten Prozessen.</summary>
public sealed record ProcessSample(int Id, string Name, TimeSpan? CpuTime, long WorkingSetBytes);

public interface IProcessSampler
{
    IReadOnlyList<ProcessSample> Sample();
}

/// <summary>Ein Programm (alle Prozesse mit gleichem Namen zusammengefasst).</summary>
public sealed record ProcessGroup(string Name, int Count, long WorkingSetBytes, double CpuPercent);

public sealed record ProcessOverviewResult(IReadOnlyList<ProcessGroup> ByMemory, IReadOnlyList<ProcessGroup> ByCpu, long TotalWorkingSetBytes);

/// <summary>
/// Prozess- und Speicher-Uebersicht (Abschnitt 6.2): rein lesend, Top 10 nach Arbeitsspeicher und CPU.
/// Bewusst ohne "RAM bereinigen" (Abschnitt 6.12).
/// </summary>
public sealed class ProcessOverview
{
    /// <summary>Hintergrund-Bericht: die 15 groessten Verbraucher (V2 Abschnitt 5.2).</summary>
    public const int Top = 15;

    private readonly IProcessSampler _sampler;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly int _processorCount;

    public ProcessOverview(IProcessSampler sampler, Func<TimeSpan, Task>? delay = null, int? processorCount = null)
    {
        _sampler = sampler;
        _delay = delay ?? Task.Delay;
        _processorCount = processorCount ?? Environment.ProcessorCount;
    }

    /// <summary>CPU-Anteil aus zwei Messungen im Abstand <paramref name="elapsed"/>, normiert auf alle logischen Prozessoren.</summary>
    public static ProcessOverviewResult Compute(IReadOnlyList<ProcessSample> first, IReadOnlyList<ProcessSample> second, TimeSpan elapsed, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var before = first.Where(s => s.CpuTime is not null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().CpuTime!.Value);
        var budget = elapsed.TotalMilliseconds * Math.Max(1, processorCount);

        var groups = second
            .Where(s => s.Id != 0)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var cpu = g.Sum(s => s.CpuTime is { } now && before.TryGetValue(s.Id, out var then) && now >= then ? (now - then).TotalMilliseconds : 0);
                return new ProcessGroup(g.Key, g.Count(), g.Sum(s => s.WorkingSetBytes), budget > 0 ? Math.Min(100, cpu / budget * 100) : 0);
            })
            .ToList();

        return new ProcessOverviewResult(
            groups.OrderByDescending(g => g.WorkingSetBytes).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Take(Top).ToList(),
            groups.OrderByDescending(g => g.CpuPercent).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Take(Top).ToList(),
            second.Sum(s => s.WorkingSetBytes));
    }

    public async Task<ProcessOverviewResult> MeasureAsync(TimeSpan interval)
    {
        var watch = Stopwatch.StartNew();
        var first = _sampler.Sample();
        await _delay(interval).ConfigureAwait(false);
        var second = _sampler.Sample();
        return Compute(first, second, watch.Elapsed, _processorCount);
    }
}

[ExcludeFromCodeCoverage(Justification = "Reads system values only.")]
public sealed class WindowsProcessSampler : IProcessSampler
{
    public IReadOnlyList<ProcessSample> Sample()
    {
        var result = new List<ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    TimeSpan? cpu;
                    try
                    {
                        cpu = process.TotalProcessorTime;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        cpu = null;
                    }

                    result.Add(new ProcessSample(process.Id, process.ProcessName, cpu, process.WorkingSet64));
                }
                catch (InvalidOperationException)
                {
                    // Prozess wurde inzwischen beendet.
                }
            }
        }

        return result;
    }
}
