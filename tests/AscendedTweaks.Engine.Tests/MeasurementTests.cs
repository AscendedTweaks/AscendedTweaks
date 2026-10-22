using System.Globalization;
using System.Text;
using AscendedTweaks.Tools.PerfMeasure;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.Engine.Tests;

public sealed class MeasurementTests
{
    [Fact]
    public void PresentMon_csv_gives_average_and_one_percent_low()
    {
        var csv = new StringBuilder("Application,ProcessID,MsBetweenPresents\n");
        for (var i = 0; i < 99; i++)
        {
            csv.AppendLine("spiel.exe,1,10.0");
        }

        csv.AppendLine("spiel.exe,1,20.0");

        var stats = FrameStats.FromPresentMonCsv(csv.ToString());

        stats.Frames.Should().Be(100);
        stats.AverageFps.Should().BeApproximately(1000 / 10.1, 0.001);
        stats.OnePercentLowFps.Should().BeApproximately(50, 0.001);
    }

    [Fact]
    public void PresentMon_two_uses_frame_time_column_and_rejects_short_runs()
    {
        var csv = "Application,FrameTime\n" + string.Concat(Enumerable.Repeat("a.exe,5\n", 120));
        FrameStats.FromPresentMonCsv(csv).AverageFps.Should().BeApproximately(200, 0.001);

        ((Action)(() => FrameStats.FromPresentMonCsv("Application,FrameTime\na.exe,5\n"))).Should().Throw<InvalidDataException>();
        ((Action)(() => FrameStats.FromPresentMonCsv("Application,Andere\n" + string.Concat(Enumerable.Repeat("a,5\n", 200))))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(new[] { 100.0, 102, 101, 140, 99 }, 101)]
    [InlineData(new[] { 1.0, 2, 3, 4 }, 2.5)]
    public void Median_is_robust_against_outliers(double[] values, double expected)
    {
        Measurement.MedianOf(values).Should().Be(expected);
    }

    [Theory]
    [InlineData("fps", 100, 103, 5, "Proven")]
    [InlineData("fps", 100, 101, 5, "Minor")]
    [InlineData("fps", 100, 99, 5, "Disputed")]
    [InlineData("fps", 100, 110, 3, "Disputed")]
    [InlineData("s", 30, 28, 5, "Proven")]
    [InlineData("s", 30, 31, 5, "Disputed")]
    public void Comparison_follows_the_measurement_protocol(string unit, double before, double after, int runs, string evidence)
    {
        var a = Measurement.FromRuns("vorher", unit, Enumerable.Repeat(before, runs).ToList(), null);
        var b = Measurement.FromRuns("nachher", unit, Enumerable.Repeat(after, runs).ToList(), null);

        Measurement.Compare(a, b).SuggestedEvidence.Should().Be(evidence);
    }

    [Fact]
    public void Values_are_parsed_culture_independent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Measurement.FromRuns("x", "s", [1.5, 2.5, 3.5], null).Median.Should().Be(2.5);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
