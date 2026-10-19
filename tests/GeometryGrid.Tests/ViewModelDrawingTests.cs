using GeometryGrid.App;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Tests;

public sealed class ViewModelDrawingTests
{
    [Fact]
    public void PlaybackSegment_UsesInfiniteCarrierButKeepsSegmentDisplayRange()
    {
        var a = new ConstructionSearchPoint("A", new Point2(0, 0), "A");
        var b = new ConstructionSearchPoint("B", new Point2(1, 0), "B");
        var step = new ConstructionStep(
            "step:0",
            ConstructionToolKind.StraightLine,
            a,
            b,
            "AB");
        var plan = new ConstructionPlaybackStepPlan(
            step,
            ConstructionDisplayKind.Segment,
            a.Position,
            b.Position,
            b.Position,
            0,
            1,
            []);
        var playback = new GeometryObjectDefinition
        {
            Id = "solverSegment1",
            Type = "segment"
        };

        MainViewModel.ConfigurePlaybackStraightedgeDefinition(
            playback,
            plan,
            "A",
            "B");

        Assert.Equal("lineThroughPoints", playback.Type);
        Assert.Equal("A", playback.PointA);
        Assert.Equal("B", playback.PointB);
        Assert.Equal(0, playback.DisplayMinParameter);
        Assert.Equal(1, playback.DisplayMaxParameter);

        var document = new ProjectDocument
        {
            Objects =
            [
                new GeometryObjectDefinition
                    { Id = "A", Type = "fixedPoint", X = "0", Y = "0" },
                new GeometryObjectDefinition
                    { Id = "B", Type = "fixedPoint", X = "1", Y = "0" },
                playback,
                new GeometryObjectDefinition
                {
                    Id = "V0",
                    Type = "fixedPoint",
                    X = "2",
                    Y = "-1"
                },
                new GeometryObjectDefinition
                {
                    Id = "V1",
                    Type = "fixedPoint",
                    X = "2",
                    Y = "1"
                },
                new GeometryObjectDefinition
                {
                    Id = "verticalCarrier",
                    Type = "lineThroughPoints",
                    PointA = "V0",
                    PointB = "V1"
                },
                new GeometryObjectDefinition
                {
                    Id = "N",
                    Type = "intersectionPoint",
                    ObjectA = playback.Id,
                    ObjectB = "verticalCarrier",
                    Selector = "first"
                }
            ]
        };

        var evaluation = new GeometryEngine().Evaluate(document);
        Assert.Empty(evaluation.Errors);
        MathAndDependencyTests.AssertClose(
            new Point2(2, 0),
            evaluation.Results["N"].Point!.Value);
        var rendered = evaluation.Scene.Items.Single(item => item.Id == playback.Id);
        Assert.Equal(0, rendered.Geometry.Line!.MinParameter);
        Assert.Equal(1, rendered.Geometry.Line.MaxParameter);
    }

    [Fact]
    public void SegmentTool_CreatesPointsAndSegmentInOneDrawingFlow()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Segment);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 4));

        Assert.Equal(3, viewModel.Document!.Objects.Count);
        Assert.Equal(2, viewModel.Document.Objects.Count(item => item.Type == "fixedPoint"));
        var segment = Assert.Single(viewModel.Document.Objects, item => item.Type == "segment");
        Assert.NotNull(segment.PointA);
        Assert.NotNull(segment.PointB);
        Assert.Null(segment.Name);
        Assert.Null(segment.Label);
        Assert.True(viewModel.Evaluation!.Results[segment.Id].IsValid);
    }

    [Fact]
    public void IntersectionTool_UsesBackgroundGridWithoutAuxiliaryLine()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Line);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 4));
        var line = Assert.Single(viewModel.Document!.Objects, item => item.Type == "lineThroughPoints");

        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(2, 2), [line.Id], [new GridLineReference("x", 2)]);

        var intersection = Assert.Single(viewModel.Document.Objects, item => item.Type == "gridIntersectionPoint");
        Assert.Equal("x", intersection.GridAxis);
        Assert.Equal("2", intersection.GridValue);
        Assert.DoesNotContain(viewModel.Document.Objects, item => item.Type is "verticalLine" or "horizontalLine");
        MathAndDependencyTests.AssertClose(new Point2(2, 2), viewModel.Evaluation!.Results[intersection.Id].Point!.Value);
    }

    [Fact]
    public void DisplayNameEditor_UpdatesObjectNameAndCanvasLabel()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Segment);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(3, 0));
        var segment = Assert.Single(viewModel.Document!.Objects, item => item.Type == "segment");
        viewModel.Select(segment.Id);

        viewModel.SelectedDisplayName = "底边";

        Assert.Equal("底边", segment.Name);
        Assert.Equal("底边", segment.Label!.Text);
        Assert.True(segment.Label.Visible);
    }

    [Fact]
    public void PointTool_DoesNotCreateSamePointTwice()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Point);

        Click(viewModel, new Point2(1.25, 2.5));
        Click(viewModel, new Point2(1.25, 2.5));

        Assert.Single(viewModel.Document!.Objects, item => item.Type == "fixedPoint");
        Assert.Contains("不会重复创建", viewModel.ToolInstruction);
    }

    [Fact]
    public void PointTool_CreatesObjectIntersectionAndReusesIt()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Line);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 4));
        Click(viewModel, new Point2(0, 4));
        Click(viewModel, new Point2(4, 0));
        var lines = viewModel.Document!.Objects.Where(item => item.Type == "lineThroughPoints").ToList();

        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(2, 2), lines.Select(line => line.Id).ToArray());
        var intersection = Assert.Single(viewModel.Document.Objects, item => item.Type == "intersectionPoint");
        Click(viewModel, new Point2(2, 2), [intersection.Id, .. lines.Select(line => line.Id)]);

        Assert.Single(viewModel.Document.Objects, item => item.Type == "intersectionPoint");
        Assert.Contains("不会重复创建", viewModel.ToolInstruction);
    }

    [Fact]
    public void PointTool_ClickingSingleLineCreatesUnnamedConstrainedPoint()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Line);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 0));
        var line = Assert.Single(viewModel.Document!.Objects, item => item.Type == "lineThroughPoints");

        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(2, 0), [line.Id]);

        var point = Assert.Single(viewModel.Document.Objects, item => item.Type == "pointOnLine");
        Assert.True(point.Draggable);
        Assert.Null(point.Name);
        Assert.Null(point.Label);
        Assert.Single(viewModel.Document.Parameters);
        MathAndDependencyTests.AssertClose(new Point2(2, 0), viewModel.Evaluation!.Results[point.Id].Point!.Value);
    }

    [Fact]
    public void SegmentTool_CanCreateAndUsePointOnExistingLineWithoutPointTool()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Line);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 0));
        var line = Assert.Single(viewModel.Document!.Objects, item => item.Type == "lineThroughPoints");

        viewModel.SetToolCommand.Execute(DrawingTools.Segment);
        Click(viewModel, new Point2(2, 0), [line.Id]);
        Assert.Single(viewModel.Document.Objects, item => item.Type == "pointOnLine");

        Click(viewModel, new Point2(2, 3));
        var segment = Assert.Single(viewModel.Document.Objects, item => item.Type == "segment");
        Assert.True(viewModel.Evaluation!.Results[segment.Id].IsValid);
    }

    [Fact]
    public void Undo_RemovesPointCreatedByConstructionToolAndClearsTemporaryPick()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Segment);
        Click(viewModel, new Point2(1, 1));
        Assert.Single(viewModel.Document!.Objects);

        viewModel.UndoCommand.Execute(null);

        Assert.Empty(viewModel.Document!.Objects);
        Assert.Null(viewModel.Preview);
        Assert.Contains("依次点击两个点", viewModel.ToolInstruction);

        Click(viewModel, new Point2(2, 2));
        Click(viewModel, new Point2(5, 2));
        var segment = Assert.Single(viewModel.Document.Objects, item => item.Type == "segment");
        Assert.True(viewModel.Evaluation!.Results[segment.Id].IsValid);
    }

    [Theory]
    [InlineData(DrawingTools.Perpendicular, "perpendicularLine")]
    [InlineData(DrawingTools.Parallel, "parallelLine")]
    public void PointLineRelation_AcceptsEitherSelectionOrderAndLocksRequiredKind(string tool, string resultType)
    {
        var pointFirst = NewViewModel();
        pointFirst.SetToolCommand.Execute(DrawingTools.Line);
        Click(pointFirst, new Point2(0, 0));
        Click(pointFirst, new Point2(4, 0));
        var sourceA = Assert.Single(pointFirst.Document!.Objects, item => item.Type == "lineThroughPoints");
        pointFirst.SetToolCommand.Execute(tool);
        Click(pointFirst, new Point2(1, 2));
        Assert.Contains("现在请选择", pointFirst.ToolInstruction);
        Click(pointFirst, new Point2(3, 3));
        Assert.Contains("现在请选择", pointFirst.ToolInstruction);
        Assert.Empty(pointFirst.Document.Objects.Where(item => item.Type == resultType));
        Click(pointFirst, new Point2(2, 0), [sourceA.Id]);
        Assert.Single(pointFirst.Document.Objects, item => item.Type == resultType);

        var lineFirst = NewViewModel();
        lineFirst.SetToolCommand.Execute(DrawingTools.Line);
        Click(lineFirst, new Point2(0, 0));
        Click(lineFirst, new Point2(4, 0));
        var sourceB = Assert.Single(lineFirst.Document!.Objects, item => item.Type == "lineThroughPoints");
        lineFirst.SetToolCommand.Execute(tool);
        Click(lineFirst, new Point2(2, 0), [sourceB.Id]);
        Assert.Contains("现在请选择或创建一个点", lineFirst.ToolInstruction);
        Click(lineFirst, new Point2(2, 3));
        Assert.Single(lineFirst.Document.Objects, item => item.Type == resultType);
    }

    [Theory]
    [InlineData(DrawingTools.Perpendicular, "perpendicularLine")]
    [InlineData(DrawingTools.Parallel, "parallelLine")]
    public void PointLineRelation_AcceptsGridLineAndProvidesLivePreview(string tool, string resultType)
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(tool);
        Click(viewModel, new Point2(2, 3));
        Assert.True(viewModel.RelationNeedsLine);

        viewModel.UpdateDrawingPreview(
            new Point2(2, 0),
            [],
            [new GridLineReference("y", 0)],
            .1);

        Assert.NotNull(viewModel.Preview?.Line);
        MathAndDependencyTests.AssertClose(new Point2(2, 3), viewModel.Preview!.Line!.Origin);

        Click(viewModel, new Point2(2, 0), [], [new GridLineReference("y", 0)]);
        var relation = Assert.Single(viewModel.Document!.Objects, item => item.Type == resultType);
        Assert.Null(relation.ObjectA);
        Assert.Equal("y", relation.GridAxis);
        Assert.Equal("0", relation.GridValue);
        Assert.True(viewModel.Evaluation!.Results[relation.Id].IsValid);
    }

    [Theory]
    [InlineData(DrawingTools.Perpendicular)]
    [InlineData(DrawingTools.Parallel)]
    public void PointLineRelation_PreviewsAfterSelectingLineFirst(string tool)
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Line);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 0));
        var source = Assert.Single(viewModel.Document!.Objects, item => item.Type == "lineThroughPoints");

        viewModel.SetToolCommand.Execute(tool);
        Click(viewModel, new Point2(2, 0), [source.Id]);
        Assert.True(viewModel.RelationNeedsPoint);
        viewModel.UpdateDrawingPreview(new Point2(2, 3), [], [], .1);

        Assert.NotNull(viewModel.Preview?.Line);
        MathAndDependencyTests.AssertClose(new Point2(2, 3), viewModel.Preview!.Line!.Origin);
    }

    [Fact]
    public void RadiusSegmentCircle_PreviewsAfterCenterAndCreatesOnSecondClick()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.CircleRadiusSegment);
        Click(viewModel, new Point2(1, 1));

        viewModel.UpdateDrawingPreview(new Point2(4, 5), [], [], .1);

        Assert.NotNull(viewModel.Preview?.Circle);
        MathAndDependencyTests.AssertClose(new Point2(1, 1), viewModel.Preview!.Circle!.Center);
        Assert.Equal(5, viewModel.Preview.Circle.Radius, 10);

        Click(viewModel, new Point2(4, 5));
        var circle = Assert.Single(viewModel.Document!.Objects, item => item.Type == "circleByCenterPoint");
        Assert.True(viewModel.Evaluation!.Results[circle.Id].IsValid);
    }

    [Fact]
    public void StyleZoom_IsBoundedWhileStillRespondingToZoom()
    {
        Assert.InRange(GeometryCanvas.StyleZoomFactor(8), 2, 2.1);
        Assert.InRange(GeometryCanvas.StyleZoomFactor(.35), .68, .75);
        Assert.Equal(1, GeometryCanvas.StyleZoomFactor(1), 10);
    }

    [Fact]
    public void PointTool_GridLineClickDoesNotReuseNearbyButUnhitPoint()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(0, .15));
        var nearbyPoint = Assert.Single(viewModel.Document!.Objects, item => item.Type == "fixedPoint");

        Click(viewModel, new Point2(0, 0), [], [new GridLineReference("y", 0)]);

        var gridPoint = Assert.Single(viewModel.Document.Objects, item => item.Type == "pointOnGridLine");
        Assert.NotEqual(nearbyPoint.Id, gridPoint.Id);
        Assert.Equal("y", gridPoint.GridAxis);
        MathAndDependencyTests.AssertClose(new Point2(0, 0), viewModel.Evaluation!.Results[gridPoint.Id].Point!.Value);
    }

    private static MainViewModel NewViewModel()
    {
        var viewModel = new MainViewModel(new FakeDialogs());
        viewModel.NewCommand.Execute(null);
        return viewModel;
    }

    private static void Click(
        MainViewModel viewModel,
        Point2 world,
        IReadOnlyList<string>? objectIds = null,
        IReadOnlyList<GridLineReference>? gridLines = null) =>
        viewModel.HandleCanvasClick(world, objectIds ?? [], gridLines ?? [], .1);

    private sealed class FakeDialogs : IFileDialogService
    {
        public string? OpenJson() => null;
        public string? SaveJson(string? currentPath) => null;
        public string? SaveExport(string extension, string filter) => null;
        public string? PickColor(string current) => null;
        public string? PromptText(string title, string prompt, string initialValue) => initialValue;
        public void ShowError(string title, string message) => throw new InvalidOperationException($"{title}: {message}");
    }
}
