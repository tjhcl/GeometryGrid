using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Rendering;

namespace GeometryGrid.Export;

public static class EmfExporter
{
    public static void Export(RenderScene scene, string path, int width, int height, string textMode = "text")
    {
        using var referenceBitmap = new Bitmap(1, 1);
        using var referenceGraphics = Graphics.FromImage(referenceBitmap);
        var hdc = referenceGraphics.GetHdc();
        try
        {
            using var metafile = new Metafile(path, hdc, new Rectangle(0, 0, width, height), MetafileFrameUnit.Pixel, EmfType.EmfPlusDual);
            using var graphics = Graphics.FromImage(metafile);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Draw(graphics, scene, width, height, textMode);
        }
        finally { referenceGraphics.ReleaseHdc(hdc); }
    }

    private static void Draw(Graphics graphics, RenderScene scene, int width, int height, string textMode)
    {
        var layout = new SceneLayout(scene.Canvas, width, height);
        graphics.Clear(ColorUtility.Gdi(scene.Canvas.Background));
        DrawGrid(graphics, scene, layout);
        foreach (var item in scene.Items.Where(i => i.Style.Visible))
            DrawItem(graphics, scene, layout, item, textMode);
    }

    private static void DrawGrid(Graphics graphics, RenderScene scene, SceneLayout layout)
    {
        var bounds = scene.Canvas;
        if (scene.Grid.Visible)
        {
            using var pen = NewPen(scene.Grid.Color, scene.Grid.StrokeWidth * layout.VisualScale, 1, "solid");
            for (var x = bounds.MinX; x <= bounds.MaxX + 1e-9; x += scene.Grid.Step)
                graphics.DrawLine(pen, layout.ToGdi(new Point2(x, bounds.MinY)), layout.ToGdi(new Point2(x, bounds.MaxY)));
            for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9; y += scene.Grid.Step)
                graphics.DrawLine(pen, layout.ToGdi(new Point2(bounds.MinX, y)), layout.ToGdi(new Point2(bounds.MaxX, y)));
        }
        using var border = NewPen(scene.Grid.Color, scene.Grid.BorderWidth * layout.VisualScale, 1, "solid");
        var topLeft = layout.ToGdi(new Point2(bounds.MinX, bounds.MaxY));
        graphics.DrawRectangle(border, topLeft.X, topLeft.Y, (float)((bounds.MaxX - bounds.MinX) * layout.Scale), (float)((bounds.MaxY - bounds.MinY) * layout.Scale));
    }

    private static void DrawItem(Graphics graphics, RenderScene scene, SceneLayout layout, RenderItem item, string textMode)
    {
        var style = item.Style;
        using var pen = NewPen(style.Color, style.StrokeWidth * layout.VisualScale, style.Opacity, style.StrokeStyle);
        if (item.Geometry.Line is { } line && layout.ClipLine(line, scene.Canvas) is { } clipped)
            graphics.DrawLine(pen, layout.ToGdi(clipped.Start), layout.ToGdi(clipped.End));
        else if (item.Geometry.Circle is { } circle)
        {
            var center = layout.ToGdi(circle.Center);
            var radius = (float)(circle.Radius * layout.Scale);
            graphics.DrawEllipse(pen, center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
        }
        else if (item.Geometry.Arc is { } arc)
        {
            var center = layout.ToGdi(arc.Center);
            var radius = (float)(arc.Radius * layout.Scale);
            graphics.DrawArc(pen, center.X - radius, center.Y - radius, 2 * radius, 2 * radius,
                (float)(-arc.StartAngleRadians * 180 / System.Math.PI),
                (float)(-arc.SweepRadians * 180 / System.Math.PI));
        }
        else if (item.Geometry.Point is { } point)
        {
            var center = layout.ToGdi(point);
            var radius = (float)(style.PointRadius * layout.VisualScale);
            using var brush = new SolidBrush(ColorUtility.Gdi(style.Color, style.Opacity));
            graphics.FillEllipse(brush, center.X - radius, center.Y - radius, radius * 2, radius * 2);
        }
        if (item.Label is { Visible: true } label && GeometryLabelAnchor.Get(item) is { } anchor)
        {
            var position = layout.ToGdi(anchor);
            using var brush = new SolidBrush(ColorUtility.Gdi(style.Color, style.Opacity));
            DrawLabel(graphics, label.Text,
                position.X + (float)(label.OffsetX * layout.VisualScale),
                position.Y + (float)(label.OffsetY * layout.VisualScale),
                (float)(label.FontSize * layout.VisualScale), brush, textMode);
        }
    }

    private static Pen NewPen(string color, double width, double opacity, string style)
    {
        var pen = new Pen(ColorUtility.Gdi(color, opacity), (float)width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        pen.DashStyle = style.ToLowerInvariant() switch { "dash" => DashStyle.Dash, "dot" => DashStyle.Dot, _ => DashStyle.Solid };
        return pen;
    }

    private static void DrawLabel(Graphics graphics, string text, float x, float y, float fontSize, Brush brush, string textMode)
    {
        using var regular = new Font("Times New Roman", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var italic = new Font("Times New Roman", fontSize, FontStyle.Italic, GraphicsUnit.Pixel);
        var cursor = x;
        foreach (var token in Tokenize(text))
        {
            var font = token.Italic ? italic : regular;
            if (textMode.Equals("outline", StringComparison.OrdinalIgnoreCase))
            {
                using var path = new GraphicsPath();
                path.AddString(token.Text, font.FontFamily, (int)font.Style, font.Size, new PointF(cursor, y), StringFormat.GenericTypographic);
                graphics.FillPath(brush, path);
                cursor += graphics.MeasureString(token.Text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
            }
            else
            {
                graphics.DrawString(token.Text, font, brush, new PointF(cursor, y), StringFormat.GenericTypographic);
                cursor += graphics.MeasureString(token.Text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
            }
        }
    }

    private static IEnumerable<(string Text, bool Italic)> Tokenize(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            if (i + 4 <= text.Length && text.Substring(i, 4).Equals("sqrt", StringComparison.OrdinalIgnoreCase))
            {
                yield return (text.Substring(i, 4), false);
                i += 4;
            }
            else
            {
                var italic = char.IsLetter(text[i]);
                yield return (text[i].ToString(), italic);
                i++;
            }
        }
    }
}
