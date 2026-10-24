using AscendedTweaks.App.Controls;
using FluentAssertions;
using Xunit;

namespace AscendedTweaks.App.Tests;

public sealed class HoldGestureTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Holding_fills_up_and_completes_once_after_the_hold_time()
    {
        var hold = new HoldGesture(TimeSpan.FromSeconds(1.2));
        hold.Start(T0);

        hold.Progress(T0.AddSeconds(0.6), out var halfway).Should().BeApproximately(0.5, 0.001);
        halfway.Should().BeFalse();
        hold.Progress(T0.AddSeconds(1.3), out var done).Should().Be(1);
        done.Should().BeTrue();
        hold.IsHolding.Should().BeFalse("it completes once");
        hold.Progress(T0.AddSeconds(2), out var again).Should().Be(0);
        again.Should().BeFalse();
    }

    [Fact]
    public void Letting_go_early_does_nothing()
    {
        var hold = new HoldGesture(TimeSpan.FromSeconds(1.2));
        hold.Start(T0);
        hold.Progress(T0.AddSeconds(1), out _);

        hold.Cancel();

        hold.Progress(T0.AddSeconds(5), out var done).Should().Be(0);
        done.Should().BeFalse();
    }
}
