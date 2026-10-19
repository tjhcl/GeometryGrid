using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Rendering;
using FlowDirection = System.Windows.FlowDirection;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace GeometryGrid.Export;

public static class RasterExporter
{
    public static void ExportPng(RenderScene scene, string path, int width, int height, double dpi, bool transparent)
    {
        var bitmap = Render(scene, width, height, dpi, transparent);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void ExportJpeg(RenderScene scene, string path, int width, int height, double dpi, int quality, string background)
    {
        var original = scene.Canvas.Background;
        scene.Canvas.Background = background;
        try
        {
            var bitmap = Render(scene, width, height, dpi, false);
            var encoder = new JpegBitmapEncoder { QualityLevel = System.Math.Clamp(quality, 1, 100) };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        finally { scene.Canvas.Background = original; }
    }

    public static RenderTargetBitmap Render(RenderScene scene, int width, int height, double dpi, bool transparent)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "导出尺寸必须大于 0。");
        if (!double.IsFinite(dpi) || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi), "导出 DPI 必须大于 0。");
        // WPF drawing coordinates use 96-DPI device-independent pixels. RenderTargetBitmap
        // applies the requested DPI again, so the drawing viewport must be converted from
        // output pixels to DIPs. Otherwise 144-DPI exports are enlarged to 150% and clipped.
        var dipWidth = width * 96d / dpi;
        var dipHeight = height * 96d / dpi;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            DrawScene(drawing, scene, dipWidth, dipHeight, transparent);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static RenderTargetBitmap RenderVideoFrame(RenderScene scene, int width, int height, double contentAspectRatio)
    {
        if (!double.IsFinite(contentAspectRatio) || contentAspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentAspectRatio));
        var contentWidth = (double)width;
        var contentHeight = contentWidth / contentAspectRatio;
        if (contentHeight > height) { contentHeight = height; contentWidth = height * contentAspectRatio; }
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(ColorUtility.Wpf(scene.Canvas.Background)), null,
                new Rect(0, 0, width, height));
            drawing.PushTransform(new TranslateTransform((width - contentWidth) / 2, (height - contentHeight) / 2));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, contentWidth, contentHeight)));
            DrawScene(drawing, scene, contentWidth, contentHeight, false);
            drawing.Pop(); drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    public static void DrawScene(DrawingContext drawing, RenderScene scene, double width, double height, bool transparent)
    {
        // A fixed logical animation viewport makes GIF, HD and 4K identical in composition;
        // only raster resolution changes, not padding, typography or marker proportions.
        if (!string.IsNullOrEmpty(scene.AnimationCaption))
        {
            var scale = width / 960d;
            drawing.PushTransform(new ScaleTransform(scale, scale));
            DrawSceneCore(drawing, scene, 960, height / scale, transparent);
            drawing.Pop();
        }
        else DrawSceneCore(drawing, scene, width, height, transparent);
    }

    private static void DrawSceneCore(DrawingContext drawing, RenderScene scene, double width, double height, bool transparent)
    {
        var captionHeight = string.IsNullOrEmpty(scene.AnimationCaption) ? 0 : 60 * width / 960d;
        var layout = new SceneLayout(scene.Canvas, width, height - captionHeight);
        if (!transparent)
            drawing.DrawRectangle(new SolidColorBrush(ColorUtility.Wpf(scene.Canvas.Background)), null, new Rect(0, 0, width, height));

        DrawGrid(drawing, scene, layout);
        foreach (var item in scene.Items.Where(i => i.Style.Visible))
            DrawItem(drawing, scene, layout, item);
        if (captionHeight > 0)
        {
            var margin = 20 * width / 960d;
            var text = new FormattedText(scene.AnimationCaption!, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI, Microsoft YaHei"), 15 * width / 960d,
                new SolidColorBrush(Color.FromRgb(30, 50, 80)), 1)
            {
                MaxTextWidth = width - margin * 2,
                MaxTextHeight = captionHeight - margin / 2,
                Trimming = TextTrimming.CharacterEllipsis
            };
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(239, 245, 255)), null,
                new Rect(margin / 2, height - captionHeight, width - margin, captionHeight - margin / 2),
                12 * width / 960d, 12 * width / 960d);
            drawing.DrawText(text, new Point(margin, height - captionHeight + margin / 2));
        }
    }

    private static void DrawGrid(DrawingContext drawing, RenderScene scene, SceneLayout layout)
    {
        var bounds = scene.Canvas;
        var grid = scene.Grid;
        if (grid.Visible)
        {
            var pen = NewPen(grid.Color, grid.StrokeWidth * layout.VisualScale, 1, "solid");
            for (var x = bounds.MinX; x <= bounds.MaxX + 1e-9; x += grid.Step)
                drawing.DrawLine(pen, layout.ToScreen(new Point2(x, bounds.MinY)), layout.ToScreen(new Point2(x, bounds.MaxY)));
            for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9; y += grid.Step)
                drawing.DrawLine(pen, layout.ToScreen(new Point2(bounds.MinX, y)), layout.ToScreen(new Point2(bounds.MaxX, y)));
        }
        var border = NewPen(grid.Color, grid.BorderWidth * layout.VisualScale, 1, "solid");
        var topLeft = layout.ToScreen(new Point2(bounds.MinX, bounds.MaxY));
        var bottomRight = layout.ToScreen(new Point2(bounds.MaxX, bounds.MinY));
        drawing.DrawRectangle(null, border, new Rect(topLeft, bottomRight));
    }

    private static void DrawItem(DrawingContext drawing, RenderScene scene, SceneLayout layout, RenderItem item)
    {
        var geometry = item.Geometry;
        var style = item.Style;
        var pen = NewPen(style.Color, style.StrokeWidth * layout.VisualScale, style.Opacity, style.StrokeStyle);
        if (geometry.Line is { } line && layout.ClipLine(line, scene.Canvas) is { } clipped)
            drawing.DrawLine(pen, layout.ToScreen(clipped.Start), layout.ToScreen(clipped.End));
        else if (geometry.Circle is { } circle)
            drawing.DrawEllipse(null, pen, layout.ToScreen(circle.Center), circle.Radius * layout.Scale, circle.Radius * layout.Scale);
        else if (geometry.Arc is { } arc)
            DrawArc(drawing, layout, arc, pen);
        else if (geometry.Point is { } point)
        {
            var center = layout.ToScreen(point);
            drawing.DrawEllipse(new SolidColorBrush(ColorUtility.Wpf(style.Color, style.Opacity)), null, center,
                style.PointRadius * layout.VisualScale, style.PointRadius * layout.VisualScale);
        }
        if (item.Label is { Visible: true } label && GeometryLabelAnchor.Get(item) is { } anchor)
        {
            var position = layout.ToScreen(anchor);
            DrawLabel(drawing, label.Text,
                new System.Windows.Point(position.X + label.OffsetX * layout.VisualScale, position.Y + label.OffsetY * layout.VisualScale),
                label.FontSize * layout.VisualScale, style.Color, style.Opacity);
        }
    }

    private static System.Windows.Media.Pen NewPen(string color, double width, double opacity, string style)
    {
        var pen = new System.Windows.Media.Pen(new SolidColorBrush(ColorUtility.Wpf(color, opacity)), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        if (style.Equals("dash", StringComparison.OrdinalIgnoreCase)) pen.DashStyle = DashStyles.Dash;
        else if (style.Equals("dot", StringComparison.OrdinalIgnoreCase)) pen.DashStyle = DashStyles.Dot;
        pen.Freeze();
        return pen;
    }

    private static void DrawArc(DrawingContext drawing, SceneLayout layout, ArcGeometry arc, System.Windows.Media.Pen pen)
    {
        var start = new Point2(arc.Center.X + arc.Radius * System.Math.Cos(arc.StartAngleRadians), arc.Center.Y + arc.Radius * System.Math.Sin(arc.StartAngleRadians));
        var endAngle = arc.StartAngleRadians + arc.SweepRadians;
        var end = new Point2(arc.Center.X + arc.Radius * System.Math.Cos(endAngle), arc.Center.Y + arc.Radius * System.Math.Sin(endAngle));
        var figure = new PathFigure { StartPoint = layout.ToScreen(start), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(layout.ToScreen(end), new System.Windows.Size(arc.Radius * layout.Scale, arc.Radius * layout.Scale),
            0, System.Math.Abs(arc.SweepRadians) > System.Math.PI, arc.SweepRadians >= 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
        var path = new PathGeometry([figure]);
        drawing.DrawGeometry(null, pen, path);
    }

    private static void DrawLabel(DrawingContext drawing, string text, System.Windows.Point origin, double fontSize, string color, double opacity)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Times New Roman"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, new SolidColorBrush(ColorUtility.Wpf(color, opacity)), 1);
        for (var i = 0; i < text.Length; i++)
        {
            if (IsOperatorWord(text, i))
            {
                formatted.SetFontStyle(FontStyles.Normal, i, 4);
                i += 3;
            }
            else if (char.IsLetter(text[i]))
                formatted.SetFontStyle(FontStyles.Italic, i, 1);
        }
        drawing.DrawText(formatted, origin);
    }

    private static bool IsOperatorWord(string text, int index) =>
        index + 4 <= text.Length && text.Substring(index, 4).Equals("sqrt", StringComparison.OrdinalIgnoreCase);
}
