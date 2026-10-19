using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Tests;

public sealed class DrawingV11Tests
{
    [Fact]
    public void PerpendicularLine_PassesThroughPointAndIsPerpendicular()
    {
        var document = Document(
            Point("A", 0, 0),
            Point("B", 4, 0),
            Point("P", 2, 3),
            new GeometryObjectDefinition { Id = "AB", Type = "lineThroughPoints", PointA = "A", PointB = "B" },
            new GeometryObjectDefinition { Id = "perp", Type = "perpendicularLine", PointA = "P", ObjectA = "AB" });

        var results = new GeometryEngine().Evaluate(document).Results;
        var source = results["AB"].Line!;
        var perpendicular = results["perp"].Line!;
        Assert.Equal(0, Vector2.Dot(source.Direction, perpendicular.Direction), 10);
        MathAndDependencyTests.AssertClose(results["P"].Point!.Value, perpendicular.Origin);
    }

    [Fact]
    public void ParallelLine_PassesThroughPointAndKeepsSourceDirection()
    {
        var document = Document(
            Point("A", 0, 0),
            Point("B", 4, 2),
            Point("P", 1, 3),
            new GeometryObjectDefinition { Id = "AB", Type = "lineThroughPoints", PointA = "A", PointB = "B" },
            new GeometryObjectDefinition { Id = "parallel", Type = "parallelLine", PointA = "P", ObjectA = "AB" });

        var results = new GeometryEngine().Evaluate(document).Results;
        var source = results["AB"].Line!;
        var parallel = results["parallel"].Line!;
        Assert.Equal(0, Vector2.Cross(source.Direction, parallel.Direction), 10);
        MathAndDependencyTests.AssertClose(results["P"].Point!.Value, parallel.Origin);
    }

    [Theory]
    [InlineData("perpendicularLine", "x", 1, 0)]
    [InlineData("parallelLine", "y", 1, 0)]
    public void PointLineRelation_CanUseBackgroundGridLine(
        string type,
        string gridAxis,
        double expectedDirectionX,
        double expectedDirectionY)
    {
        var document = Document(
            Point("P", 2, 3),
            new GeometryObjectDefinition
            {
                Id = "result",
                Type = type,
                PointA = "P",
                GridAxis = gridAxis,
                GridValue = "1"
            });

        var line = new GeometryEngine().Evaluate(document).Results["result"].Line!;
        MathAndDependencyTests.AssertClose(new Point2(2, 3), line.Origin);
        Assert.Equal(0, Vector2.Cross(line.Direction, new Vector2(expectedDirectionX, expectedDirectionY)), 10);
    }

    [Fact]
    public void CircleByCenterPoint_UsesSegmentLengthAsRadius()
    {
        var document = Document(
            Point("O", 1, 1),
            Point("A", 4, 5),
            new GeometryObjectDefinition { Id = "circle", Type = "circleByCenterPoint", Center = "O", PointA = "A" });

        var circle = new GeometryEngine().Evaluate(document).Results["circle"].Circle!;
        MathAndDependencyTests.AssertClose(new Point2(1, 1), circle.Center);
        Assert.Equal(5, circle.Radius, 10);
    }

    [Fact]
    public void CircleByThreePoints_CalculatesCircumcircle()
    {
        var document = Document(
            Point("A", 1, 0),
            Point("B", 0, 1),
            Point("C", -1, 0),
            new GeometryObjectDefinition
            {
                Id = "circle",
                Type = "circleByThreePoints",
                PointA = "A",
                PointB = "B",
                PointC = "C"
            });

        var circle = new GeometryEngine().Evaluate(document).Results["circle"].Circle!;
        MathAndDependencyTests.AssertClose(new Point2(0, 0), circle.Center);
        Assert.Equal(1, circle.Radius, 10);
    }

    [Fact]
    public void GridIntersection_UsesGridDefinitionWithoutHiddenLineObject()
    {
        var document = Document(
            Point("A", 0, 0),
            Point("B", 4, 4),
            new GeometryObjectDefinition { Id = "AB", Type = "lineThroughPoints", PointA = "A", PointB = "B" },
            new GeometryObjectDefinition
            {
                Id = "X",
                Type = "gridIntersectionPoint",
                ObjectA = "AB",
                GridAxis = "x",
                GridValue = "2",
                Selector = "first"
            });

        var evaluation = new GeometryEngine().Evaluate(document);
        Assert.Equal(4, document.Objects.Count);
        Assert.DoesNotContain(document.Objects, item => item.Type is "verticalLine" or "horizontalLine");
        MathAndDependencyTests.AssertClose(new Point2(2, 2), evaluation.Results["X"].Point!.Value);
    }

    [Fact]
    public void CircleGridIntersection_SelectorChoosesRequestedPoint()
    {
        var document = Document(
            Point("O", 0, 0),
            new GeometryObjectDefinition { Id = "circle", Type = "circleByCenterRadius", Center = "O", Radius = "2" },
            new GeometryObjectDefinition
            {
                Id = "X",
                Type = "gridIntersectionPoint",
                ObjectA = "circle",
                GridAxis = "x",
                GridValue = "0",
                Selector = "upper"
            });

        var point = new GeometryEngine().Evaluate(document).Results["X"].Point!.Value;
        MathAndDependencyTests.AssertClose(new Point2(0, 2), point);
    }

    [Fact]
    public void DisplayName_RoundTripsThroughJson()
    {
        var document = Document(Point("A", 1, 2));
        document.Objects[0].Name = "起点";
        var clone = ProjectSerializer.Clone(document);
        Assert.Equal("起点", clone.Objects[0].Name);
    }

    private static ProjectDocument Document(params GeometryObjectDefinition[] objects) =>
        new() { Objects = objects.ToList() };

    private static GeometryObjectDefinition Point(string id, double x, double y) =>
        new()
        {
            Id = id,
            Type = "fixedPoint",
            X = x.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Y = y.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
}
