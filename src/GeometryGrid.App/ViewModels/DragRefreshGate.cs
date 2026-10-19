using System.Diagnostics;

namespace GeometryGrid.App.ViewModels;

internal sealed class DragRefreshGate(TimeSpan interval)
{
    private readonly long _intervalTicks = System.Math.Max(
        1,
        (long)System.Math.Ceiling(interval.TotalSeconds * Stopwatch.Frequency));
    private long _lastRun;

    public bool ShouldRun(bool force = false) =>
        ShouldRunAt(Stopwatch.GetTimestamp(), force);

    internal bool ShouldRunAt(long timestamp, bool force = false)
    {
        if (force || _lastRun == 0 || timestamp - _lastRun >= _intervalTicks)
        {
            _lastRun = timestamp;
            return true;
        }
        return false;
    }

    public void Reset() => _lastRun = 0;
}
