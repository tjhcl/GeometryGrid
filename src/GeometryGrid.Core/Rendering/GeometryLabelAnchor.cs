using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Core.Rendering;

public static class GeometryLabelAnchor
{
    public static Point2? Get(RenderItem item)
    {
        if (item.Geometry.Point is { } point) return point;
        if (item.Geometry.Line is { } line)
        {
            if (double.IsFinite(line.MinParameter) && double.IsFinite(line.MaxParameter))
                return line.At((line.MinParameter + line.MaxParameter) / 2);
            return line.Origin;
        }
        if (item.Geometry.Circle is { } circle)
            return new Point2(circle.Center.X + circle.Radius, circle.Center.Y);
        if (item.Geometry.Arc is { } arc)
        {
            var angle = arc.StartAngleRadians + arc.SweepRadians / 2;
            return new Point2(
                arc.Center.X + arc.Radius * System.Math.Cos(angle),
                arc.Center.Y + arc.Radius * System.Math.Sin(angle));
        }
        return null;
    }
}
