namespace GeometryGrid.Core.Geometry;

public readonly record struct Point2(double X, double Y)
{
    public double DistanceTo(Point2 other) => System.Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
    public static Point2 operator +(Point2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
}

public readonly record struct Vector2(double X, double Y)
{
    public double Length => System.Math.Sqrt(X * X + Y * Y);
    public Vector2 Normalized => Length < GeometryAlgorithms.Epsilon ? new(0, 0) : new(X / Length, Y / Length);
    public static Vector2 operator *(Vector2 a, double value) => new(a.X * value, a.Y * value);
    public static Vector2 operator /(Vector2 a, double value) => new(a.X / value, a.Y / value);
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static double Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}

public sealed record LineGeometry(Point2 Origin, Vector2 Direction, double MinParameter = double.NegativeInfinity, double MaxParameter = double.PositiveInfinity)
{
    public Point2 At(double t) => Origin + Direction * t;
    public bool ContainsParameter(double t) => t >= MinParameter - GeometryAlgorithms.Epsilon && t <= MaxParameter + GeometryAlgorithms.Epsilon;
}

public sealed record CircleGeometry(Point2 Center, double Radius);

public sealed record ArcGeometry(Point2 Center, double Radius, double StartAngleRadians, double SweepRadians)
{
    public bool Contains(Point2 point)
    {
        var angle = System.Math.Atan2(point.Y - Center.Y, point.X - Center.X);
        var delta = GeometryAlgorithms.NormalizeAngle(angle - StartAngleRadians);
        if (SweepRadians >= 0) return delta <= SweepRadians + GeometryAlgorithms.Epsilon;
        return GeometryAlgorithms.NormalizeAngle(StartAngleRadians - angle) <= -SweepRadians + GeometryAlgorithms.Epsilon;
    }
}

public sealed class GeometryResult
{
    public required string ObjectId { get; init; }
    public required string Type { get; init; }
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public Point2? Point { get; init; }
    public LineGeometry? Line { get; init; }
    public CircleGeometry? Circle { get; init; }
    public ArcGeometry? Arc { get; init; }

    public static GeometryResult Invalid(string id, string type, string error) =>
        new() { ObjectId = id, Type = type, IsValid = false, Error = error };
}

public static class GeometryAlgorithms
{
    public const double Epsilon = 1e-9;

    public static IReadOnlyList<Point2> Intersections(GeometryResult a, GeometryResult b)
    {
        if (!a.IsValid || !b.IsValid) return [];
        if (a.Line is not null && b.Line is not null) return LineLine(a.Line, b.Line);
        if (a.Line is not null && (b.Circle is not null || b.Arc is not null)) return LineCircle(a.Line, b.Circle ?? new CircleGeometry(b.Arc!.Center, b.Arc.Radius), b.Arc);
        if (b.Line is not null && (a.Circle is not null || a.Arc is not null)) return LineCircle(b.Line, a.Circle ?? new CircleGeometry(a.Arc!.Center, a.Arc.Radius), a.Arc);
        if ((a.Circle is not null || a.Arc is not null) && (b.Circle is not null || b.Arc is not null))
            return CircleCircle(
                a.Circle ?? new CircleGeometry(a.Arc!.Center, a.Arc.Radius), a.Arc,
                b.Circle ?? new CircleGeometry(b.Arc!.Center, b.Arc.Radius), b.Arc);
        return [];
    }

    public static IReadOnlyList<Point2> LineLine(LineGeometry a, LineGeometry b)
    {
        var denominator = Vector2.Cross(a.Direction, b.Direction);
        if (System.Math.Abs(denominator) < Epsilon) return [];
        var difference = b.Origin - a.Origin;
        var ta = Vector2.Cross(difference, b.Direction) / denominator;
        var tb = Vector2.Cross(difference, a.Direction) / denominator;
        return a.ContainsParameter(ta) && b.ContainsParameter(tb) ? [a.At(ta)] : [];
    }

    public static IReadOnlyList<Point2> LineCircle(LineGeometry line, CircleGeometry circle, ArcGeometry? arc = null)
    {
        var d = line.Direction;
        var f = line.Origin - circle.Center;
        var qa = Vector2.Dot(d, d);
        if (qa < Epsilon) return [];
        var qb = 2 * Vector2.Dot(f, d);
        var qc = Vector2.Dot(f, f) - circle.Radius * circle.Radius;
        var discriminant = qb * qb - 4 * qa * qc;
        if (discriminant < -Epsilon) return [];
        var roots = new List<double>();
        if (System.Math.Abs(discriminant) < Epsilon) roots.Add(-qb / (2 * qa));
        else
        {
            var sqrt = System.Math.Sqrt(System.Math.Max(0, discriminant));
            roots.Add((-qb - sqrt) / (2 * qa));
            roots.Add((-qb + sqrt) / (2 * qa));
        }
        return roots.Where(line.ContainsParameter).Select(line.At).Where(p => arc is null || arc.Contains(p)).ToList();
    }

    public static IReadOnlyList<Point2> CircleCircle(CircleGeometry a, ArcGeometry? arcA, CircleGeometry b, ArcGeometry? arcB)
    {
        var delta = b.Center - a.Center;
        var distance = delta.Length;
        if (distance < Epsilon || distance > a.Radius + b.Radius + Epsilon || distance < System.Math.Abs(a.Radius - b.Radius) - Epsilon) return [];
        var along = (a.Radius * a.Radius - b.Radius * b.Radius + distance * distance) / (2 * distance);
        var heightSquared = a.Radius * a.Radius - along * along;
        if (heightSquared < -Epsilon) return [];
        var unit = delta / distance;
        var basePoint = a.Center + unit * along;
        var perpendicular = new Vector2(-unit.Y, unit.X);
        var height = System.Math.Sqrt(System.Math.Max(0, heightSquared));
        var points = height < Epsilon ? new List<Point2> { basePoint } : [basePoint + perpendicular * height, basePoint + perpendicular * -height];
        return points.Where(p => (arcA is null || arcA.Contains(p)) && (arcB is null || arcB.Contains(p))).ToList();
    }

    public static double NormalizeAngle(double value)
    {
        var tau = 2 * System.Math.PI;
        value %= tau;
        if (value < 0) value += tau;
        return value;
    }
}
