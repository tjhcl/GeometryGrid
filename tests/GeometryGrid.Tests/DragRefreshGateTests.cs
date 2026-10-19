using System.Diagnostics;
using GeometryGrid.App.ViewModels;

namespace GeometryGrid.Tests;

public sealed class DragRefreshGateTests
{
    [Fact]
    public void Gate_ThrottlesHeavyPlaybackRefreshButAlwaysAllowsFinalFrame()
    {
        var gate = new DragRefreshGate(TimeSpan.FromMilliseconds(80));
        var start = Stopwatch.Frequency;
        var twentyMilliseconds =
            (long)System.Math.Round(Stopwatch.Frequency * .020);

        Assert.True(gate.ShouldRunAt(start));
        Assert.False(gate.ShouldRunAt(start + twentyMilliseconds));
        Assert.False(gate.ShouldRunAt(start + twentyMilliseconds * 2));
        Assert.False(gate.ShouldRunAt(start + twentyMilliseconds * 3));
        Assert.True(gate.ShouldRunAt(start + twentyMilliseconds * 4));
        Assert.True(gate.ShouldRunAt(
            start + twentyMilliseconds * 5,
            force: true));
    }

    [Fact]
    public void Reset_AllowsImmediateRefreshForNextDrag()
    {
        var gate = new DragRefreshGate(TimeSpan.FromSeconds(1));
        var start = Stopwatch.Frequency;

        Assert.True(gate.ShouldRunAt(start));
        Assert.False(gate.ShouldRunAt(start + 1));
        gate.Reset();
        Assert.True(gate.ShouldRunAt(start + 1));
    }
}
