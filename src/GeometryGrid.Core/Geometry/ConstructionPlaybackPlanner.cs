namespace GeometryGrid.Core.Geometry;

public enum ConstructionDisplayKind
{
    Segment,
    Ray,
    Line,
    Circle
}

public sealed record ConstructionPlaybackStepPlan(
    ConstructionStep Step,
    ConstructionDisplayKind DisplayKind,
    Point2 Start,
    Point2 Guide,
    Point2 End,
    double DisplayMinParameter,
    double DisplayMaxParameter,
    IReadOnlyList<Point2> RevealedPoints);

public static class ConstructionPlaybackPlanner
{
    public static IReadOnlyList<ConstructionPlaybackStepPlan> Create(ConstructionSolution solution)
    {
        var relevantPoints = solution.Steps
            .SelectMany(step => new[] { step.PointA, step.PointB })
            .Concat(solution.CompletedTargets.Select(target => target.Point))
            .GroupBy(point => point.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        return solution.Steps
            .Select(step => CreateStep(step, relevantPoints))
            .ToList();
    }

    private static ConstructionPlaybackStepPlan CreateStep(
        ConstructionStep step,
        IReadOnlyList<ConstructionSearchPoint> relevantPoints)
    {
        if (step.Tool == ConstructionToolKind.Circle)
        {
            return new ConstructionPlaybackStepPlan(
                step,
                ConstructionDisplayKind.Circle,
                step.PointA.Position,
                step.PointB.Position,
                step.PointB.Position,
                0,
                1,
                ProducedBy(step.Key, relevantPoints));
        }

        var a = step.PointA.Position;
        var b = step.PointB.Position;
        var direction = b - a;
        var lengthSquared = Vector2.Dot(direction, direction);
        if (lengthSquared < GeometryAlgorithms.Epsilon)
        {
            return new ConstructionPlaybackStepPlan(
                step,
                ConstructionDisplayKind.Segment,
                a,
                b,
                b,
                0,
                1,
                []);
        }

        var revealed = ProducedBy(step.Key, relevantPoints);
        var parameters = revealed
            .Select(point => Vector2.Dot(point - a, direction) / lengthSquared)
            .Where(double.IsFinite)
            .Concat([0d, 1d])
            .ToList();
        var min = parameters.Min();
        var max = parameters.Max();
        const double tolerance = 1e-7;

        if (min >= -tolerance && max <= 1 + tolerance)
        {
            return new ConstructionPlaybackStepPlan(
                step,
                ConstructionDisplayKind.Segment,
                a,
                b,
                b,
                0,
                1,
                revealed);
        }

        if (min >= -tolerance)
        {
            return new ConstructionPlaybackStepPlan(
                step,
                ConstructionDisplayKind.Ray,
                a,
                b,
                a + direction * max,
                0,
                max,
                revealed);
        }

        if (max <= 1 + tolerance)
        {
            var reverseDirection = a - b;
            var reverseMax = 1 - min;
            return new ConstructionPlaybackStepPlan(
                step,
                ConstructionDisplayKind.Ray,
                b,
                a,
                b + reverseDirection * reverseMax,
                0,
                reverseMax,
                revealed);
        }

        return new ConstructionPlaybackStepPlan(
            step,
            ConstructionDisplayKind.Line,
            a + direction * min,
            b,
            a + direction * max,
            min,
            max,
            revealed);
    }

    private static IReadOnlyList<Point2> ProducedBy(
        string elementKey,
        IReadOnlyList<ConstructionSearchPoint> points) =>
        points
            .Where(point =>
                string.Equals(point.ParentElementA, elementKey, StringComparison.Ordinal) ||
                string.Equals(point.ParentElementB, elementKey, StringComparison.Ordinal))
            .Select(point => point.Position)
            .DistinctBy(PointKey)
            .ToList();

    private static string PointKey(Point2 point) =>
        $"{System.Math.Round(point.X, 9):G17}:{System.Math.Round(point.Y, 9):G17}";
}
