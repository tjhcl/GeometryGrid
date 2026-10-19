using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Core.Geometry;

public static class ConstructionTargetResolver
{
    public static ConstructionSearchTarget Resolve(
        SolverTargetDefinition definition,
        SceneEvaluation evaluation)
    {
        Point2 point;
        if (definition.Constraint is null)
        {
            var x = ExpressionParser.Parse(definition.X)
                .Evaluate(evaluation.Parameters)
                .Approximate;
            var y = ExpressionParser.Parse(definition.Y)
                .Evaluate(evaluation.Parameters)
                .Approximate;
            point = new Point2(x, y);
        }
        else
        {
            point = ResolveConstraint(definition.Constraint, evaluation);
        }

        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new InvalidOperationException(
                $"目标 {definition.Name} 的坐标不是有限数值。");
        return new ConstructionSearchTarget(definition.Name, point);
    }

    private static Point2 ResolveConstraint(
        SolverTargetConstraintDefinition constraint,
        SceneEvaluation evaluation) =>
        constraint.Type.Trim().ToLowerInvariant() switch
        {
            "anglebisectorpair" => ResolveAngleBisectorPair(
                constraint,
                evaluation),
            _ => throw new InvalidOperationException(
                $"不支持的求解目标约束“{constraint.Type}”。")
        };

    private static Point2 ResolveAngleBisectorPair(
        SolverTargetConstraintDefinition constraint,
        SceneEvaluation evaluation)
    {
        var a = RequiredPoint(evaluation, constraint.PointA);
        var b = RequiredPoint(evaluation, constraint.PointB);
        var c = RequiredPoint(evaluation, constraint.PointC);
        var e = RequiredPoint(evaluation, constraint.PointE);
        var p = RequiredPoint(evaluation, constraint.PointP);
        var ea = (a - e).Normalized;
        if (ea.Length < GeometryAlgorithms.Epsilon)
            throw new InvalidOperationException("角平分目标中 A 与 E 重合。");

        const int scanCount = 768;
        const double endpointPadding = 1e-7;
        var roots = new List<(double Parameter, Point2 M, Point2 N)>();
        (double U, double Residual)? previous = null;
        for (var index = 0; index <= scanCount; index++)
        {
            var u = endpointPadding +
                    (1 - 2 * endpointPadding) * index / scanCount;
            if (!TryEvaluateAngleBisector(
                    u,
                    a,
                    b,
                    c,
                    e,
                    p,
                    ea,
                    out var residual,
                    out var forward,
                    out var m,
                    out var n))
            {
                previous = null;
                continue;
            }

            if (System.Math.Abs(residual) <= 1e-9 && forward > 0)
                AddRoot(roots, u, m, n);
            if (previous is { } left &&
                left.Residual * residual < 0 &&
                TryBisectAngleBisector(
                    left.U,
                    u,
                    a,
                    b,
                    c,
                    e,
                    p,
                    ea,
                    out var rootU,
                    out var rootM,
                    out var rootN))
                AddRoot(roots, rootU, rootM, rootN);
            previous = (u, residual);
        }

        if (roots.Count == 0)
            throw new InvalidOperationException(
                "当前图形中没有找到满足 EA 平分 ∠MEN、M∈AB、N∈AC 且 M、P、N 共线的目标点。");

        var ordered = roots.OrderBy(root => root.Parameter).ToList();
        var selected = constraint.Selector.Trim().ToLowerInvariant() switch
        {
            "last" or "second" => ordered[^1],
            _ => ordered[0]
        };
        return constraint.Role.Trim().ToUpperInvariant() switch
        {
            "M" => selected.M,
            "N" => selected.N,
            _ => throw new InvalidOperationException(
                "angleBisectorPair 的 role 必须是 M 或 N。")
        };
    }

    private static bool TryBisectAngleBisector(
        double low,
        double high,
        Point2 a,
        Point2 b,
        Point2 c,
        Point2 e,
        Point2 p,
        Vector2 ea,
        out double rootU,
        out Point2 rootM,
        out Point2 rootN)
    {
        rootU = 0;
        rootM = default;
        rootN = default;
        if (!TryEvaluateAngleBisector(
                low,
                a,
                b,
                c,
                e,
                p,
                ea,
                out var lowResidual,
                out _,
                out _,
                out _))
            return false;

        for (var iteration = 0; iteration < 72; iteration++)
        {
            var middle = (low + high) / 2;
            if (!TryEvaluateAngleBisector(
                    middle,
                    a,
                    b,
                    c,
                    e,
                    p,
                    ea,
                    out var middleResidual,
                    out _,
                    out _,
                    out _))
                return false;
            if (lowResidual * middleResidual <= 0)
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowResidual = middleResidual;
            }
        }

        rootU = (low + high) / 2;
        return TryEvaluateAngleBisector(
                   rootU,
                   a,
                   b,
                   c,
                   e,
                   p,
                   ea,
                   out var residual,
                   out var forward,
                   out rootM,
                   out rootN) &&
               System.Math.Abs(residual) <= 1e-7 &&
               forward > 0;
    }

    private static bool TryEvaluateAngleBisector(
        double u,
        Point2 a,
        Point2 b,
        Point2 c,
        Point2 e,
        Point2 p,
        Vector2 ea,
        out double residual,
        out double forward,
        out Point2 m,
        out Point2 n)
    {
        m = a + (b - a) * u;
        if (!TryLineIntersection(
                m,
                p - m,
                a,
                c - a,
                out n,
                out var acParameter) ||
            acParameter < -1e-7 ||
            acParameter > 1 + 1e-7)
        {
            residual = 0;
            forward = 0;
            return false;
        }

        var em = (m - e).Normalized;
        var en = (n - e).Normalized;
        if (em.Length < GeometryAlgorithms.Epsilon ||
            en.Length < GeometryAlgorithms.Epsilon)
        {
            residual = 0;
            forward = 0;
            return false;
        }
        var sum = em + en;
        residual = Vector2.Cross(sum, ea);
        forward = Vector2.Dot(sum, ea);
        return double.IsFinite(residual) && double.IsFinite(forward);
    }

    private static bool TryLineIntersection(
        Point2 firstOrigin,
        Vector2 firstDirection,
        Point2 secondOrigin,
        Vector2 secondDirection,
        out Point2 point,
        out double secondParameter)
    {
        var denominator = Vector2.Cross(
            firstDirection,
            secondDirection);
        if (System.Math.Abs(denominator) < GeometryAlgorithms.Epsilon)
        {
            point = default;
            secondParameter = 0;
            return false;
        }
        var difference = secondOrigin - firstOrigin;
        var firstParameter =
            Vector2.Cross(difference, secondDirection) / denominator;
        secondParameter =
            Vector2.Cross(difference, firstDirection) / denominator;
        point = firstOrigin + firstDirection * firstParameter;
        return true;
    }

    private static Point2 RequiredPoint(
        SceneEvaluation evaluation,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            !evaluation.Results.TryGetValue(id, out var result) ||
            result.Point is not { } point)
            throw new InvalidOperationException(
                $"角平分目标引用的“{id}”不是有效点。");
        return point;
    }

    private static void AddRoot(
        ICollection<(double Parameter, Point2 M, Point2 N)> roots,
        double parameter,
        Point2 m,
        Point2 n)
    {
        if (roots.Any(root =>
                System.Math.Abs(root.Parameter - parameter) <= 1e-6))
            return;
        roots.Add((parameter, m, n));
    }
}
