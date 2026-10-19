using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Tests;

public sealed class ConstructionSolutionOptimizerTests
{
    [Fact]
    public void ExistingSegment_RemovesRedundantInfiniteCarrierStep()
    {
        var a = new ConstructionSearchPoint("A", new Point2(0, 0), "A");
        var c = new ConstructionSearchPoint("C", new Point2(10, 0), "C");
        var step = new ConstructionStep(
            "drawn:AC",
            ConstructionToolKind.StraightLine,
            a,
            c,
            "过 A、C 作直线");
        var targetPoint = new ConstructionSearchPoint(
            "N",
            new Point2(5, 0),
            ParentElementA: "drawn:AC",
            ParentElementB: "grid:x:5");
        var solution = new ConstructionSolution(
            "N",
            targetPoint.Position,
            [step],
            targetPoint,
            1);
        var existingSegment = new ConstructionSearchElement(
            "known:AC",
            new GeometryResult
            {
                ObjectId = "AC",
                Type = "segment",
                IsValid = true,
                Line = new LineGeometry(a.Position, c.Position - a.Position, 0, 1)
            });

        var optimized = ConstructionSolutionOptimizer.RemoveRedundantCarrierSteps(
            solution,
            [existingSegment]);

        Assert.Empty(optimized.Steps);
        Assert.Equal("known:AC", optimized.TargetPoint.ParentElementA);
    }

    [Fact]
    public void ExistingSegment_DoesNotRemoveStepWhenRequiredPointIsOutside()
    {
        var a = new ConstructionSearchPoint("A", new Point2(0, 0), "A");
        var c = new ConstructionSearchPoint("C", new Point2(10, 0), "C");
        var step = new ConstructionStep(
            "drawn:AC",
            ConstructionToolKind.StraightLine,
            a,
            c,
            "过 A、C 作直线");
        var targetPoint = new ConstructionSearchPoint(
            "N",
            new Point2(12, 0),
            ParentElementA: "drawn:AC",
            ParentElementB: "grid:x:12");
        var solution = new ConstructionSolution(
            "N",
            targetPoint.Position,
            [step],
            targetPoint,
            1);
        var existingSegment = new ConstructionSearchElement(
            "known:AC",
            new GeometryResult
            {
                ObjectId = "AC",
                Type = "segment",
                IsValid = true,
                Line = new LineGeometry(a.Position, c.Position - a.Position, 0, 1)
            });

        var optimized = ConstructionSolutionOptimizer.RemoveRedundantCarrierSteps(
            solution,
            [existingSegment]);

        Assert.Single(optimized.Steps);
        Assert.Equal("drawn:AC", optimized.TargetPoint.ParentElementA);
    }

    [Fact]
    public void UnusedDuplicateCarrierStep_IsRemovedEvenWithoutDependentPoint()
    {
        var a = new ConstructionSearchPoint("A", new Point2(1, 1), "A");
        var c = new ConstructionSearchPoint("C", new Point2(7, 4d / 3), "C");
        var redundant = new ConstructionStep(
            "step:2",
            ConstructionToolKind.StraightLine,
            a,
            c,
            "过 A、C 作直线");
        var finalA = new ConstructionSearchPoint("Q", new Point2(0, 2));
        var finalB = new ConstructionSearchPoint("R", new Point2(4, 3));
        var final = new ConstructionStep(
            "step:3",
            ConstructionToolKind.StraightLine,
            finalA,
            finalB,
            "作最后载线");
        var target = new ConstructionSearchPoint(
            "N",
            new Point2(3.5, 1.2083333333333333),
            ParentElementA: "step:3",
            ParentElementB: "known:AC");
        var solution = new ConstructionSolution(
            "N",
            target.Position,
            [redundant, final],
            target,
            1);
        var existingSegment = new ConstructionSearchElement(
            "known:AC",
            new GeometryResult
            {
                ObjectId = "AC",
                Type = "segment",
                IsValid = true,
                Line = new LineGeometry(a.Position, c.Position - a.Position, 0, 1)
            });

        var optimized = ConstructionSolutionOptimizer.RemoveRedundantCarrierSteps(
            solution,
            [existingSegment]);

        var remaining = Assert.Single(optimized.Steps);
        Assert.Equal("step:3", remaining.Key);
        Assert.Equal("known:AC", optimized.TargetPoint.ParentElementB);
    }
}
