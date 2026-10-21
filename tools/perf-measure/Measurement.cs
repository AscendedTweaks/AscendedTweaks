using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TweakPilot.Tools.PerfMeasure;

/// <summary>Kennzahlen eines Laufs aus einer PresentMon-Aufzeichnung.</summary>
internal sealed record FrameStats(int Frames, double AverageFps, double OnePercentLowFps)
{
    /// <summary>
    /// Liest die Bildzeiten aus einer PresentMon-CSV. Unterstuetzt "MsBetweenPresents" (PresentMon 1.x) und "FrameTime" (2.x).
    /// Durchschnitts-FPS = 1000 / mittlere Bildzeit; 1-%-Tief = 1000 / mittlere Bildzeit der langsamsten 1 Prozent.
    /// </summary>
    public static FrameStats FromPresentMonCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            throw new InvalidDataException("CSV enthaelt keine Bilder.");
        }

        var header = lines[0].Split(',');
        var column = Array.FindIndex(header, h => h.Trim('"') == "MsBetweenPresents");
        if (column < 0)
        {
            column = Array.FindIndex(header, h => h.Trim('"') == "FrameTime");
        }

        if (column < 0)
        {
            throw new InvalidDataException("Spalte MsBetweenPresents oder FrameTime fehlt.");
        }

        var times = new List<double>();
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(',');
            if (cells.Length > column && double.TryParse(cells[column].Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0)
            {
                times.Add(ms);
            }
        }

        if (times.Count < 100)
        {
            throw new InvalidDataException($"Zu wenige Bilder ({times.Count}); mindestens 100 noetig.");
        }

        // 1-%-Tief: mittlere Bildzeit der langsamsten 1 Prozent aller Bilder (mindestens ein Bild).
        var slowest = times.OrderDescending().Take(Math.Max(1, times.Count / 100)).Average();
        return new FrameStats(times.Count, 1000 / times.Average(), 1000 / slowest);
    }
}

/// <summary>Ergebnis einer Messreihe: Median ueber die Laeufe (Abschnitt 6: 5 Laeufe).</summary>
internal sealed record Measurement(string Label, string Unit, int Runs, double Median, double? MedianLow, IReadOnlyList<double> Values, bool ProtocolComplete)
{
    public const int RequiredRuns = 5;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static Measurement FromRuns(string label, string unit, IReadOnlyList<double> values, IReadOnlyList<double>? lows)
    {
        if (values.Count == 0)
        {
            throw new InvalidDataException("Keine Messwerte.");
        }

        return new Measurement(label, unit, values.Count, MedianOf(values), lows is null ? null : MedianOf(lows), values, values.Count >= RequiredRuns);
    }

    public static double MedianOf(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>
    /// Einstufung nach Abschnitt 6: unter 2 Prozent Verbesserung hoechstens "Gering"; ohne vollstaendiges Protokoll
    /// (5 Laeufe je Seite) bleibt es "Umstritten". "Hoeher ist besser" gilt fuer fps, "niedriger ist besser" fuer Zeiten.
    /// </summary>
    public static Comparison Compare(Measurement before, Measurement after)
    {
        var higherIsBetter = string.Equals(before.Unit, "fps", StringComparison.OrdinalIgnoreCase);
        var change = (after.Median - before.Median) / before.Median * 100;
        var improvement = higherIsBetter ? change : -change;
        var evidence = !before.ProtocolComplete || !after.ProtocolComplete
            ? "Disputed"
            : improvement >= 2 ? "Proven" : improvement > 0 ? "Minor" : "Disputed";
        return new Comparison(before.Label, after.Label, Math.Round(change, 2), Math.Round(improvement, 2), evidence);
    }
}

internal sealed record Comparison(string Before, string After, double ChangePercent, double ImprovementPercent, string SuggestedEvidence);
