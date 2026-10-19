using System.IO;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Math;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Tests;

public sealed class SolverJsonTests
{
    [Fact]
    public void BuiltInExample_ContainsReadyToRunTargetsAndDefaultTools()
    {
        var solver = TestProject.Load().Solver;

        Assert.Equal(["P", "M", "N"], solver.Targets.Select(target => target.Name));
        Assert.True(solver.UseStraightedge);
        Assert.False(solver.UseCompass);
        Assert.True(solver.UseGrid);
    }

    [Fact]
    public void LegacyJsonWithoutSolver_UsesStraightedgeAndGridDefaults()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "canvas": { "minX": 0, "maxX": 8, "minY": 0, "maxY": 8 },
          "grid": { "visible": true, "step": 1 },
          "styles": {},
          "parameters": [],
          "objects": [],
          "exports": {}
        }
        """;

        var document = ProjectSerializer.LoadJson(json, "legacy.json");

        Assert.True(document.Solver.UseStraightedge);
        Assert.False(document.Solver.UseCompass);
        Assert.True(document.Solver.UseGrid);
        Assert.Empty(document.Solver.Targets);
        Assert.Equal(1, document.Solver.MinSteps);
        Assert.Equal(5, document.Solver.MaxSteps);
    }

    [Fact]
    public void SolverSettings_RoundTripKeepsExpressionsAndOptions()
    {
        var document = new ProjectDocument
        {
            Solver = new SolverSettings
            {
                Targets =
                [
                    new SolverTargetDefinition { Name = "P", X = "7/8", Y = "5/2" },
                    new SolverTargetDefinition { Name = "Q", X = "3+t/2", Y = "sqrt(5)" }
                ],
                MinSteps = 3,
                MaxSteps = 6,
                MaxSolutionsPerTarget = 4,
                MaxStates = 345678,
                UseStraightedge = true,
                UseCompass = true,
                UseGrid = false
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(document, ProjectSerializer.Options);
        var loaded = ProjectSerializer.LoadJson(json, "roundtrip.json");

        Assert.Equal("7/8", loaded.Solver.Targets[0].X);
        Assert.Equal("sqrt(5)", loaded.Solver.Targets[1].Y);
        Assert.Equal(3, loaded.Solver.MinSteps);
        Assert.Equal(6, loaded.Solver.MaxSteps);
        Assert.Equal(4, loaded.Solver.MaxSolutionsPerTarget);
        Assert.Equal(345678, loaded.Solver.MaxStates);
        Assert.True(loaded.Solver.UseCompass);
        Assert.False(loaded.Solver.UseGrid);
    }

    [Fact]
    public void ViewModel_OpenAndSave_SynchronizesSolverPanelWithJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"geometrygrid-solver-{Guid.NewGuid():N}.json");
        try
        {
            ProjectSerializer.Save(new ProjectDocument
            {
                Solver = new SolverSettings
                {
                    Targets = [new SolverTargetDefinition { Name = "T", X = "3/2", Y = "9/4" }],
                    MinSteps = 2,
                    MaxSteps = 4,
                    MaxSolutionsPerTarget = 2,
                    MaxStates = 120000,
                    UseStraightedge = true,
                    UseCompass = false,
                    UseGrid = true
                }
            }, path);
            var viewModel = new MainViewModel(new FakeDialogs(path));

            viewModel.OpenCommand.Execute(null);

            Assert.Equal("T = 3/2, 9/4", viewModel.ConstructionTargetsInput);
            Assert.Equal("2", viewModel.ConstructionMinStepsInput);
            Assert.Equal("4", viewModel.ConstructionMaxStepsInput);
            Assert.True(viewModel.SolverUseStraightedge);
            Assert.False(viewModel.SolverUseCompass);
            Assert.True(viewModel.SolverUseGrid);

            viewModel.ConstructionTargetsInput = "U = 11/6, 13/7";
            viewModel.ConstructionMinStepsInput = "3";
            viewModel.ConstructionMaxStepsInput = "6";
            viewModel.SolverUseCompass = true;
            viewModel.SolverUseGrid = false;
            viewModel.SaveCommand.Execute(null);

            var saved = ProjectSerializer.Load(path);
            var target = Assert.Single(saved.Solver.Targets);
            Assert.Equal("U", target.Name);
            Assert.Equal("11/6", target.X);
            Assert.Equal("13/7", target.Y);
            Assert.Equal(3, saved.Solver.MinSteps);
            Assert.Equal(6, saved.Solver.MaxSteps);
            Assert.True(saved.Solver.UseCompass);
            Assert.False(saved.Solver.UseGrid);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ViewModel_OpenAndSave_PreservesNestedTargetExpressions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"geometrygrid-nested-target-{Guid.NewGuid():N}.json");
        const string mX = "(21+35*sqrt(17))/26";
        const string nX = "(367+35*sqrt(17))/86";
        const string pX = "(6880+2847*sqrt(17))/3617";
        const string pY = "(15289+1091*sqrt(17))/3617";
        try
        {
            ProjectSerializer.Save(new ProjectDocument
            {
                Solver = new SolverSettings
                {
                    Targets =
                    [
                        new SolverTargetDefinition { Name = "M", X = mX, Y = "6" },
                        new SolverTargetDefinition { Name = "N", X = nX, Y = "6" },
                        new SolverTargetDefinition { Name = "P", X = pX, Y = pY }
                    ]
                }
            }, path);
            var viewModel = new MainViewModel(new FakeDialogs(path));

            viewModel.OpenCommand.Execute(null);
            viewModel.SaveCommand.Execute(null);

            var targets = ProjectSerializer.Load(path).Solver.Targets;
            Assert.Equal(mX, targets[0].X);
            Assert.Equal(nX, targets[1].X);
            Assert.Equal(pX, targets[2].X);
            Assert.Equal(pY, targets[2].Y);

            var parameters = new Dictionary<string, Rational>();
            Assert.Equal(6.358026803716082, ExpressionParser.Parse(targets[0].X).Evaluate(parameters).Approximate, 12);
            Assert.Equal(5.945449963914165, ExpressionParser.Parse(targets[1].X).Evaluate(parameters).Approximate, 12);
            Assert.Equal(5.147492871477324, ExpressionParser.Parse(targets[2].X).Evaluate(parameters).Approximate, 12);
            Assert.Equal(5.470640928268971, ExpressionParser.Parse(targets[2].Y).Evaluate(parameters).Approximate, 12);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ViewModel_Save_AcceptsAParenthesizedCoordinatePair()
    {
        var path = Path.Combine(Path.GetTempPath(), $"geometrygrid-wrapped-target-{Guid.NewGuid():N}.json");
        try
        {
            ProjectSerializer.Save(new ProjectDocument(), path);
            var viewModel = new MainViewModel(new FakeDialogs(path));
            viewModel.OpenCommand.Execute(null);
            viewModel.ConstructionTargetsInput =
                "P = ((6880+2847*sqrt(17))/3617, (15289+1091*sqrt(17))/3617)";

            viewModel.SaveCommand.Execute(null);

            var target = Assert.Single(ProjectSerializer.Load(path).Solver.Targets);
            Assert.Equal("(6880+2847*sqrt(17))/3617", target.X);
            Assert.Equal("(15289+1091*sqrt(17))/3617", target.Y);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FakeDialogs(string path) : IFileDialogService
    {
        public string? OpenJson() => path;
        public string? SaveJson(string? currentPath) => path;
        public string? SaveExport(string extension, string filter) => null;
        public string? PickColor(string current) => null;
        public string? PromptText(string title, string prompt, string initialValue) => initialValue;
        public void ShowError(string title, string message) => throw new InvalidOperationException($"{title}: {message}");
    }
}
