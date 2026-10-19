using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Export;

public sealed class SceneLayout(CanvasSettings canvas, double width, double height)
{
    public double Width { get; } = width;
    public double Height { get; } = height;
    public double Scale { get; } = System.Math.Min(
        System.Math.Max(1, width - 2 * canvas.Padding) / (canvas.MaxX - canvas.MinX),
        System.Math.Max(1, height - 2 * canvas.Padding) / (canvas.MaxY - canvas.MinY));
    public double OriginX { get; } = (width - (canvas.MaxX - canvas.MinX) * System.Math.Min(
        System.Math.Max(1, width - 2 * canvas.Padding) / (canvas.MaxX - canvas.MinX),
        System.Math.Max(1, height - 2 * canvas.Padding) / (canvas.MaxY - canvas.MinY))) / 2 - canvas.MinX * System.Math.Min(
        System.Math.Max(1, width - 2 * canvas.Padding) / (canvas.MaxX - canvas.MinX),
        System.Math.Max(1, height - 2 * canvas.Padding) / (canvas.MaxY - canvas.MinY));
    public double OriginY { get; } = (height - (canvas.MaxY - canvas.MinY) * System.Math.Min(
        System.Math.Max(1, width - 2 * canvas.Padding) / (canvas.MaxX - canvas.MinX),
        System.Math.Max(1, height - 2 * canvas.Padding) / (canvas.MaxY - canvas.MinY))) / 2 + canvas.MaxY * System.Math.Min(
        System.Math.Max(1, width - 2 * canvas.Padding) / (canvas.MaxX - canvas.MinX),
        System.Math.Max(1, height - 2 * canvas.Padding) / (canvas.MaxY - canvas.MinY));
    // Visual styling is tied to the physical viewport, not to the mathematical
    // canvas range. Changing min/max therefore changes geometry placement only;
    // stroke widths, point radii, labels and grid thickness remain stable.
    public double VisualScale { get; } =
        System.Math.Min(
            System.Math.Max(1, width - 2 * canvas.Padding),
            System.Math.Max(1, height - 2 * canvas.Padding))
        / System.Math.Max(1, 800 - 2 * canvas.Padding);

    public System.Windows.Point ToScreen(Point2 point) => new(OriginX + point.X * Scale, OriginY - point.Y * Scale);
    public System.Drawing.PointF ToGdi(Point2 point) => new((float)(OriginX + point.X * Scale), (float)(OriginY - point.Y * Scale));

    public (Point2 Start, Point2 End)? ClipLine(LineGeometry line, CanvasSettings bounds)
    {
        var candidates = new List<(double T, Point2 P)>();
        void Add(double t)
        {
            if (!line.ContainsParameter(t)) return;
            var p = line.At(t);
            if (p.X >= bounds.MinX - 1e-8 && p.X <= bounds.MaxX + 1e-8 && p.Y >= bounds.MinY - 1e-8 && p.Y <= bounds.MaxY + 1e-8)
                candidates.Add((t, p));
        }
        if (System.Math.Abs(line.Direction.X) > GeometryAlgorithms.Epsilon)
        {
            Add((bounds.MinX - line.Origin.X) / line.Direction.X);
            Add((bounds.MaxX - line.Origin.X) / line.Direction.X);
        }
        if (System.Math.Abs(line.Direction.Y) > GeometryAlgorithms.Epsilon)
        {
            Add((bounds.MinY - line.Origin.Y) / line.Direction.Y);
            Add((bounds.MaxY - line.Origin.Y) / line.Direction.Y);
        }
        if (double.IsFinite(line.MinParameter)) Add(line.MinParameter);
        if (double.IsFinite(line.MaxParameter)) Add(line.MaxParameter);
        var unique = candidates.OrderBy(c => c.T).Select(c => c.P).DistinctBy(p => (System.Math.Round(p.X, 8), System.Math.Round(p.Y, 8))).ToList();
        return unique.Count >= 2 ? (unique.First(), unique.Last()) : null;
    }
}
