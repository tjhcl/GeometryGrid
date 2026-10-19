using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Rendering;
using GeometryGrid.Export;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;

namespace GeometryGrid.App;

public sealed class GeometryCanvas : FrameworkElement
{
    private MainViewModel? _viewModel;
    private double _zoom = 1;
    private Vector _pan;
    private Point _lastMouse;
    private DragMode _dragMode;
    private string? _dragId;
    private double _startOffsetX;
    private double _startOffsetY;
    private string? _hoverId;
    private IReadOnlyList<GridLineReference> _hoverGridLines = [];

    public GeometryCanvas()
    {
        ClipToBounds = true;
        DataContextChanged += OnDataContextChanged;
        MouseWheel += OnMouseWheel;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.SceneChanged -= OnSceneChanged;
        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null) _viewModel.SceneChanged += OnSceneChanged;
        UpdateCursor();
        InvalidateVisual();
    }

    private void OnSceneChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        UpdateCursor();
        InvalidateVisual();
    });

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_viewModel?.Evaluation?.Scene is not { } scene) return;
        var layout = new SceneLayout(scene.Canvas, ActualWidth, ActualHeight);
        DrawGrid(drawing, scene, layout);
        foreach (var item in scene.Items.Where(i => i.Style.Visible))
            DrawItem(drawing, scene, layout, item, item.Id == _viewModel.SelectedObject?.Id,
                item.Id == _hoverId || item.Id == _viewModel.PendingRelationLineId);
        DrawConstructionPlayback(drawing, layout);
        DrawPreview(drawing, scene, layout);
    }

    private Point Screen(Point2 world, SceneLayout layout)
    {
        var source = layout.ToScreen(world);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        return new Point(center.X + (source.X - center.X) * _zoom + _pan.X, center.Y + (source.Y - center.Y) * _zoom + _pan.Y);
    }

    private Point2 World(Point screen, SceneLayout layout)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var baseX = center.X + (screen.X - center.X - _pan.X) / _zoom;
        var baseY = center.Y + (screen.Y - center.Y - _pan.Y) / _zoom;
        return new Point2((baseX - layout.OriginX) / layout.Scale, (layout.OriginY - baseY) / layout.Scale);
    }

    internal static double StyleZoomFactor(double zoom) =>
        System.Math.Clamp(System.Math.Pow(zoom, .35), .68, 2.1);

    private double VisualScale(SceneLayout layout) => layout.VisualScale * StyleZoomFactor(_zoom);

    private void DrawGrid(DrawingContext drawing, RenderScene scene, SceneLayout layout)
    {
        var bounds = scene.Canvas;
        if (scene.Grid.Visible)
        {
            var pen = MakePen(scene.Grid.Color, scene.Grid.StrokeWidth * VisualScale(layout), 1, "solid");
            for (var x = bounds.MinX; x <= bounds.MaxX + 1e-9; x += scene.Grid.Step)
                drawing.DrawLine(pen, Screen(new Point2(x, bounds.MinY), layout), Screen(new Point2(x, bounds.MaxY), layout));
            for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9; y += scene.Grid.Step)
                drawing.DrawLine(pen, Screen(new Point2(bounds.MinX, y), layout), Screen(new Point2(bounds.MaxX, y), layout));

            var highlight = MakePen("#3B82F6", System.Math.Max(4, scene.Grid.StrokeWidth + 3) * VisualScale(layout), .58, "solid");
            var highlightedGridLines = _hoverGridLines
                .Concat(_viewModel?.PendingRelationGridLine is { } pending ? [pending] : [])
                .Distinct()
                .ToList();
            foreach (var line in highlightedGridLines)
            {
                if (line.Axis == "x")
                    drawing.DrawLine(highlight, Screen(new Point2(line.Value, bounds.MinY), layout), Screen(new Point2(line.Value, bounds.MaxY), layout));
                else if (line.Axis == "y")
                    drawing.DrawLine(highlight, Screen(new Point2(bounds.MinX, line.Value), layout), Screen(new Point2(bounds.MaxX, line.Value), layout));
            }
        }
        drawing.DrawRectangle(null, MakePen(scene.Grid.Color, scene.Grid.BorderWidth * VisualScale(layout), 1, "solid"),
            new Rect(Screen(new Point2(bounds.MinX, bounds.MaxY), layout), Screen(new Point2(bounds.MaxX, bounds.MinY), layout)));
    }

    private void DrawItem(DrawingContext drawing, RenderScene scene, SceneLayout layout, RenderItem item, bool selected, bool hovered)
    {
        var style = item.Style;
        var visualScale = VisualScale(layout);
        var pen = MakePen(style.Color, style.StrokeWidth * visualScale, style.Opacity, style.StrokeStyle);
        if ((selected || hovered) && item.Geometry.Point is null)
        {
            var highlight = new Pen(new SolidColorBrush(selected
                    ? Color.FromArgb(105, 37, 99, 235)
                    : Color.FromArgb(75, 14, 165, 233)),
                System.Math.Max((selected ? 8 : 5) * visualScale, style.StrokeWidth * visualScale + (selected ? 6 : 3) * visualScale))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            if (item.Geometry.Line is { } selectedLine && layout.ClipLine(selectedLine, scene.Canvas) is { } selectedClip)
            {
                drawing.DrawLine(highlight, Screen(selectedClip.Start, layout), Screen(selectedClip.End, layout));
                var handleRadius = System.Math.Max(4, 4 * visualScale);
                var handlePen = new Pen(Brushes.White, System.Math.Max(1, visualScale));
                drawing.DrawEllipse(Brushes.DodgerBlue, handlePen, Screen(selectedClip.Start, layout), handleRadius, handleRadius);
                drawing.DrawEllipse(Brushes.DodgerBlue, handlePen, Screen(selectedClip.End, layout), handleRadius, handleRadius);
            }
            else if (item.Geometry.Circle is { } selectedCircle)
                drawing.DrawEllipse(null, highlight, Screen(selectedCircle.Center, layout),
                    selectedCircle.Radius * layout.Scale * _zoom, selectedCircle.Radius * layout.Scale * _zoom);
            else if (item.Geometry.Arc is { } selectedArc)
                DrawArc(drawing, layout, selectedArc, highlight);
        }
        if (item.Geometry.Line is { } line && layout.ClipLine(line, scene.Canvas) is { } clipped)
        {
            drawing.DrawLine(pen, Screen(clipped.Start, layout), Screen(clipped.End, layout));
            if (item.Type == "ray" && double.IsFinite(line.MaxParameter))
                DrawArrowHead(drawing, Screen(clipped.Start, layout), Screen(clipped.End, layout), pen);
        }
        else if (item.Geometry.Circle is { } circle)
            drawing.DrawEllipse(null, pen, Screen(circle.Center, layout), circle.Radius * layout.Scale * _zoom, circle.Radius * layout.Scale * _zoom);
        else if (item.Geometry.Arc is { } arc)
            DrawArc(drawing, layout, arc, pen);
        else if (item.Geometry.Point is { } point)
        {
            var center = Screen(point, layout);
            drawing.DrawEllipse(MakeBrush(style.Color, style.Opacity), null, center,
                style.PointRadius * visualScale, style.PointRadius * visualScale);
            if (selected)
                drawing.DrawEllipse(null, new Pen(Brushes.DodgerBlue, System.Math.Max(1.5, 1.5 * visualScale)) { DashStyle = DashStyles.Dash },
                    center, (style.PointRadius + 6) * visualScale, (style.PointRadius + 6) * visualScale);
            else if (hovered)
                drawing.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(14, 165, 233)), System.Math.Max(1.5, 1.3 * visualScale)),
                    center, (style.PointRadius + 5) * visualScale, (style.PointRadius + 5) * visualScale);
        }
        if (item.Label is { Visible: true } label && GeometryLabelAnchor.Get(item) is { } anchor)
        {
            var origin = Screen(anchor, layout);
            var text = new FormattedText(label.Text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Times New Roman"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                label.FontSize * visualScale, MakeBrush(style.Color, style.Opacity), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            for (var i = 0; i < label.Text.Length; i++)
            {
                if (i + 4 <= label.Text.Length && label.Text.Substring(i, 4).Equals("sqrt", StringComparison.OrdinalIgnoreCase))
                {
                    text.SetFontStyle(FontStyles.Normal, i, 4);
                    i += 3;
                }
                else if (char.IsLetter(label.Text[i])) text.SetFontStyle(FontStyles.Italic, i, 1);
            }
            drawing.DrawText(text, new Point(origin.X + label.OffsetX * visualScale, origin.Y + label.OffsetY * visualScale));
        }
    }

    private void DrawArc(DrawingContext drawing, SceneLayout layout, ArcGeometry arc, Pen pen)
    {
        var start = new Point2(arc.Center.X + arc.Radius * System.Math.Cos(arc.StartAngleRadians), arc.Center.Y + arc.Radius * System.Math.Sin(arc.StartAngleRadians));
        var endAngle = arc.StartAngleRadians + arc.SweepRadians;
        var end = new Point2(arc.Center.X + arc.Radius * System.Math.Cos(endAngle), arc.Center.Y + arc.Radius * System.Math.Sin(endAngle));
        var figure = new PathFigure { StartPoint = Screen(start, layout) };
        figure.Segments.Add(new ArcSegment(Screen(end, layout), new Size(arc.Radius * layout.Scale * _zoom, arc.Radius * layout.Scale * _zoom),
            0, System.Math.Abs(arc.SweepRadians) > System.Math.PI, SweepDirection.Counterclockwise, true));
        drawing.DrawGeometry(null, pen, new PathGeometry([figure]));
    }

    private void DrawPreview(DrawingContext drawing, RenderScene scene, SceneLayout layout)
    {
        if (_viewModel?.Preview is not { } preview) return;
        var previewPen = MakePen("#2563EB", System.Math.Max(1.6, 2 * layout.VisualScale), .78, "dash");
        if (preview.Line is { } line && layout.ClipLine(line, scene.Canvas) is { } clipped)
            drawing.DrawLine(previewPen, Screen(clipped.Start, layout), Screen(clipped.End, layout));
        if (preview.Circle is { Radius: > GeometryAlgorithms.Epsilon } circle)
            drawing.DrawEllipse(null, previewPen, Screen(circle.Center, layout),
                circle.Radius * layout.Scale * _zoom, circle.Radius * layout.Scale * _zoom);

        var candidate = Screen(preview.Candidate, layout);
        var markerPen = new Pen(Brushes.White, System.Math.Max(1, layout.VisualScale));
        drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, 37, 99, 235)), markerPen, candidate, 5, 5);
    }

    private void DrawConstructionPlayback(DrawingContext drawing, SceneLayout layout)
    {
        if (_viewModel?.PlaybackVisual is not { } visual) return;
        var visualScale = VisualScale(layout);
        var crispPen = MakePen(
            visual.Color,
            System.Math.Max(2.4, 3.5 * visualScale),
            .98,
            "solid");
        var glowPen = MakePen(
            visual.Color,
            System.Math.Max(9, 12 * visualScale),
            .16,
            "solid");

        if (visual.Kind == ConstructionDisplayKind.Circle)
        {
            var radius = visual.Start.DistanceTo(visual.Guide);
            DrawCircleProgress(drawing, layout, visual.Start, radius, visual.Progress, glowPen);
            DrawCircleProgress(drawing, layout, visual.Start, radius, visual.Progress, crispPen);
        }
        else if (visual.Progress > 0)
        {
            var current = new Point2(
                visual.Start.X + (visual.End.X - visual.Start.X) * visual.Progress,
                visual.Start.Y + (visual.End.Y - visual.Start.Y) * visual.Progress);
            var screenStart = Screen(visual.Start, layout);
            var screenEnd = Screen(current, layout);
            drawing.DrawLine(glowPen, screenStart, screenEnd);
            drawing.DrawLine(crispPen, screenStart, screenEnd);
            if (visual.Kind == ConstructionDisplayKind.Ray && visual.Progress > .82)
                DrawArrowHead(drawing, screenStart, screenEnd, crispPen);
        }

        foreach (var point in visual.FocusPoints)
        {
            var center = Screen(point, layout);
            var radius = 7 + 6 * visual.Pulse;
            drawing.DrawEllipse(
                MakeBrush(visual.Color, .18 + .15 * visual.Pulse),
                new Pen(MakeBrush(visual.Color, .8), 1.8),
                center,
                radius,
                radius);
            drawing.DrawEllipse(Brushes.White, null, center, 3.4, 3.4);
            drawing.DrawEllipse(MakeBrush(visual.Color, 1), null, center, 2.2, 2.2);
        }

        foreach (var point in visual.RevealedPoints)
        {
            var center = Screen(point, layout);
            var radius = 7 + 8 * visual.Pulse;
            drawing.DrawEllipse(
                MakeBrush("#F59E0B", .2 + .22 * visual.Pulse),
                new Pen(MakeBrush("#F59E0B", .95), 2),
                center,
                radius,
                radius);
            drawing.DrawEllipse(Brushes.White, null, center, 4.4, 4.4);
            drawing.DrawEllipse(MakeBrush("#DC2626", 1), null, center, 2.8, 2.8);
        }
    }

    private void DrawCircleProgress(
        DrawingContext drawing,
        SceneLayout layout,
        Point2 center,
        double radius,
        double progress,
        Pen pen)
    {
        if (radius <= GeometryAlgorithms.Epsilon || progress <= 0) return;
        var segments = System.Math.Max(2, (int)System.Math.Ceiling(72 * progress));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var start = new Point2(center.X + radius, center.Y);
            context.BeginFigure(Screen(start, layout), false, false);
            for (var index = 1; index <= segments; index++)
            {
                var angle = 2 * System.Math.PI * progress * index / segments;
                var point = new Point2(
                    center.X + radius * System.Math.Cos(angle),
                    center.Y + radius * System.Math.Sin(angle));
                context.LineTo(Screen(point, layout), true, false);
            }
        }
        geometry.Freeze();
        drawing.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArrowHead(
        DrawingContext drawing,
        Point start,
        Point end,
        Pen pen)
    {
        var vector = end - start;
        if (vector.Length < 8) return;
        vector.Normalize();
        var normal = new Vector(-vector.Y, vector.X);
        var length = System.Math.Clamp(10 + pen.Thickness, 11, 17);
        var width = length * .48;
        var left = end - vector * length + normal * width;
        var right = end - vector * length - normal * width;
        drawing.DrawLine(pen, left, end);
        drawing.DrawLine(pen, right, end);
    }

    private static SolidColorBrush MakeBrush(string color, double opacity)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(string color, double width, double opacity, string style)
    {
        var pen = new Pen(MakeBrush(color, opacity), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (style.Equals("dash", StringComparison.OrdinalIgnoreCase)) pen.DashStyle = DashStyles.Dash;
        else if (style.Equals("dot", StringComparison.OrdinalIgnoreCase)) pen.DashStyle = DashStyles.Dot;
        pen.Freeze();
        return pen;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(this);
        var oldZoom = _zoom;
        _zoom = System.Math.Clamp(_zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), .35, 8);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        _pan = new Vector(mouse.X - center.X - (mouse.X - center.X - _pan.X) * _zoom / oldZoom,
            mouse.Y - center.Y - (mouse.Y - center.Y - _pan.Y) * _zoom / oldZoom);
        InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _lastMouse = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Right)
        {
            _dragMode = DragMode.Pan;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            return;
        }
        if (e.ChangedButton != MouseButton.Left || _viewModel?.Evaluation?.Scene is not { } scene) return;
        var layout = new SceneLayout(scene.Canvas, ActualWidth, ActualHeight);
        var hit = HitScene(_lastMouse, scene, layout);
        if (_viewModel.ActiveTool != DrawingTools.Select)
        {
            var world = World(_lastMouse, layout);
            _viewModel.HandleCanvasClick(
                world,
                HitGeometryObjects(_lastMouse, scene, layout),
                HitGridLines(_lastMouse, scene, layout),
                10 / System.Math.Max(1, layout.Scale * _zoom));
            return;
        }
        _viewModel.Select(hit.Id);
        if (hit.Mode is DragMode.Label or DragMode.Point)
        {
            _dragMode = hit.Mode;
            _dragId = hit.Id;
            if (hit.Mode == DragMode.Label && _viewModel.SelectedObject?.Label is { } label)
            {
                _startOffsetX = label.OffsetX;
                _startOffsetY = label.OffsetY;
            }
            _viewModel.BeginEdit();
            CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(this);
        if (_viewModel?.Evaluation?.Scene is not { } scene) return;
        var layout = new SceneLayout(scene.Canvas, ActualWidth, ActualHeight);
        _viewModel.SetMouseCoordinate(World(current, layout));
        if (_dragMode == DragMode.Pan && e.RightButton == MouseButtonState.Pressed)
        {
            _pan += current - _lastMouse; _lastMouse = current; InvalidateVisual();
        }
        else if (_dragMode == DragMode.Label && e.LeftButton == MouseButtonState.Pressed && _dragId is not null)
            _viewModel.MoveLabel(_dragId,
                _startOffsetX + (current.X - _lastMouse.X) / VisualScale(layout),
                _startOffsetY + (current.Y - _lastMouse.Y) / VisualScale(layout));
        else if (_dragMode == DragMode.Point && e.LeftButton == MouseButtonState.Pressed && _dragId is not null)
            _viewModel.MoveDraggablePoint(_dragId, World(current, layout));
        else if (_dragMode == DragMode.None)
        {
            var hover = HitScene(current, scene, layout).Id;
            var gridHover = _viewModel.ActiveTool != DrawingTools.Select
                ? HitGridLines(current, scene, layout)
                : [];
            var objectHits = _viewModel.ActiveTool != DrawingTools.Select
                ? HitGeometryObjects(current, scene, layout)
                : [];
            UpdateCursor(objectHits, gridHover);
            if (_viewModel.ActiveTool != DrawingTools.Select)
                _viewModel.UpdateDrawingPreview(
                    World(current, layout),
                    objectHits,
                    gridHover,
                    10 / System.Math.Max(1, layout.Scale * _zoom));
            if (hover != _hoverId || !gridHover.SequenceEqual(_hoverGridLines)
                || _viewModel.ActiveTool != DrawingTools.Select)
            {
                _hoverId = hover;
                _hoverGridLines = gridHover;
                InvalidateVisual();
            }
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.Point &&
            _dragId is not null &&
            _viewModel?.Evaluation?.Scene is { } scene)
        {
            var layout = new SceneLayout(
                scene.Canvas,
                ActualWidth,
                ActualHeight);
            _viewModel.MoveDraggablePoint(
                _dragId,
                World(e.GetPosition(this), layout),
                completeDrag: true);
        }
        if (_dragMode is DragMode.Label or DragMode.Point) _viewModel?.EndEdit();
        _dragMode = DragMode.None; _dragId = null; ReleaseMouseCapture();
        UpdateCursor();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel?.CancelDrawing();
            e.Handled = true;
            return;
        }
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = e.Key switch
        {
            Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => new Vector()
        };
        if (delta.Length > 0) { _viewModel?.NudgeSelectedLabel(delta.X, delta.Y); e.Handled = true; }
    }

    private (string? Id, DragMode Mode) HitScene(Point mouse, RenderScene scene, SceneLayout layout)
    {
        foreach (var item in scene.Items.AsEnumerable().Reverse())
        {
            if (!item.Style.Visible || item.Label is not { Visible: true } label || GeometryLabelAnchor.Get(item) is not { } anchor) continue;
            var origin = Screen(anchor, layout);
            if (new Rect(origin.X + label.OffsetX * VisualScale(layout) - 4, origin.Y + label.OffsetY * VisualScale(layout) - 4,
                    System.Math.Max(label.FontSize, label.Text.Length * label.FontSize * .7) * VisualScale(layout),
                    label.FontSize * 1.3 * VisualScale(layout)).Contains(mouse))
                return (item.Id, DragMode.Label);
        }
        foreach (var item in scene.Items.AsEnumerable().Reverse())
        {
            if (!item.Style.Visible || item.Geometry.Point is not { } point) continue;
            var center = Screen(point, layout);
            if ((mouse - center).Length <= (item.Style.PointRadius + 8) * VisualScale(layout))
                return (item.Id, _viewModel?.Document?.Objects.First(o => o.Id == item.Id).Draggable == true ? DragMode.Point : DragMode.None);
        }
        foreach (var item in scene.Items.AsEnumerable().Reverse())
        {
            if (!item.Style.Visible || item.Geometry.Line is not { } line || layout.ClipLine(line, scene.Canvas) is not { } clipped) continue;
            if (DistanceToSegment(mouse, Screen(clipped.Start, layout), Screen(clipped.End, layout)) < 7) return (item.Id, DragMode.None);
        }
        var world = World(mouse, layout);
        foreach (var item in scene.Items.AsEnumerable().Reverse())
        {
            if (!item.Style.Visible) continue;
            var center = item.Geometry.Circle?.Center ?? item.Geometry.Arc?.Center;
            var radius = item.Geometry.Circle?.Radius ?? item.Geometry.Arc?.Radius;
            if (center is null || radius is null) continue;
            var radialDistance = System.Math.Abs(center.Value.DistanceTo(world) - radius.Value) * layout.Scale * _zoom;
            if (radialDistance <= 7 * VisualScale(layout) && (item.Geometry.Arc is null || item.Geometry.Arc.Contains(world)))
                return (item.Id, DragMode.None);
        }
        return (null, DragMode.None);
    }

    private IReadOnlyList<string> HitGeometryObjects(Point mouse, RenderScene scene, SceneLayout layout)
    {
        var hits = new List<(string Id, double Distance, int Priority)>();
        var world = World(mouse, layout);
        foreach (var item in scene.Items.AsEnumerable().Reverse())
        {
            if (!item.Style.Visible) continue;
            if (item.Geometry.Point is { } point)
            {
                var distance = (mouse - Screen(point, layout)).Length;
                var hitRadius = System.Math.Clamp(
                    (item.Style.PointRadius + 3) * VisualScale(layout), 6, 11);
                if (distance <= hitRadius)
                    hits.Add((item.Id, distance, 0));
            }
            else if (item.Geometry.Line is { } line && layout.ClipLine(line, scene.Canvas) is { } clipped)
            {
                var distance = DistanceToSegment(mouse, Screen(clipped.Start, layout), Screen(clipped.End, layout));
                if (distance <= 9) hits.Add((item.Id, distance, 1));
            }
            else
            {
                var center = item.Geometry.Circle?.Center ?? item.Geometry.Arc?.Center;
                var radius = item.Geometry.Circle?.Radius ?? item.Geometry.Arc?.Radius;
                if (center is null || radius is null) continue;
                var distance = System.Math.Abs(center.Value.DistanceTo(world) - radius.Value) * layout.Scale * _zoom;
                if (distance <= 9 && (item.Geometry.Arc is null || item.Geometry.Arc.Contains(world)))
                    hits.Add((item.Id, distance, 1));
            }
        }
        return hits.OrderBy(hit => hit.Priority).ThenBy(hit => hit.Distance).Select(hit => hit.Id).Distinct().ToList();
    }

    private IReadOnlyList<GridLineReference> HitGridLines(Point mouse, RenderScene scene, SceneLayout layout)
    {
        if (!scene.Grid.Visible || scene.Grid.Step <= 0) return [];
        var world = World(mouse, layout);
        var bounds = scene.Canvas;
        var x = bounds.MinX + System.Math.Round((world.X - bounds.MinX) / scene.Grid.Step) * scene.Grid.Step;
        var y = bounds.MinY + System.Math.Round((world.Y - bounds.MinY) / scene.Grid.Step) * scene.Grid.Step;
        var verticalDistance = x >= bounds.MinX - 1e-9 && x <= bounds.MaxX + 1e-9
            ? System.Math.Abs(Screen(new Point2(x, world.Y), layout).X - mouse.X)
            : double.PositiveInfinity;
        var horizontalDistance = y >= bounds.MinY - 1e-9 && y <= bounds.MaxY + 1e-9
            ? System.Math.Abs(Screen(new Point2(world.X, y), layout).Y - mouse.Y)
            : double.PositiveInfinity;
        const double threshold = 8;
        var hits = new List<(GridLineReference Line, double Distance)>();
        if (verticalDistance <= threshold) hits.Add((new GridLineReference("x", x), verticalDistance));
        if (horizontalDistance <= threshold) hits.Add((new GridLineReference("y", y), horizontalDistance));
        return hits.OrderBy(hit => hit.Distance).Select(hit => hit.Line).ToList();
    }

    private void UpdateCursor(
        IReadOnlyList<string>? objectHits = null,
        IReadOnlyList<GridLineReference>? gridHits = null)
    {
        if (_dragMode == DragMode.Pan)
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        if (_viewModel is null || _viewModel.ActiveTool == DrawingTools.Select)
        {
            Cursor = Cursors.Arrow;
            return;
        }
        if (!_viewModel.IsLineRelationTool)
        {
            Cursor = Cursors.Cross;
            return;
        }

        if (_viewModel.RelationNeedsLine)
        {
            Cursor = Cursors.Arrow;
            return;
        }
        if (_viewModel.RelationNeedsPoint)
        {
            var overPoint = objectHits?.Any(id => _viewModel.Evaluation?.Results.GetValueOrDefault(id)?.Point is not null) == true;
            Cursor = overPoint ? Cursors.Arrow : Cursors.Cross;
            return;
        }
        var overSelectableObject = objectHits?.Any(id =>
            _viewModel.Evaluation?.Results.GetValueOrDefault(id) is { } result &&
            (result.Point is not null || result.Line is not null)) == true;
        Cursor = overSelectableObject || gridHits?.Count > 0 ? Cursors.Arrow : Cursors.Cross;
    }

    private static double DistanceToSegment(Point point, Point a, Point b)
    {
        var ab = b - a;
        var squared = ab.X * ab.X + ab.Y * ab.Y;
        if (squared == 0) return (point - a).Length;
        var ap = point - a;
        var t = System.Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / squared, 0, 1);
        return (point - (a + ab * t)).Length;
    }

    private enum DragMode { None, Pan, Label, Point }
}
