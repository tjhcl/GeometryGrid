using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Tests;

public sealed class V13Tests
{
    [Fact]
    public void DeleteSelected_RemovesDependentsAndUndoRestoresWholeStep()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Segment);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(4, 0));
        var point = viewModel.Document!.Objects.First(item => item.Type == "fixedPoint");
        viewModel.Select(point.Id);

        viewModel.DeleteSelectedCommand.Execute(null);

        Assert.Single(viewModel.Document.Objects);
        Assert.Single(viewModel.Document.Objects, item => item.Type == "fixedPoint");
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(3, viewModel.Document.Objects.Count);
        Assert.Single(viewModel.Document.Objects, item => item.Type == "segment");
    }

    [Fact]
    public void PointOnGridLine_IsDraggableAndRemainsOnGridLine()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(2.01, 1.4), gridLines: [new GridLineReference("x", 2)]);
        var point = Assert.Single(viewModel.Document!.Objects, item => item.Type == "pointOnGridLine");

        viewModel.BeginEdit();
        viewModel.MoveDraggablePoint(point.Id, new Point2(7, 6));
        viewModel.EndEdit();

        MathAndDependencyTests.AssertClose(
            new Point2(2, 6),
            viewModel.Evaluation!.Results[point.Id].Point!.Value);
    }

    [Fact]
    public void PointTool_TwoGridLinesCreateFixedIntersection()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Point);

        Click(viewModel, new Point2(2.03, 3.02), gridLines:
            [new GridLineReference("x", 2), new GridLineReference("y", 3)]);

        var point = Assert.Single(viewModel.Document!.Objects, item => item.Type == "fixedPoint");
        Assert.False(point.Draggable);
        MathAndDependencyTests.AssertClose(
            new Point2(2, 3),
            viewModel.Evaluation!.Results[point.Id].Point!.Value);
        Assert.Contains("固定交点", viewModel.ToolInstruction);
    }

    [Fact]
    public void PointTool_BlankPointIsFreeAndDraggable()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.Point);
        Click(viewModel, new Point2(1.25, 2.5));
        var point = Assert.Single(viewModel.Document!.Objects, item => item.Type == "fixedPoint");
        Assert.True(point.Draggable);

        viewModel.BeginEdit();
        viewModel.MoveDraggablePoint(point.Id, new Point2(6.5, 7.25));
        viewModel.EndEdit();

        MathAndDependencyTests.AssertClose(
            new Point2(6.5, 7.25),
            viewModel.Evaluation!.Results[point.Id].Point!.Value);
        viewModel.UndoCommand.Execute(null);
        MathAndDependencyTests.AssertClose(
            new Point2(1.25, 2.5),
            viewModel.Evaluation.Results[point.Id].Point!.Value);
    }

    [Fact]
    public void CircleCenterDistance_UsesDistanceBetweenSelectedGridPoints()
    {
        var viewModel = NewViewModel();
        viewModel.SetToolCommand.Execute(DrawingTools.CircleCenterDistance);
        Click(viewModel, new Point2(0, 0));
        Click(viewModel, new Point2(1.03, 1.04),
            gridLines: [new GridLineReference("x", 1), new GridLineReference("y", 1)]);
        Click(viewModel, new Point2(4.02, 5.01),
            gridLines: [new GridLineReference("x", 4), new GridLineReference("y", 5)]);

        var circle = Assert.Single(viewModel.Document!.Objects, item => item.Type == "circleByCenterDistance");
        var geometry = viewModel.Evaluation!.Results[circle.Id].Circle!;
        MathAndDependencyTests.AssertClose(new Point2(0, 0), geometry.Center);
        Assert.Equal(5, geometry.Radius, 10);
    }

    [Fact]
    public void CanvasSize_KeepsLowerLeftAndExtendsRightAndUp()
    {
        var viewModel = NewViewModel();
        viewModel.CanvasWidthInput = "12";
        viewModel.CanvasHeightInput = "10";

        viewModel.ApplyCanvasSizeCommand.Execute(null);

        Assert.Equal(0, viewModel.Document!.Canvas.MinX);
        Assert.Equal(0, viewModel.Document.Canvas.MinY);
        Assert.Equal(12, viewModel.Document.Canvas.MaxX);
        Assert.Equal(10, viewModel.Document.Canvas.MaxY);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(8, viewModel.Document.Canvas.MaxX);
        Assert.Equal(8, viewModel.Document.Canvas.MaxY);
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
