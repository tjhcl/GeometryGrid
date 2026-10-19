using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Math;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Rendering;

namespace GeometryGrid.Core.Geometry;

public sealed class SceneEvaluation
{
    public required IReadOnlyDictionary<string, GeometryResult> Results { get; init; }
    public required IReadOnlyDictionary<string, NumericValue> Parameters { get; init; }
    public required IReadOnlyList<string> Order { get; init; }
    public required RenderScene Scene { get; init; }
    public IReadOnlyList<string> Errors => Results.Values.Where(r => !r.IsValid).Select(r => r.Error!).ToList();
}

public sealed class GeometryEngine
{
    public SceneEvaluation Evaluate(ProjectDocument document)
    {
        var parameters = EvaluateParameters(document);
        var graph = new DependencyGraph(document.Objects);
        var order = graph.TopologicalOrder();
        var definitions = document.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var results = new Dictionary<string, GeometryResult>(StringComparer.Ordinal);

        foreach (var id in order)
        {
            var item = definitions[id];
            var invalidDependency = DependencyGraph.GetReferences(item).Select(reference => results[reference]).FirstOrDefault(result => !result.IsValid);
            results[id] = invalidDependency is not null
                ? GeometryResult.Invalid(item.Id, item.Type, $"对象 {item.Id} 无效：依赖对象 {invalidDependency.ObjectId} 无效。")
                : EvaluateObject(item, results, parameters);
        }

        var scene = new RenderScene
        {
            Canvas = document.Canvas,
            Grid = document.Grid,
            Items = document.Objects
                .Where(o => results[o.Id].IsValid)
                .Select(o => new RenderItem { Id = o.Id, Type = o.Type, Geometry = GeometryForRendering(results[o.Id], o), Style = StyleResolver.Resolve(document, o), Label = o.Label })
                .OrderBy(i => i.Style.ZIndex).ToList()
        };
        return new SceneEvaluation { Results = results, Parameters = parameters, Order = order, Scene = scene };
    }

    public SceneEvaluation EvaluateParameterChange(ProjectDocument document, SceneEvaluation previous, string parameterId)
    {
        var parameters = EvaluateParameters(document);
        var graph = new DependencyGraph(document.Objects);
        var order = graph.TopologicalOrder();
        var definitions = document.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var directlyAffected = document.Objects.Where(o => UsesParameter(o, parameterId)).Select(o => o.Id).ToList();
        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in directlyAffected) affected.UnionWith(graph.AffectedBy(id));
        var results = previous.Results.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        foreach (var id in order.Where(affected.Contains))
        {
            var item = definitions[id];
            var invalidDependency = DependencyGraph.GetReferences(item).Select(reference => results[reference]).FirstOrDefault(result => !result.IsValid);
            results[id] = invalidDependency is not null
                ? GeometryResult.Invalid(item.Id, item.Type, $"对象 {item.Id} 无效：依赖对象 {invalidDependency.ObjectId} 无效。")
                : EvaluateObject(item, results, parameters);
        }

        var scene = new RenderScene
        {
            Canvas = document.Canvas,
            Grid = document.Grid,
            Items = document.Objects.Where(o => results[o.Id].IsValid)
                .Select(o => new RenderItem { Id = o.Id, Type = o.Type, Geometry = GeometryForRendering(results[o.Id], o), Style = StyleResolver.Resolve(document, o), Label = o.Label })
                .OrderBy(i => i.Style.ZIndex).ToList()
        };
        return new SceneEvaluation { Results = results, Parameters = parameters, Order = order, Scene = scene };
    }

    public double ProjectParameter(ProjectDocument document, GeometryObjectDefinition item, Point2 worldPoint)
    {
        if (item.Parameter is null) throw new InvalidOperationException("动点缺少 parameter。");
        if (item.Type == "pointOnGridLine")
        {
            if (item.GridAxis is not ("x" or "y")) throw new InvalidOperationException("格线动点缺少有效的 gridAxis。");
            var gridParameter = document.Parameters.Single(p => p.Id == item.Parameter);
            var gridValues = EvaluateParameters(document);
            var gridMin = ExpressionParser.Parse(gridParameter.Min).Evaluate(gridValues).Approximate;
            var gridMax = ExpressionParser.Parse(gridParameter.Max).Evaluate(gridValues).Approximate;
            return System.Math.Clamp(item.GridAxis == "x" ? worldPoint.Y : worldPoint.X, gridMin, gridMax);
        }
        if (item.Support is null) throw new InvalidOperationException("动点缺少 support。");
        var evaluation = Evaluate(document);
        if (!evaluation.Results.TryGetValue(item.Support, out var support))
            throw new InvalidOperationException("找不到动点支撑对象。");
        var parameter = document.Parameters.Single(p => p.Id == item.Parameter);
        var values = EvaluateParameters(document);
        var min = ExpressionParser.Parse(parameter.Min).Evaluate(values).Approximate;
        var max = ExpressionParser.Parse(parameter.Max).Evaluate(values).Approximate;
        double projected;
        if (support.Line is { } line)
        {
            var directionSquared = Vector2.Dot(line.Direction, line.Direction);
            if (directionSquared < GeometryAlgorithms.Epsilon) throw new InvalidOperationException("支撑线退化。");
            projected = Vector2.Dot(worldPoint - line.Origin, line.Direction) / directionSquared;
        }
        else if (support.Circle is { } circle)
        {
            projected = GeometryAlgorithms.NormalizeAngle(System.Math.Atan2(worldPoint.Y - circle.Center.Y, worldPoint.X - circle.Center.X)) / (2 * System.Math.PI);
        }
        else if (support.Arc is { } arc)
        {
            var angle = System.Math.Atan2(worldPoint.Y - arc.Center.Y, worldPoint.X - arc.Center.X);
            projected = GeometryAlgorithms.NormalizeAngle(angle - arc.StartAngleRadians) / System.Math.Abs(arc.SweepRadians);
        }
        else throw new InvalidOperationException("support 必须是线、圆或圆弧对象。");
        return System.Math.Clamp(projected, min, max);
    }

    private static Dictionary<string, NumericValue> EvaluateParameters(ProjectDocument document)
    {
        var result = new Dictionary<string, NumericValue>(StringComparer.Ordinal);
        var pending = document.Parameters.ToDictionary(p => p.Id, StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var progressed = false;
            foreach (var entry in pending.ToList())
            {
                var expression = ExpressionParser.Parse(entry.Value.Value);
                if (expression.Dependencies.Any(d => !result.ContainsKey(d))) continue;
                var value = expression.Evaluate(result);
                if (double.IsFinite(value.Approximate))
                {
                    result[entry.Key] = value;
                    pending.Remove(entry.Key);
                    progressed = true;
                    continue;
                }
                if (value.Exact is null) throw new ExpressionException($"参数 {entry.Key} 必须能求得精确有理数。", 0);
                result[entry.Key] = value.Exact.Value;
                pending.Remove(entry.Key);
                progressed = true;
            }
            if (!progressed) throw new ExpressionException($"参数存在缺失或循环依赖：{string.Join(", ", pending.Keys)}。", 0);
        }
        return result;
    }

    private static GeometryResult GeometryForRendering(
        GeometryResult geometry,
        GeometryObjectDefinition definition)
    {
        if (geometry.Line is not { } line ||
            definition.DisplayMinParameter is null && definition.DisplayMaxParameter is null)
            return geometry;

        var min = definition.DisplayMinParameter ?? line.MinParameter;
        var max = definition.DisplayMaxParameter ?? line.MaxParameter;
        min = System.Math.Max(min, line.MinParameter);
        max = System.Math.Min(max, line.MaxParameter);
        if (min > max) (min, max) = (max, min);
        return new GeometryResult
        {
            ObjectId = geometry.ObjectId,
            Type = geometry.Type,
            IsValid = geometry.IsValid,
            Error = geometry.Error,
            Point = geometry.Point,
            Line = line with { MinParameter = min, MaxParameter = max },
            Circle = geometry.Circle,
            Arc = geometry.Arc
        };
    }

    private static bool UsesParameter(GeometryObjectDefinition item, string parameterId)
    {
        if (item.Parameter == parameterId ||
            item.ParameterX == parameterId ||
            item.ParameterY == parameterId)
            return true;
        foreach (var expression in new[] { item.X, item.Y, item.Radius, item.Ratio, item.StartAngle, item.EndAngle, item.GridValue })
        {
            if (string.IsNullOrWhiteSpace(expression)) continue;
            if (ExpressionParser.Parse(expression).Dependencies.Contains(parameterId)) return true;
        }
        return false;
    }

    private static GeometryResult EvaluateObject(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        try
        {
            var type = item.Type;
            return type switch
            {
                "fixedPoint" or "parametricPoint" => Point(item, Eval(item.X, parameters), Eval(item.Y, parameters)),
                "midpoint" => Midpoint(item, results),
                "ratioPoint" => RatioPoint(item, results, parameters),
                "intersectionPoint" => IntersectionPoint(item, results),
                "gridIntersectionPoint" => GridIntersectionPoint(item, results, parameters),
                "pointOnGridLine" => PointOnGridLine(item, parameters),
                "pointOnSegment" or "pointOnLine" => PointOnLine(item, results, parameters),
                "pointOnCircle" or "pointOnArc" => PointOnCircle(item, results, parameters),
                "segment" => Line(item, results, 0, 1),
                "ray" => Line(item, results, 0, double.PositiveInfinity),
                "infiniteLine" or "lineThroughPoints" => Line(item, results, double.NegativeInfinity, double.PositiveInfinity),
                "perpendicularLine" => PerpendicularLine(item, results, parameters),
                "parallelLine" => ParallelLine(item, results, parameters),
                "horizontalLine" => Horizontal(item, parameters),
                "verticalLine" => Vertical(item, parameters),
                "circleByCenterRadius" => Circle(item, results, parameters),
                "circleByCenterPoint" => CenterPointCircle(item, results),
                "circleByCenterDistance" => CenterDistanceCircle(item, results),
                "circleByDiameter" => DiameterCircle(item, results),
                "circleByThreePoints" => ThreePointCircle(item, results),
                "arc" => Arc(item, results, parameters),
                "semicircleByDiameter" => Semicircle(item, results),
                _ => GeometryResult.Invalid(item.Id, item.Type, $"对象 {item.Id} 使用了不支持的类型“{item.Type}”。")
            };
        }
        catch (Exception ex)
        {
            return GeometryResult.Invalid(item.Id, item.Type, $"对象 {item.Id} 计算失败：{ex.Message}");
        }
    }

    private static double Eval(string? expression, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new InvalidOperationException("缺少表达式字段。");
        return ExpressionParser.Parse(expression).Evaluate(parameters).Approximate;
    }

    private static GeometryResult Point(GeometryObjectDefinition item, double x, double y) =>
        new() { ObjectId = item.Id, Type = item.Type, IsValid = true, Point = new Point2(x, y) };

    private static Point2 RequiredPoint(IReadOnlyDictionary<string, GeometryResult> results, string? id) =>
        id is not null && results.TryGetValue(id, out var value) && value.Point is { } point ? point : throw new InvalidOperationException($"引用“{id}”不是有效点。");

    private static GeometryResult Midpoint(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        return Point(item, (a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    private static GeometryResult RatioPoint(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var ratio = Eval(item.Ratio, parameters);
        return Point(item, a.X + ratio * (b.X - a.X), a.Y + ratio * (b.Y - a.Y));
    }

    private static GeometryResult Line(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, double min, double max)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var direction = b - a;
        if (direction.Length < GeometryAlgorithms.Epsilon) throw new InvalidOperationException("两点重合，无法定义直线。");
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Line = new LineGeometry(a, direction, min, max) };
    }

    private static GeometryResult Horizontal(GeometryObjectDefinition item, IReadOnlyDictionary<string, NumericValue> parameters) =>
        new() { ObjectId = item.Id, Type = item.Type, IsValid = true, Line = new LineGeometry(new Point2(0, Eval(item.Y, parameters)), new Vector2(1, 0)) };

    private static GeometryResult Vertical(GeometryObjectDefinition item, IReadOnlyDictionary<string, NumericValue> parameters) =>
        new() { ObjectId = item.Id, Type = item.Type, IsValid = true, Line = new LineGeometry(new Point2(Eval(item.X, parameters), 0), new Vector2(0, 1)) };

    private static GeometryResult PerpendicularLine(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, GeometryResult> results,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var point = RequiredPoint(results, item.PointA);
        var source = RelationSourceLine(item, results, parameters);
        var direction = new Vector2(-source.Direction.Y, source.Direction.X);
        if (direction.Length < GeometryAlgorithms.Epsilon) throw new InvalidOperationException("基准直线退化。");
        return new GeometryResult
        {
            ObjectId = item.Id,
            Type = item.Type,
            IsValid = true,
            Line = new LineGeometry(point, direction)
        };
    }

    private static GeometryResult ParallelLine(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, GeometryResult> results,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var point = RequiredPoint(results, item.PointA);
        var source = RelationSourceLine(item, results, parameters);
        if (source.Direction.Length < GeometryAlgorithms.Epsilon) throw new InvalidOperationException("基准直线退化。");
        return new GeometryResult
        {
            ObjectId = item.Id,
            Type = item.Type,
            IsValid = true,
            Line = new LineGeometry(point, source.Direction)
        };
    }

    private static LineGeometry RelationSourceLine(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, GeometryResult> results,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        if (!string.IsNullOrWhiteSpace(item.ObjectA))
            return results[item.ObjectA].Line ?? throw new InvalidOperationException("objectA 不是有效直线对象。");
        if (item.GridAxis == "x")
            return new LineGeometry(new Point2(Eval(item.GridValue, parameters), 0), new Vector2(0, 1));
        if (item.GridAxis == "y")
            return new LineGeometry(new Point2(0, Eval(item.GridValue, parameters)), new Vector2(1, 0));
        throw new InvalidOperationException("需要 objectA，或 gridAxis + gridValue 作为基准线。");
    }

    private static GeometryResult Circle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var center = RequiredPoint(results, item.Center);
        var radius = Eval(item.Radius, parameters);
        if (radius <= 0) throw new InvalidOperationException("半径必须大于零。");
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Circle = new CircleGeometry(center, radius) };
    }

    private static GeometryResult DiameterCircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var center = new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Circle = new CircleGeometry(center, a.DistanceTo(b) / 2) };
    }

    private static GeometryResult CenterPointCircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var center = RequiredPoint(results, item.Center);
        var edge = RequiredPoint(results, item.PointA);
        var radius = center.DistanceTo(edge);
        if (radius <= GeometryAlgorithms.Epsilon) throw new InvalidOperationException("圆心与圆上一点重合，无法定义圆。");
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Circle = new CircleGeometry(center, radius) };
    }

    private static GeometryResult CenterDistanceCircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var center = RequiredPoint(results, item.Center);
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var radius = a.DistanceTo(b);
        if (radius <= GeometryAlgorithms.Epsilon) throw new InvalidOperationException("用于确定半径的两个点不能重合。");
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Circle = new CircleGeometry(center, radius) };
    }

    private static GeometryResult ThreePointCircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var c = RequiredPoint(results, item.PointC);
        var determinant = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (System.Math.Abs(determinant) < GeometryAlgorithms.Epsilon)
            throw new InvalidOperationException("三点共线，无法定义外接圆。");
        var aa = a.X * a.X + a.Y * a.Y;
        var bb = b.X * b.X + b.Y * b.Y;
        var cc = c.X * c.X + c.Y * c.Y;
        var center = new Point2(
            (aa * (b.Y - c.Y) + bb * (c.Y - a.Y) + cc * (a.Y - b.Y)) / determinant,
            (aa * (c.X - b.X) + bb * (a.X - c.X) + cc * (b.X - a.X)) / determinant);
        return new GeometryResult
        {
            ObjectId = item.Id,
            Type = item.Type,
            IsValid = true,
            Circle = new CircleGeometry(center, center.DistanceTo(a))
        };
    }

    private static GeometryResult Arc(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var center = RequiredPoint(results, item.Center);
        var radius = Eval(item.Radius, parameters);
        var start = Eval(item.StartAngle, parameters) * System.Math.PI / 180;
        var end = Eval(item.EndAngle, parameters) * System.Math.PI / 180;
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Arc = new ArcGeometry(center, radius, start, GeometryAlgorithms.NormalizeAngle(end - start)) };
    }

    private static GeometryResult Semicircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var a = RequiredPoint(results, item.PointA);
        var b = RequiredPoint(results, item.PointB);
        var center = new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var radius = a.DistanceTo(b) / 2;
        var angleA = System.Math.Atan2(a.Y - center.Y, a.X - center.X);
        var angleB = System.Math.Atan2(b.Y - center.Y, b.X - center.X);
        var orientation = item.Orientation?.ToLowerInvariant() ?? "upper";
        var candidateFromA = new ArcGeometry(center, radius, angleA, System.Math.PI);
        var midpointA = new Point2(center.X + radius * System.Math.Cos(angleA + System.Math.PI / 2), center.Y + radius * System.Math.Sin(angleA + System.Math.PI / 2));
        var chooseA = orientation switch
        {
            "upper" => midpointA.Y >= center.Y,
            "lower" => midpointA.Y <= center.Y,
            "left" => midpointA.X <= center.X,
            "right" => midpointA.X >= center.X,
            _ => throw new InvalidOperationException($"未知半圆方向“{orientation}”。")
        };
        var arc = chooseA ? candidateFromA : new ArcGeometry(center, radius, angleB, System.Math.PI);
        return new GeometryResult { ObjectId = item.Id, Type = item.Type, IsValid = true, Arc = arc };
    }

    private static GeometryResult IntersectionPoint(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results)
    {
        var a = results[item.ObjectA!];
        var b = results[item.ObjectB!];
        var points = GeometryAlgorithms.Intersections(a, b).ToList();
        if (points.Count == 0)
            throw new InvalidOperationException(
                $"{item.ObjectA} 与 {item.ObjectB} 当前没有可用交点" +
                $"（selector={item.Selector ?? "first"}）。" +
                "这通常表示两对象平行、相离，或交点超出了线段/射线范围。");
        var chosen = SelectIntersection(item, results, points);
        return Point(item, chosen.X, chosen.Y);
    }

    private static GeometryResult GridIntersectionPoint(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, GeometryResult> results,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var source = results[item.ObjectA!];
        var value = Eval(item.GridValue, parameters);
        var axis = item.GridAxis?.ToLowerInvariant();
        var gridLine = axis switch
        {
            "x" => new LineGeometry(new Point2(value, 0), new Vector2(0, 1)),
            "y" => new LineGeometry(new Point2(0, value), new Vector2(1, 0)),
            _ => throw new InvalidOperationException("gridAxis 必须为 x 或 y。")
        };
        var synthetic = new GeometryResult
        {
            ObjectId = $"grid:{axis}={value}",
            Type = "gridLine",
            IsValid = true,
            Line = gridLine
        };
        var points = GeometryAlgorithms.Intersections(source, synthetic).ToList();
        if (points.Count == 0)
            throw new InvalidOperationException($"{item.ObjectA} 与网格线 {axis}={value:G6} 没有交点。");
        var chosen = SelectIntersection(item, results, points);
        return Point(item, chosen.X, chosen.Y);
    }

    private static Point2 SelectIntersection(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, GeometryResult> results,
        IReadOnlyList<Point2> points)
    {
        var selector = item.Selector?.ToLowerInvariant() ?? "first";
        return selector switch
        {
            "first" => points[0],
            "second" => points.Count > 1 ? points[1] : points[0],
            "upper" => points.MaxBy(p => p.Y),
            "lower" => points.MinBy(p => p.Y),
            "left" => points.MinBy(p => p.X),
            "right" => points.MaxBy(p => p.X),
            "closestto" => points.MinBy(p => p.DistanceTo(RequiredPoint(results, item.ClosestTo))),
            _ => throw new InvalidOperationException($"未知交点选择器“{selector}”。")
        };
    }

    private static GeometryResult PointOnLine(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var support = results[item.Support!].Line ?? throw new InvalidOperationException("support 不是线对象。");
        var t = parameters[item.Parameter!].Approximate;
        var point = support.At(t);
        return Point(item, point.X, point.Y);
    }

    private static GeometryResult PointOnGridLine(
        GeometryObjectDefinition item,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var fixedCoordinate = Eval(item.GridValue, parameters);
        var movingCoordinate = parameters[item.Parameter!].Approximate;
        return item.GridAxis?.ToLowerInvariant() switch
        {
            "x" => Point(item, fixedCoordinate, movingCoordinate),
            "y" => Point(item, movingCoordinate, fixedCoordinate),
            _ => throw new InvalidOperationException("gridAxis 必须为 x 或 y。")
        };
    }

    private static GeometryResult PointOnCircle(GeometryObjectDefinition item, IReadOnlyDictionary<string, GeometryResult> results, IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var support = results[item.Support!];
        var center = support.Circle?.Center ?? support.Arc?.Center ?? throw new InvalidOperationException("support 不是圆或圆弧。");
        var radius = support.Circle?.Radius ?? support.Arc!.Radius;
        var t = parameters[item.Parameter!].Approximate;
        var start = support.Arc?.StartAngleRadians ?? 0;
        var sweep = support.Arc?.SweepRadians ?? (2 * System.Math.PI);
        var angle = start + t * sweep;
        return Point(item, center.X + radius * System.Math.Cos(angle), center.Y + radius * System.Math.Sin(angle));
    }
}
