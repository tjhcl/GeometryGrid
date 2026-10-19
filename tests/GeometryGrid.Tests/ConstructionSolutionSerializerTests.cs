using System.IO;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Tests;

public sealed class ConstructionSolutionSerializerTests
{
    [Fact]
    public void Package_RoundTripsConstructionProvenance()
    {
        var a = new ConstructionSearchPoint(
            "known-point:A",
            new Point2(0, 0),
            "A");
        var b = new ConstructionSearchPoint(
            "known-point:B",
            new Point2(1, 1),
            "B");
        var targetPoint = new ConstructionSearchPoint(
            "intersection",
            new Point2(.5, .5),
            ParentElementA: "step:0",
            ParentElementB: "known:x",
            Selector: "first");
        var solution = new ConstructionSolution(
            "P",
            targetPoint.Position,
            [
                new ConstructionStep(
                    "step:0",
                    ConstructionToolKind.StraightLine,
                    a,
                    b,
                    "过 A、B 作直线")
            ],
            targetPoint,
            123);
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.ggsteps.json");
        try
        {
            ConstructionSolutionSerializer.Save(
                new ConstructionSolutionPackage
                {
                    SourceProject = "test.json",
                    Strategy = "test",
                    OptimalityProven = true,
                    Solutions = [solution]
                },
                path);
            var loaded = ConstructionSolutionSerializer.Load(path);

            Assert.Single(loaded.Solutions);
            Assert.Equal(
                ConstructionToolKind.StraightLine,
                loaded.Solutions[0].Steps[0].Tool);
            Assert.Equal(
                "known-point:A",
                loaded.Solutions[0].Steps[0].PointA.Key);
            Assert.Equal(
                "known:x",
                loaded.Solutions[0].TargetPoint.ParentElementB);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
