namespace GeometryGrid.App.ViewModels;

using GeometryGrid.Core.Geometry;

public static class DrawingTools
{
    public const string Select = "select";
    public const string Point = "point";
    public const string Segment = "segment";
    public const string Ray = "ray";
    public const string Line = "line";
    public const string Midpoint = "midpoint";
    public const string Perpendicular = "perpendicular";
    public const string Parallel = "parallel";
    public const string CircleDiameter = "circleDiameter";
    public const string CircleRadiusSegment = "circleRadiusSegment";
    public const string CircleCenterRadius = "circleCenterRadius";
    public const string CircleCenterDistance = "circleCenterDistance";
    public const string CircleThreePoints = "circleThreePoints";
}

public readonly record struct GridLineReference(string Axis, double Value)
{
    public override string ToString() => $"{Axis}={Value:G6}";
}

public sealed record DrawingPreview(Point2 Candidate, LineGeometry? Line = null, CircleGeometry? Circle = null);

public sealed record ConstructionPlaybackVisual(
    ConstructionDisplayKind Kind,
    Point2 Start,
    Point2 Guide,
    Point2 End,
    string Color,
    double Progress,
    double Pulse,
    IReadOnlyList<Point2> FocusPoints,
    IReadOnlyList<Point2> RevealedPoints);
