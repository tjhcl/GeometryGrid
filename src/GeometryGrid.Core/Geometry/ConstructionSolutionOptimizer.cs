namespace GeometryGrid.Core.Geometry;

public static class ConstructionSolutionOptimizer
{
    public static ConstructionSolution RemoveRedundantCarrierSteps(
        ConstructionSolution solution,
        IReadOnlyList<ConstructionSearchElement> initialElements,
        double tolerance = 1e-7)
    {
        var optimized = solution;
        foreach (var step in solution.Steps.ToList())
        {
            if (step.Tool != ConstructionToolKind.StraightLine) continue;
            if (optimized.Steps.All(candidate =>
                    !string.Equals(candidate.Key, step.Key, StringComparison.Ordinal)))
                continue;
            var dependentPoints = RelevantPoints(optimized)
                .Where(point =>
                    string.Equals(point.ParentElementA, step.Key, StringComparison.Ordinal) ||
                    string.Equals(point.ParentElementB, step.Key, StringComparison.Ordinal))
                .ToList();
            if (dependentPoints.Count == 0)
            {
                optimized = RemoveUnusedStep(optimized, step.Key);
                continue;
            }

            var stepLine = InfiniteLine(step);
            var replacement = initialElements.FirstOrDefault(element =>
                element.Geometry.Line is { } knownLine &&
                SameCarrier(stepLine, knownLine, tolerance) &&
                dependentPoints.All(point => Contains(knownLine, point.Position, tolerance)));
            if (replacement is null) continue;

            optimized = ReplaceAndRemove(optimized, step.Key, replacement.Key);
        }
        return optimized;
    }

    private static ConstructionSolution RemoveUnusedStep(
        ConstructionSolution solution,
        string removedElementKey) =>
        solution with
        {
            Steps = solution.Steps
                .Where(step =>
                    !string.Equals(
                        step.Key,
                        removedElementKey,
                        StringComparison.Ordinal))
                .ToList()
        };

    private static ConstructionSolution ReplaceAndRemove(
        ConstructionSolution solution,
        string removedElementKey,
        string replacementElementKey)
    {
        ConstructionSearchPoint ReplacePoint(ConstructionSearchPoint point) =>
            point with
            {
                ParentElementA = string.Equals(
                    point.ParentElementA,
                    removedElementKey,
                    StringComparison.Ordinal)
                    ? replacementElementKey
                    : point.ParentElementA,
                ParentElementB = string.Equals(
                    point.ParentElementB,
                    removedElementKey,
                    StringComparison.Ordinal)
                    ? replacementElementKey
                    : point.ParentElementB
            };

        var steps = solution.Steps
            .Where(step => !string.Equals(step.Key, removedElementKey, StringComparison.Ordinal))
            .Select(step => step with
            {
                PointA = ReplacePoint(step.PointA),
                PointB = ReplacePoint(step.PointB)
            })
            .ToList();
        var targets = solution.CompletedTargets
            .Select(target => target with { Point = ReplacePoint(target.Point) })
            .ToList();
        var targetPoint = ReplacePoint(solution.TargetPoint);
        return solution with
        {
            Steps = steps,
            TargetPoint = targetPoint,
            Targets = targets
        };
    }

    private static IEnumerable<ConstructionSearchPoint> RelevantPoints(
        ConstructionSolution solution) =>
        solution.Steps
            .SelectMany(step => new[] { step.PointA, step.PointB })
            .Concat(solution.CompletedTargets.Select(target => target.Point));

    private static LineGeometry InfiniteLine(ConstructionStep step) =>
        new(
            step.PointA.Position,
            step.PointB.Position - step.PointA.Position);

    private static bool SameCarrier(
        LineGeometry a,
        LineGeometry b,
        double tolerance)
    {
        var scale = System.Math.Max(1, a.Direction.Length * b.Direction.Length);
        if (System.Math.Abs(Vector2.Cross(a.Direction, b.Direction)) > tolerance * scale)
            return false;
        var offsetScale = System.Math.Max(1, b.Direction.Length);
        return System.Math.Abs(Vector2.Cross(a.Origin - b.Origin, b.Direction)) <=
               tolerance * offsetScale;
    }

    private static bool Contains(
        LineGeometry line,
        Point2 point,
        double tolerance)
    {
        var lengthSquared = Vector2.Dot(line.Direction, line.Direction);
        if (lengthSquared < GeometryAlgorithms.Epsilon) return false;
        var parameter = Vector2.Dot(point - line.Origin, line.Direction) / lengthSquared;
        var projected = line.At(parameter);
        return projected.DistanceTo(point) <= tolerance &&
               parameter >= line.MinParameter - tolerance &&
               parameter <= line.MaxParameter + tolerance;
    }
}
