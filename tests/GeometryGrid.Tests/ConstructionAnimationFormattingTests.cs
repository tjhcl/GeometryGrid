using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Rendering;

namespace GeometryGrid.Tests;

public sealed class ConstructionAnimationFormattingTests
{
    [Fact]
    public void AnimationCircle_TracesCircumferenceInsteadOfGrowingRadius()
    {
        var a = new ConstructionSearchPoint("A", new Point2(2, 2));
        var b = new ConstructionSearchPoint("B", new Point2(4, 2));
        var step = new ConstructionStep("circle:A:B", ConstructionToolKind.Circle, a, b, "作圆");
        var plan = new ConstructionPlaybackStepPlan(step, ConstructionDisplayKind.Circle,
            a.Position, b.Position, b.Position, 0, 1, []);
        var scene = new RenderScene { Canvas = new CanvasSettings(), Grid = new GridSettings() };
        var animated = MainViewModel.CreateAnimationScene(scene, plan, "#000000", null, .5, 0, false);
        var arc = Assert.Single(animated.Items).Geometry.Arc!;
        Assert.Equal(2, arc.Radius);
        Assert.Equal(Math.PI, arc.SweepRadians, 10);
    }

    [Fact]
    public void AnimationFocus_SelectsOneEndpointAndShowsMovingNib()
    {
        var a = new ConstructionSearchPoint("A", new Point2(0, 0));
        var b = new ConstructionSearchPoint("B", new Point2(4, 2));
        var step = new ConstructionStep("line:A:B", ConstructionToolKind.StraightLine, a, b, "连线");
        var plan = new ConstructionPlaybackStepPlan(step, ConstructionDisplayKind.Segment,
            a.Position, b.Position, b.Position, 0, 1, []);
        var scene = new RenderScene { Canvas = new CanvasSettings(), Grid = new GridSettings() };
        var focus = MainViewModel.CreateAnimationScene(scene, plan, "#2563EB", null, 0, .5, false, 1);
        Assert.All(focus.Items, item => Assert.Equal(b.Position, item.Geometry.Point));
        var drawing = MainViewModel.CreateAnimationScene(scene, plan, "#2563EB", null, .5, .2, false);
        Assert.Contains(drawing.Items, item => item.Id.StartsWith("animation-nib") &&
            item.Geometry.Point == new Point2(2, 1));
    }
    [Fact]
    public void Apply_PreservesCurrentStylesLabelsAndDraggablePointPosition()
    {
        var document = new ProjectDocument
        {
            Objects =
            [
                new GeometryObjectDefinition
                {
                    Id = "A",
                    Type = "fixedPoint",
                    X = "1",
                    Y = "2",
                    Draggable = true
                },
                new GeometryObjectDefinition
                {
                    Id = "solverSegment1",
                    Type = "segment",
                    PointA = "A",
                    PointB = "B"
                }
            ]
        };
        var artwork = new ProjectDocument
        {
            Canvas = new CanvasSettings { Background = "#F8FAFC" },
            Grid = new GridSettings { Color = "#64748B", StrokeWidth = 1.7 },
            Objects =
            [
                new GeometryObjectDefinition
                {
                    Id = "A",
                    Name = "所求点",
                    Type = "fixedPoint",
                    X = "3.25",
                    Y = "4.5",
                    Draggable = true,
                    Style = new GeometryStyle { Color = "#DC2626", PointRadius = 5 },
                    Label = new LabelDefinition
                    {
                        Text = "P",
                        Visible = true,
                        OffsetX = 18,
                        OffsetY = -42
                    }
                },
                new GeometryObjectDefinition
                {
                    Id = "solverSegment1",
                    Type = "segment",
                    PointA = "A",
                    PointB = "B",
                    Style = new GeometryStyle
                    {
                        Color = "#16A34A",
                        StrokeStyle = "dash",
                        StrokeWidth = 6
                    }
                },
                new GeometryObjectDefinition
                {
                    Id = "midpointNote",
                    Type = "fixedPoint",
                    X = "4",
                    Y = "4",
                    Label = new LabelDefinition { Text = "中点", Visible = true }
                }
            ]
        };

        ConstructionAnimationFormatting.Apply(
            document,
            artwork,
            includeMissingObjects: false);

        var point = Assert.Single(document.Objects, item => item.Id == "A");
        Assert.Equal("3.25", point.X);
        Assert.Equal("4.5", point.Y);
        Assert.Equal("P", point.Label?.Text);
        Assert.Equal(18, point.Label?.OffsetX);
        Assert.Equal("#DC2626", point.Style?.Color);

        var segment = Assert.Single(
            document.Objects,
            item => item.Id == "solverSegment1");
        Assert.Equal("dash", segment.Style?.StrokeStyle);
        Assert.Equal("#16A34A", segment.Style?.Color);
        Assert.DoesNotContain(document.Objects, item => item.Id == "midpointNote");

        ConstructionAnimationFormatting.Apply(
            document,
            artwork,
            includeMissingObjects: true);
        Assert.Contains(document.Objects, item =>
            item.Id == "midpointNote" &&
            item.Label?.Text == "中点");
        Assert.Equal("#F8FAFC", document.Canvas.Background);
        Assert.Equal("#64748B", document.Grid.Color);
    }

    [Fact]
    public void AnimationOverlay_UsesCurrentDashedLineStyle()
    {
        var pointA = new ConstructionSearchPoint("A", new Point2(0, 0));
        var pointB = new ConstructionSearchPoint("B", new Point2(4, 2));
        var step = new ConstructionStep(
            "line:A:B",
            ConstructionToolKind.StraightLine,
            pointA,
            pointB,
            "作线段");
        var plan = new ConstructionPlaybackStepPlan(
            step,
            ConstructionDisplayKind.Segment,
            pointA.Position,
            pointB.Position,
            pointB.Position,
            0,
            1,
            []);
        var style = new ResolvedStyle(
            true,
            6.5,
            "dash",
            "#16A34A",
            .7,
            0,
            52);
        var scene = new RenderScene
        {
            Canvas = new CanvasSettings(),
            Grid = new GridSettings(),
            Items = []
        };

        var animated = MainViewModel.CreateAnimationScene(
            scene,
            plan,
            style.Color,
            style,
            .5,
            0,
            false);

        var overlay = Assert.Single(animated.Items);
        Assert.Equal("dash", overlay.Style.StrokeStyle);
        Assert.Equal("#16A34A", overlay.Style.Color);
        Assert.Equal(6.5, overlay.Style.StrokeWidth);
        Assert.Equal(.7, overlay.Style.Opacity);
    }
}
