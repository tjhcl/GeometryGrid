namespace GeometryGrid.Core.Geometry;

public readonly record struct ConstructionPointPairRank(
    int FirstIndex,
    int SecondIndex,
    int TargetHits,
    float MinimumTargetDistance);

public interface IConstructionCandidateAccelerator
{
    string DeviceName { get; }
    bool IsHardwareAccelerated { get; }
    long TotalPairsProcessed { get; }
    double LastPairsPerSecond { get; }

    IReadOnlyList<ConstructionPointPairRank> RankPointPairs(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<ConstructionSearchTarget> targets,
        double tolerance,
        CancellationToken cancellationToken);
}
