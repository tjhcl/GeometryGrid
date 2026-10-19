using System.Diagnostics;
using ComputeSharp;
using GeometryGrid.Core.Geometry;

namespace GeometryGrid.App.Services;

public sealed class GpuCandidateAccelerator :
    IConstructionCandidateAccelerator
{
    private readonly GraphicsDevice _device;
    private readonly object _dispatchLock = new();
    private long _totalPairsProcessed;
    private double _lastPairsPerSecond;

    private GpuCandidateAccelerator(GraphicsDevice device)
    {
        _device = device;
    }

    public string DeviceName => _device.Name;
    public bool IsHardwareAccelerated => _device.IsHardwareAccelerated;
    public long TotalPairsProcessed =>
        Interlocked.Read(ref _totalPairsProcessed);
    public double LastPairsPerSecond =>
        Volatile.Read(ref _lastPairsPerSecond);

    public static bool TryCreate(
        out GpuCandidateAccelerator? accelerator,
        out string status)
    {
        try
        {
            var device = GraphicsDevice.GetDefault();
            if (!device.IsHardwareAccelerated)
            {
                accelerator = null;
                status = "未找到可用的 DirectX 12 硬件 GPU；不会使用 WARP CPU 模拟。";
                return false;
            }
            accelerator = new GpuCandidateAccelerator(device);
            status = $"可用：{device.Name}";
            return true;
        }
        catch (Exception ex)
        {
            accelerator = null;
            status = $"GPU 初始化失败：{ex.Message}";
            return false;
        }
    }

    public IReadOnlyList<ConstructionPointPairRank> RankPointPairs(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<ConstructionSearchTarget> targets,
        double tolerance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (points.Count < 2)
            return [];

        var pointData = points
            .Select(point => new Float2(
                (float)point.Position.X,
                (float)point.Position.Y))
            .ToArray();
        var targetData = targets
            .Select(target => new Float2(
                (float)target.Position.X,
                (float)target.Position.Y))
            .ToArray();
        if (targetData.Length == 0)
            return CpuPairOrder(points.Count);

        var outputLength = checked(points.Count * points.Count);
        var output = new Float4[outputLength];
        var stopwatch = Stopwatch.StartNew();
        lock (_dispatchLock)
        {
            using var pointBuffer =
                _device.AllocateReadOnlyBuffer(pointData);
            using var targetBuffer =
                _device.AllocateReadOnlyBuffer(targetData);
            using var outputBuffer =
                _device.AllocateReadWriteBuffer<Float4>(outputLength);
            _device.For(
                points.Count,
                points.Count,
                new RankPointPairsShader(
                    pointBuffer,
                    targetBuffer,
                    outputBuffer,
                    points.Count,
                    targetData.Length,
                    (float)System.Math.Max(tolerance * 8, 1e-5)));
            outputBuffer.CopyTo(output);
        }
        stopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();

        var pairs = new List<ConstructionPointPairRank>(
            points.Count * (points.Count - 1) / 2);
        foreach (var score in output)
        {
            var first = (int)score.X;
            var second = (int)score.Y;
            if (first < 0 || second <= first || second >= points.Count)
                continue;
            pairs.Add(new ConstructionPointPairRank(
                first,
                second,
                (int)score.Z,
                score.W));
        }

        pairs.Sort(static (left, right) =>
        {
            var byHits = right.TargetHits.CompareTo(left.TargetHits);
            if (byHits != 0) return byHits;
            var byDistance = left.MinimumTargetDistance.CompareTo(
                right.MinimumTargetDistance);
            if (byDistance != 0) return byDistance;
            var byFirst = left.FirstIndex.CompareTo(right.FirstIndex);
            return byFirst != 0
                ? byFirst
                : left.SecondIndex.CompareTo(right.SecondIndex);
        });

        Interlocked.Add(ref _totalPairsProcessed, pairs.Count);
        Volatile.Write(
            ref _lastPairsPerSecond,
            pairs.Count / System.Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-9));
        return pairs;
    }

    public double RunBenchmark()
    {
        var points = Enumerable.Range(0, 256)
            .Select(index =>
            {
                var angle = index * System.Math.PI * 2 / 256;
                var radius = 10 + index % 17 * .13;
                return new ConstructionSearchPoint(
                    $"benchmark:{index}",
                    new Point2(
                        System.Math.Cos(angle) * radius,
                        System.Math.Sin(angle) * radius));
            })
            .ToList();
        var targets = new[]
        {
            new ConstructionSearchTarget("T1", new Point2(1.25, 2.5)),
            new ConstructionSearchTarget("T2", new Point2(-3.5, 4.25)),
            new ConstructionSearchTarget("T3", new Point2(6.75, -1.5))
        };

        _ = RankPointPairs(
            points,
            targets,
            1e-7,
            CancellationToken.None);
        return LastPairsPerSecond;
    }

    private static IReadOnlyList<ConstructionPointPairRank> CpuPairOrder(
        int pointCount)
    {
        var pairs = new List<ConstructionPointPairRank>(
            pointCount * (pointCount - 1) / 2);
        for (var first = 0; first < pointCount; first++)
        for (var second = first + 1; second < pointCount; second++)
            pairs.Add(new ConstructionPointPairRank(
                first,
                second,
                0,
                float.PositiveInfinity));
        return pairs;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RankPointPairsShader(
    ReadOnlyBuffer<Float2> points,
    ReadOnlyBuffer<Float2> targets,
    ReadWriteBuffer<Float4> output,
    int pointCount,
    int targetCount,
    float tolerance) : IComputeShader
{
    public void Execute()
    {
        var first = ThreadIds.X;
        var second = ThreadIds.Y;
        var outputIndex = first * pointCount + second;
        if (second <= first)
        {
            output[outputIndex] = new Float4(-1, -1, 0, 0);
            return;
        }

        var a = points[first];
        var b = points[second];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Hlsl.Sqrt(dx * dx + dy * dy);
        if (length <= tolerance)
        {
            output[outputIndex] = new Float4(-1, -1, 0, 0);
            return;
        }

        var hits = 0;
        var minimumDistance = 3.402823466e+38f;
        for (var index = 0; index < targetCount; index++)
        {
            var target = targets[index];
            var cross =
                dx * (target.Y - a.Y) -
                dy * (target.X - a.X);
            var distance = Hlsl.Abs(cross) / length;
            minimumDistance = Hlsl.Min(minimumDistance, distance);
            if (distance <= tolerance)
                hits++;
        }
        output[outputIndex] = new Float4(
            first,
            second,
            hits,
            minimumDistance);
    }
}
