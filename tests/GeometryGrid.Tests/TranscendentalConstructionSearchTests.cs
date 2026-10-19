using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Serialization;
using System.IO;

namespace GeometryGrid.Tests;

public sealed class TranscendentalConstructionSearchTests
{
    [Fact]
    public void TranscendentalExample_EvaluatesAllThreeExactFormulaTargets()
    {
        var document = ProjectSerializer.Load(FindExample());
        var evaluation = new GeometryEngine().Evaluate(document);
        Assert.Empty(evaluation.Errors);

        var expected = new[]
        {
            new Point2(2.89695607941962, 1.03191118560012),
            new Point2(2.59962603810979, 0.887126385927845),
            new Point2(1.47814082987958, 0.341019404965802)
        };
        for (var index = 0; index < expected.Length; index++)
        {
            var target = document.Solver.Targets[index];
            var point = new Point2(
                ExpressionParser.Parse(target.X).Evaluate(evaluation.Parameters).Approximate,
                ExpressionParser.Parse(target.Y).Evaluate(evaluation.Parameters).Approximate);
            MathAndDependencyTests.AssertClose(expected[index], point, 1e-12);
        }
    }

    [Fact]
    public void ManualSingleTargetSearch_WhenRequestedByEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("GEOMETRYGRID_MANUAL_SEARCH_JSON");
        if (string.IsNullOrWhiteSpace(path)) return;

        var document = ProjectSerializer.Load(path);
        var evaluation = new GeometryEngine().Evaluate(document);
        var definition = Assert.Single(document.Solver.Targets);
        var target = new Point2(
            ExpressionParser.Parse(definition.X).Evaluate(evaluation.Parameters).Approximate,
            ExpressionParser.Parse(definition.Y).Evaluate(evaluation.Parameters).Approximate);
        var points = evaluation.Scene.Items
            .Where(item => item.Style.Visible && item.Geometry.Point is not null)
            .Select(item => new ConstructionSearchPoint(
                $"known-point:{item.Id}",
                item.Geometry.Point!.Value,
                item.Id))
            .ToList();
        var elements = evaluation.Scene.Items
            .Where(item => item.Style.Visible &&
                           (item.Geometry.Line is not null ||
                            item.Geometry.Circle is not null ||
                            item.Geometry.Arc is not null))
            .Select(item => new ConstructionSearchElement($"known:{item.Id}", item.Geometry))
            .ToList();
        var bounds = evaluation.Scene.Canvas;
        var grid = evaluation.Scene.Grid;
        if (document.Solver.UseGrid && grid.Visible && grid.Step > 0)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX + 1e-9; x += grid.Step)
            {
                elements.Add(new ConstructionSearchElement(
                    $"grid:x:{x:G17}",
                    InfiniteLine($"grid:x:{x:G17}", new Point2(x, 0), new Vector2(0, 1))));
                for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9; y += grid.Step)
                    points.Add(new ConstructionSearchPoint(
                        $"grid-point:{x:G17}:{y:G17}",
                        new Point2(x, y)));
            }
            for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9; y += grid.Step)
                elements.Add(new ConstructionSearchElement(
                    $"grid:y:{y:G17}",
                    InfiniteLine($"grid:y:{y:G17}", new Point2(0, y), new Vector2(1, 0))));
        }
        var completedPoints = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point =>
                point.X >= bounds.MinX - 1e-7 &&
                point.X <= bounds.MaxX + 1e-7 &&
                point.Y >= bounds.MinY - 1e-7 &&
                point.Y <= bounds.MaxY + 1e-7);
        var maxSteps = int.TryParse(
            Environment.GetEnvironmentVariable("GEOMETRYGRID_MANUAL_MAX_STEPS"),
            out var requestedMaxSteps)
            ? requestedMaxSteps
            : document.Solver.MaxSteps;
        var maxStates = long.TryParse(
            Environment.GetEnvironmentVariable("GEOMETRYGRID_MANUAL_MAX_STATES"),
            out var requestedMaxStates)
            ? requestedMaxStates
            : document.Solver.MaxStates;
        var guidedLimit = int.TryParse(
            Environment.GetEnvironmentVariable("GEOMETRYGRID_MANUAL_GUIDED_LIMIT"),
            out var requestedGuidedLimit)
            ? requestedGuidedLimit
            : 8;
        var mode = Environment.GetEnvironmentVariable("GEOMETRYGRID_MANUAL_MODE")
            ?.Trim()
            .ToLowerInvariant();
        var useGuided = mode != "exhaustive";
        var useExhaustive = mode == "exhaustive";
        var report = new ConstructionSearchEngine().Search(
            completedPoints,
            elements,
            definition.Name,
            target,
            new ConstructionSearchOptions(
                MaxSteps: maxSteps,
                MaxSolutions: 1,
                MaxStates: maxStates,
                MaxPointsPerState: document.Solver.UseGrid ? 240 : 120,
                UseStraightedge: document.Solver.UseStraightedge,
                UseCompass: document.Solver.UseCompass,
                MinX: bounds.MinX,
                MaxX: bounds.MaxX,
                MinY: bounds.MinY,
                MaxY: bounds.MaxY,
                UseCollinearityClosure: useGuided,
                UseGuidedSearch: useGuided,
                GuidedCandidateLimit: guidedLimit,
                UseExhaustiveSearch: useExhaustive));

        Assert.True(report.Solutions.Count > 0,
            $"{definition.Name}: target=({target.X:G17},{target.Y:G17}), "
            + $"explored={report.ExploredStates}, depth={report.SearchedDepth}, "
            + $"limit={report.StateLimitReached}, strategy={report.Strategy}");
        var solution = report.Solutions[0];
        Console.WriteLine(
            $"{definition.Name}: {solution.StepCount} steps, explored={report.ExploredStates}, "
            + $"strategy={report.Strategy}");
        foreach (var step in solution.Steps)
            Console.WriteLine(step.Description);
    }

    private static GeometryResult InfiniteLine(string id, Point2 origin, Vector2 direction) =>
        new()
        {
            ObjectId = id,
            Type = "infiniteLine",
            IsValid = true,
            Line = new LineGeometry(origin, direction)
        };

    private static string FindExample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "examples", "transcendental_three_P_5x4.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("找不到 examples/transcendental_three_P_5x4.json。");
    }
}
