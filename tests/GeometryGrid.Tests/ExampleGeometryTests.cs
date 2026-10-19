using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Tests;

public sealed class ExampleGeometryTests
{
    [Fact]
    public void ExampleJson_LoadsAndEvaluates()
    {
        var document = TestProject.Load();
        var result = new GeometryEngine().Evaluate(document);
        Assert.Empty(result.Errors);
        Assert.Equal("1.0", document.SchemaVersion);
        Assert.Equal(14, document.Objects.Count);
        Assert.Equal(2, result.Scene.Items.Single(item => item.Id == "A").Style.PointRadius);
        Assert.Equal(3.5, result.Scene.Items.Single(item => item.Id == "AC").Style.StrokeWidth);
        Assert.Equal(45, document.Objects.Single(item => item.Id == "A").Label!.FontSize);
    }

    [Fact]
    public void Example_DefaultCoordinatesAreCorrect()
    {
        var result = new GeometryEngine().Evaluate(TestProject.Load()).Results;
        var expected = new Dictionary<string, Point2>
        {
            ["A"] = new(1, 1), ["B"] = new(2, 7d / 2), ["C"] = new(7, 3d / 2),
            ["D"] = new(3d / 4, 4), ["P"] = new(7d / 8, 5d / 2),
            ["M"] = new(17d / 6, 19d / 6), ["N"] = new(7d / 2, 29d / 24)
        };
        foreach (var (id, point) in expected)
            MathAndDependencyTests.AssertClose(point, result[id].Point!.Value);
    }

    [Fact]
    public void UpperSemicircle_PassesThroughABC()
    {
        var result = new GeometryEngine().Evaluate(TestProject.Load()).Results;
        var arc = result["arc_AC"].Arc!;
        foreach (var id in new[] { "A", "B", "C" })
        {
            var point = result[id].Point!.Value;
            Assert.InRange(System.Math.Abs(point.DistanceTo(arc.Center) - arc.Radius), 0, 1e-8);
            Assert.True(arc.Contains(point));
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1/3")]
    [InlineData("4/5")]
    public void MovingParameter_KeepsMAndNOnSpecifiedSegments(string t)
    {
        var document = TestProject.Load();
        document.Parameters.Single(p => p.Id == "t").Value = t;
        var result = new GeometryEngine().Evaluate(document).Results;
        AssertOnSegment(result["M"].Point!.Value, result["D"].Point!.Value, result["C"].Point!.Value);
        AssertOnSegment(result["N"].Point!.Value, result["A"].Point!.Value, result["C"].Point!.Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1/3")]
    [InlineData("4/5")]
    public void DistanceIdentity_Holds(string t)
    {
        var document = TestProject.Load();
        document.Parameters.Single(p => p.Id == "t").Value = t;
        var result = new GeometryEngine().Evaluate(document).Results;
        var an = result["A"].Point!.Value.DistanceTo(result["N"].Point!.Value);
        var dm = result["D"].Point!.Value.DistanceTo(result["M"].Point!.Value);
        Assert.InRange(System.Math.Abs(2 * an - System.Math.Sqrt(5) * dm), 0, 1e-8);
    }

    [Fact]
    public void DragProjection_ClampsParameterRange()
    {
        var document = TestProject.Load();
        var item = document.Objects.Single(o => o.Id == "M");
        var engine = new GeometryEngine();
        Assert.Equal(0, engine.ProjectParameter(document, item, new Point2(-100, 100)), 10);
        Assert.Equal(.8, engine.ProjectParameter(document, item, new Point2(100, -100)), 10);
    }

    private static void AssertOnSegment(Point2 point, Point2 a, Point2 b)
    {
        var cross = Vector2.Cross(point - a, b - a);
        var dot = Vector2.Dot(point - a, b - a);
        var squared = Vector2.Dot(b - a, b - a);
        Assert.InRange(System.Math.Abs(cross), 0, 1e-8);
        Assert.InRange(dot, -1e-8, squared + 1e-8);
    }
}
