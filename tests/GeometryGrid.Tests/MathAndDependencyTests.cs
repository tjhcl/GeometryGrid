using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Math;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Tests;

public sealed class MathAndDependencyTests
{
    [Fact]
    public void Rational_ReducesAndCalculatesExactly()
    {
        Assert.Equal("1/2", new Rational(10, 20).ToString());
        Assert.Equal(new Rational(5, 6), new Rational(1, 2) + new Rational(1, 3));
        Assert.Equal(new Rational(1, 6), new Rational(1, 2) - new Rational(1, 3));
        Assert.Equal(new Rational(1, 6), new Rational(1, 2) * new Rational(1, 3));
        Assert.Equal(new Rational(3, 2), new Rational(1, 2) / new Rational(1, 3));
    }

    [Fact]
    public void ExpressionParser_HandlesWhitelistAndSqrt()
    {
        var parsed = ExpressionParser.Parse("(3 + 25*t)/4");
        Assert.Contains("t", parsed.Dependencies);
        Assert.Equal(new Rational(17, 6), parsed.Evaluate(new Dictionary<string, Rational> { ["t"] = new(1, 3) }).Exact);
        Assert.Equal(System.Math.Sqrt(5), ExpressionParser.Parse("sqrt(5)").Evaluate(new Dictionary<string, Rational>()).Approximate, 12);
        Assert.Throws<ExpressionException>(() => ExpressionParser.Parse("System.IO.File.Delete(x)"));
    }

    [Theory]
    [InlineData("1E-7", 0.0000001)]
    [InlineData("-2.5e+3", -2500)]
    [InlineData("3e2 + 4E-1", 300.4)]
    public void ExpressionParser_SupportsScientificNotation(
        string expression,
        double expected)
    {
        var result = ExpressionParser.Parse(expression)
            .Evaluate(new Dictionary<string, Rational>());
        Assert.Equal(expected, result.Approximate, 12);
    }

    [Fact]
    public void ParameterSubstitution_IsExact()
    {
        var result = ExpressionParser.Parse("1 + 5*t/8").Evaluate(new Dictionary<string, Rational> { ["t"] = new(1, 3) });
        Assert.Equal(new Rational(29, 24), result.Exact);
    }

    [Fact]
    public void ExpressionParser_SupportsTranscendentalConstantsAndDegreeFunctions()
    {
        var values = new Dictionary<string, NumericValue>();
        Assert.Equal(System.Math.PI, ExpressionParser.Parse("pi").Evaluate(values).Approximate, 12);
        Assert.Equal(0.5, ExpressionParser.Parse("sind(30)").Evaluate(values).Approximate, 12);
        Assert.Equal(System.Math.Cos(50 * System.Math.PI / 180),
            ExpressionParser.Parse("cosd(50)").Evaluate(values).Approximate, 12);
        Assert.Equal(System.Math.Tan(20 * System.Math.PI / 180),
            ExpressionParser.Parse("tan(20*pi/180)").Evaluate(values).Approximate, 12);
        Assert.Equal(8, ExpressionParser.Parse("2^3").Evaluate(values).Approximate, 12);
        Assert.Equal(System.Math.Atan2(1, -1) * 180 / System.Math.PI,
            ExpressionParser.Parse("atan2d(1,-1)").Evaluate(values).Approximate, 12);
    }

    [Fact]
    public void GeometryEngine_AllowsTranscendentalParameterDependencies()
    {
        var document = new ProjectDocument
        {
            Parameters =
            [
                new ParameterDefinition { Id = "angle", Value = "50", Min = "0", Max = "90" },
                new ParameterDefinition { Id = "length", Value = "sqrt(17)/(4*cosd(angle))", Min = "0", Max = "5" }
            ],
            Objects =
            [
                new GeometryObjectDefinition
                {
                    Id = "C",
                    Type = "fixedPoint",
                    X = "1+(4*sqrt(3)+1)/(8*cosd(angle))",
                    Y = "1+(4-sqrt(3))/(8*cosd(angle))"
                }
            ]
        };

        var evaluation = new GeometryEngine().Evaluate(document);
        Assert.False(evaluation.Parameters["length"].Exact.HasValue);
        AssertClose(new Point2(2.54176183369141, 1.44103782459674),
            evaluation.Results["C"].Point!.Value, 1e-12);
    }

    [Fact]
    public void LineLineIntersection_Works()
    {
        var point = Assert.Single(GeometryAlgorithms.LineLine(
            new LineGeometry(new Point2(0, 0), new Vector2(1, 1)),
            new LineGeometry(new Point2(0, 2), new Vector2(1, -1))));
        AssertClose(new Point2(1, 1), point);
    }

    [Fact]
    public void LineCircleIntersection_Works()
    {
        var points = GeometryAlgorithms.LineCircle(new LineGeometry(new Point2(-2, 0), new Vector2(1, 0)), new CircleGeometry(new Point2(0, 0), 1));
        Assert.Equal(2, points.Count);
        Assert.Contains(points, p => System.Math.Abs(p.X + 1) < 1e-9);
        Assert.Contains(points, p => System.Math.Abs(p.X - 1) < 1e-9);
    }

    [Fact]
    public void Midpoint_IsCalculatedByEngine()
    {
        var result = new GeometryEngine().Evaluate(TestProject.Load());
        AssertClose(new Point2(7d / 8, 5d / 2), result.Results["P"].Point!.Value);
    }

    [Fact]
    public void DependencyGraph_SortsDependencies()
    {
        var objects = new[]
        {
            new GeometryObjectDefinition { Id = "line", Type = "segment", PointA = "A", PointB = "B" },
            new GeometryObjectDefinition { Id = "B", Type = "fixedPoint" },
            new GeometryObjectDefinition { Id = "A", Type = "fixedPoint" }
        };
        var order = new DependencyGraph(objects).TopologicalOrder().ToList();
        Assert.True(order.IndexOf("A") < order.IndexOf("line"));
        Assert.True(order.IndexOf("B") < order.IndexOf("line"));
    }

    [Fact]
    public void DependencyGraph_DetectsCycle()
    {
        var objects = new[]
        {
            new GeometryObjectDefinition { Id = "A", Type = "midpoint", PointA = "B", PointB = "B" },
            new GeometryObjectDefinition { Id = "B", Type = "midpoint", PointA = "A", PointB = "A" }
        };
        Assert.Throws<DependencyException>(() => new DependencyGraph(objects).TopologicalOrder());
    }

    internal static void AssertClose(Point2 expected, Point2 actual, double tolerance = 1e-8)
    {
        Assert.InRange(System.Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(System.Math.Abs(expected.Y - actual.Y), 0, tolerance);
    }
}
