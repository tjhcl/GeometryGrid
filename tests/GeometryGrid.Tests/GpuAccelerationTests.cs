using GeometryGrid.App.Services;
using GeometryGrid.Core.Geometry;
using Xunit.Abstractions;

namespace GeometryGrid.Tests;

public sealed class GpuAccelerationTests(ITestOutputHelper output)
{
    [Fact]
    public void GuidedSearch_UsesCandidateAcceleratorForLargePointSets()
    {
        var accelerator = new RecordingAccelerator();
        var points = Enumerable.Range(0, 40)
            .Select(index => new ConstructionSearchPoint(
                $"P{index}",
                new Point2(index % 8, index / 8.0 + index * .001)))
            .ToList();

        _ = new ConstructionSearchEngine().Search(
            points,
            [],
            "T",
            new Point2(3.125, 2.75),
            new ConstructionSearchOptions(
                MaxSteps: 4,
                MaxSolutions: 1,
                MaxStates: 100,
                UseStraightedge: true,
                UseCompass: false,
                UseCollinearityClosure: false,
                UseGuidedSearch: true,
                UseExhaustiveSearch: false,
                CandidateAccelerator: accelerator));

        Assert.True(accelerator.CallCount > 0);
    }

    [Fact]
    public void HardwareGpu_RanksEveryDistinctPointPair_WhenAvailable()
    {
        if (!GpuCandidateAccelerator.TryCreate(
                out var accelerator,
                out var status))
        {
            output.WriteLine($"GPU test skipped: {status}");
            return;
        }

        var points = Enumerable.Range(0, 256)
            .Select(index => new ConstructionSearchPoint(
                $"P{index}",
                new Point2(
                    Math.Cos(index * Math.PI * 2 / 256) * (8 + index * .002),
                    Math.Sin(index * Math.PI * 2 / 256) * (8 + index * .002))))
            .ToList();
        var targets = new[]
        {
            new ConstructionSearchTarget("T1", new Point2(1.25, 2.5)),
            new ConstructionSearchTarget("T2", new Point2(-3.5, 4.25)),
            new ConstructionSearchTarget("T3", new Point2(6.75, -1.5))
        };

        var ranked = accelerator!.RankPointPairs(
            points,
            targets,
            1e-7,
            CancellationToken.None);

        Assert.Equal(256 * 255 / 2, ranked.Count);
        Assert.All(ranked, pair => Assert.True(pair.SecondIndex > pair.FirstIndex));
        output.WriteLine(
            $"{accelerator.DeviceName}: {accelerator.LastPairsPerSecond:N0} candidate pairs/s");
    }

    private sealed class RecordingAccelerator :
        IConstructionCandidateAccelerator
    {
        public int CallCount { get; private set; }
        public string DeviceName => "test";
        public bool IsHardwareAccelerated => true;
        public long TotalPairsProcessed { get; private set; }
        public double LastPairsPerSecond => 1;

        public IReadOnlyList<ConstructionPointPairRank> RankPointPairs(
            IReadOnlyList<ConstructionSearchPoint> points,
            IReadOnlyList<ConstructionSearchTarget> targets,
            double tolerance,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var pairs = new List<ConstructionPointPairRank>();
            for (var first = 0; first < points.Count; first++)
            for (var second = first + 1; second < points.Count; second++)
                pairs.Add(new ConstructionPointPairRank(
                    first,
                    second,
                    0,
                    float.PositiveInfinity));
            TotalPairsProcessed += pairs.Count;
            return pairs;
        }
    }
}
