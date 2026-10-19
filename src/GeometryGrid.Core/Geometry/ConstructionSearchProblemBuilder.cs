using System.Globalization;
using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Core.Geometry;

public sealed record ConstructionSearchProblem(
    ProjectDocument Document,
    SceneEvaluation Evaluation,
    IReadOnlyList<ConstructionSearchPoint> Points,
    IReadOnlyList<ConstructionSearchElement> Elements,
    IReadOnlyList<ConstructionSearchTarget> Targets,
    IReadOnlyList<ConstructionSearchScenario> ValidationScenarios);

/// <summary>
/// Creates the exact same solver input for the desktop application and the
/// headless command-line runner.
/// </summary>
public static class ConstructionSearchProblemBuilder
{
    private static readonly int[] HaltonPrimes =
        [2, 3, 5, 7, 11, 13, 17, 19];

    public static ConstructionSearchProblem Build(
        ProjectDocument document,
        bool? useGrid = null,
        int? parameterSampleCount = null)
    {
        var engine = new GeometryEngine();
        var evaluation = engine.Evaluate(document);
        if (evaluation.Errors.Count > 0)
            throw new InvalidOperationException(
                $"图形计算失败：{evaluation.Errors[0]}");

        var gridEnabled = useGrid ?? document.Solver.UseGrid;
        var (points, elements) = BuildSeed(evaluation, gridEnabled);
        var targets = document.Solver.Targets
            .Select(target =>
                ConstructionTargetResolver.Resolve(target, evaluation))
            .ToList();
        if (targets.Count == 0)
            throw new InvalidOperationException(
                "JSON 的 solver.targets 中至少需要一个求解目标。");

        var scenarios = document.Solver.ValidateParameters
            ? BuildValidationScenarios(
                document,
                evaluation,
                gridEnabled,
                parameterSampleCount ??
                document.Solver.ParameterSampleCount,
                engine)
            : [];
        return new ConstructionSearchProblem(
            document,
            evaluation,
            points,
            elements,
            targets,
            scenarios);
    }

    public static (
        List<ConstructionSearchPoint> Points,
        List<ConstructionSearchElement> Elements) BuildSeed(
        SceneEvaluation evaluation,
        bool useGrid)
    {
        var points = new List<ConstructionSearchPoint>();
        var elements = new List<ConstructionSearchElement>();
        var bounds = evaluation.Scene.Canvas;
        foreach (var item in evaluation.Scene.Items.Where(
                     item => item.Style.Visible))
        {
            if (item.Geometry.Point is { } point)
                points.Add(new ConstructionSearchPoint(
                    $"known-point:{item.Id}",
                    point,
                    item.Id));
            if (item.Geometry.Line is not null ||
                item.Geometry.Circle is not null ||
                item.Geometry.Arc is not null)
                elements.Add(new ConstructionSearchElement(
                    $"known:{item.Id}",
                    item.Geometry));
        }

        if (useGrid &&
            evaluation.Scene.Grid is { Visible: true, Step: > 0 } grid)
        {
            var verticals = new List<double>();
            var horizontals = new List<double>();
            for (var x = bounds.MinX;
                 x <= bounds.MaxX + 1e-9 && verticals.Count < 50;
                 x += grid.Step)
                verticals.Add(x);
            for (var y = bounds.MinY;
                 y <= bounds.MaxY + 1e-9 && horizontals.Count < 50;
                 y += grid.Step)
                horizontals.Add(y);

            foreach (var x in verticals)
                elements.Add(new ConstructionSearchElement(
                    GridSearchKey("x", x),
                    GridGeometry("x", x)));
            foreach (var y in horizontals)
                elements.Add(new ConstructionSearchElement(
                    GridSearchKey("y", y),
                    GridGeometry("y", y)));
            foreach (var x in verticals)
            foreach (var y in horizontals)
                points.Add(new ConstructionSearchPoint(
                    $"grid-point:{FormatNumber(x)}:{FormatNumber(y)}",
                    new Point2(x, y)));
        }

        var completedPoints =
            ConstructionSearchEngine.CompleteKnownIntersections(
                points,
                elements,
                includePoint: point =>
                    point.X >= bounds.MinX - 1e-7 &&
                    point.X <= bounds.MaxX + 1e-7 &&
                    point.Y >= bounds.MinY - 1e-7 &&
                    point.Y <= bounds.MaxY + 1e-7);
        return (completedPoints.ToList(), elements);
    }

    private static List<ConstructionSearchScenario>
        BuildValidationScenarios(
            ProjectDocument source,
            SceneEvaluation baseEvaluation,
            bool useGrid,
            int sampleCount,
            GeometryEngine engine)
    {
        if (sampleCount is < 1 or > 31)
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                "联合检查总位置数必须在 1–31 之间。");

        var movingParameterIds = source.Objects
            .Where(item => item.Draggable)
            .SelectMany(item => new[]
            {
                item.Parameter,
                item.ParameterX,
                item.ParameterY
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (movingParameterIds.Count == 0)
            throw new InvalidOperationException(
                "启用了不定图验证，但没有 draggable 对象引用运动参数。");

        var ranges = movingParameterIds
            .Select(id =>
            {
                var definition = source.Parameters.Single(
                    parameter => parameter.Id == id);
                var min = ExpressionParser.Parse(definition.Min)
                    .Evaluate(baseEvaluation.Parameters)
                    .Approximate;
                var max = ExpressionParser.Parse(definition.Max)
                    .Evaluate(baseEvaluation.Parameters)
                    .Approximate;
                var current = ExpressionParser.Parse(definition.Value)
                    .Evaluate(baseEvaluation.Parameters)
                    .Approximate;
                if (!double.IsFinite(min) ||
                    !double.IsFinite(max) ||
                    max <= min ||
                    !double.IsFinite(current))
                    throw new InvalidOperationException(
                        $"运动参数 {id} 的 value/min/max 无效。");
                return (Id: id, Min: min, Max: max, Current: current);
            })
            .ToList();

        var plans = Enumerable.Range(
                0,
                System.Math.Max(0, sampleCount - 1))
            .Select(sampleIndex =>
            {
                var values = ranges
                    .Select((range, parameterIndex) =>
                    {
                        var fraction = Halton(
                            sampleIndex + 1 + parameterIndex * 11,
                            HaltonPrimes[
                                parameterIndex % HaltonPrimes.Length]);
                        var value =
                            range.Min +
                            (range.Max - range.Min) * fraction;
                        var distance =
                            System.Math.Abs(value - range.Current) /
                            (range.Max - range.Min);
                        return (range.Id, Value: value, Distance: distance);
                    })
                    .ToList();
                return (
                    Index: sampleIndex,
                    Values: values,
                    RejectionScore: values.Sum(value => value.Distance));
            })
            .OrderByDescending(plan => plan.RejectionScore)
            .ThenBy(plan => plan.Index)
            .ToList();

        var scenarios = new List<ConstructionSearchScenario>();
        foreach (var plan in plans)
        {
            var document = ProjectSerializer.Clone(source);
            var labels = new List<string>();
            foreach (var sample in plan.Values)
            {
                document.Parameters.Single(
                        parameter => parameter.Id == sample.Id)
                    .Value = FormatNumber(sample.Value);
                labels.Add($"{sample.Id}={sample.Value:G6}");
            }

            var evaluation = engine.Evaluate(document);
            if (evaluation.Errors.Count > 0)
                throw new InvalidOperationException(
                    $"运动位置 {string.Join(", ", labels)} 无效："
                    + evaluation.Errors[0]);
            var (points, elements) = BuildSeed(evaluation, useGrid);
            var targets = document.Solver.Targets
                .Select(target =>
                    ConstructionTargetResolver.Resolve(target, evaluation))
                .ToList();
            scenarios.Add(new ConstructionSearchScenario(
                points,
                elements,
                targets,
                string.Join(", ", labels)));
        }
        return scenarios;
    }

    private static GeometryResult GridGeometry(
        string axis,
        double value) =>
        new()
        {
            ObjectId = GridSearchKey(axis, value),
            Type = "gridLine",
            IsValid = true,
            Line = axis == "x"
                ? new LineGeometry(
                    new Point2(value, 0),
                    new Vector2(0, 1))
                : new LineGeometry(
                    new Point2(0, value),
                    new Vector2(1, 0))
        };

    private static string GridSearchKey(string axis, double value) =>
        $"grid:{axis}:{FormatNumber(value)}";

    private static string FormatNumber(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    private static double Halton(int index, int basis)
    {
        var result = 0d;
        var fraction = 1d;
        while (index > 0)
        {
            fraction /= basis;
            result += fraction * (index % basis);
            index /= basis;
        }
        return result;
    }
}
