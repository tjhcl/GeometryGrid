using System.IO;
using System.Reflection;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.Tests;

public sealed class SuperIndeterminateDiagramTests
{
    [Fact]
    public void GeneratedDesktopSample_ResolvesAcrossFourParameterPositions()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),
            "json合集（持续更新中）",
            "超级不定图_双自由点_角平分_MN.json");
        // This is an optional user sample rather than a repository fixture.
        // Moving or archiving the Desktop collection must not break the suite.
        if (!File.Exists(path))
            return;
        var document = ProjectSerializer.Load(path);
        var positions = new[]
        {
            (0.3, 1.3, 3.2, 0.85),
            (0.18, 1.15, 3.08, 1.0),
            (0.61, 1.49, 3.36, 0.7),
            (0.22, 1.46, 3.31, 0.94),
            (0.57, 1.2, 3.12, 0.76)
        };

        foreach (var (bX, bY, cX, cY) in positions)
        {
            SetParameter(document, "bX", bX);
            SetParameter(document, "bY", bY);
            SetParameter(document, "cX", cX);
            SetParameter(document, "cY", cY);
            var evaluation = new GeometryEngine().Evaluate(document);
            Assert.Empty(evaluation.Errors);
            var targets = document.Solver.Targets.Select(target =>
                ConstructionTargetResolver.Resolve(
                    target,
                    evaluation)).ToList();
            Assert.Equal(2, targets.Count);
            Assert.All(targets, target =>
            {
                Assert.True(double.IsFinite(target.Position.X));
                Assert.True(double.IsFinite(target.Position.Y));
            });
        }
    }

    [Fact]
    public void AngleBisectorPair_ResolvesBothTargets()
    {
        var document = new ProjectDocument
        {
            Objects =
            [
                Point("A", 3, 3),
                Point("B", 0.3, 1.3),
                Point("C", 3.2, 0.85),
                Point("E", 1.2391149602562583, 0.2882370387946378),
                Point("P", 5.650611690077031, 0.46973266878115005)
            ]
        };
        var evaluation = new GeometryEngine().Evaluate(document);
        var m = ConstructionTargetResolver.Resolve(
            Target("M", "M"),
            evaluation);
        var n = ConstructionTargetResolver.Resolve(
            Target("N", "N"),
            evaluation);

        AssertClose(new Point2(
            1.398451393745629,
            1.991617544210211), m.Position);
        AssertClose(new Point2(
            3.1521917457062294,
            1.3639387336580366), n.Position);
    }

    [Fact]
    public void ParametricPoint_WithTwoParameters_DragsFreelyInRectangle()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"geometrygrid-free-point-{Guid.NewGuid():N}.json");
        try
        {
            ProjectSerializer.Save(new ProjectDocument
            {
                Parameters =
                [
                    new ParameterDefinition
                    {
                        Id = "bX",
                        Value = "0.3",
                        Min = "0.15",
                        Max = "0.65"
                    },
                    new ParameterDefinition
                    {
                        Id = "bY",
                        Value = "1.3",
                        Min = "1.1",
                        Max = "1.55"
                    }
                ],
                Objects =
                [
                    new GeometryObjectDefinition
                    {
                        Id = "B",
                        Type = "parametricPoint",
                        X = "bX",
                        Y = "bY",
                        ParameterX = "bX",
                        ParameterY = "bY",
                        Draggable = true
                    }
                ]
            }, path);
            var viewModel = new MainViewModel(new FakeDialogs(path));
            viewModel.OpenCommand.Execute(null);

            viewModel.MoveDraggablePoint(
                "B",
                new Point2(0.55, 1.45),
                completeDrag: true);

            var point = viewModel.Evaluation!.Results["B"].Point!.Value;
            AssertClose(new Point2(0.55, 1.45), point);
            Assert.Equal(
                0.55,
                double.Parse(
                    viewModel.Document!.Parameters.Single(
                        parameter => parameter.Id == "bX").Value,
                    System.Globalization.CultureInfo.InvariantCulture),
                12);
            Assert.Equal(
                1.45,
                double.Parse(
                    viewModel.Document.Parameters.Single(
                        parameter => parameter.Id == "bY").Value,
                    System.Globalization.CultureInfo.InvariantCulture),
                12);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ConstraintTarget_RoundTripKeepsDefinition()
    {
        var document = new ProjectDocument
        {
            Solver = new SolverSettings
            {
                Targets = [Target("M", "M")]
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            document,
            ProjectSerializer.Options);
        var loaded = ProjectSerializer.LoadJson(
            json,
            "constraint-roundtrip.json");

        var constraint = Assert.Single(loaded.Solver.Targets).Constraint;
        Assert.NotNull(constraint);
        Assert.Equal("angleBisectorPair", constraint.Type);
        Assert.Equal("M", constraint.Role);
        Assert.Equal("P", constraint.PointP);
    }

    [Fact]
    public void ParameterSampleCount_IncludesCurrentFrameAndChecksFarFramesFirst()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"geometrygrid-sample-count-{Guid.NewGuid():N}.json");
        try
        {
            ProjectSerializer.Save(new ProjectDocument
            {
                Parameters =
                [
                    new ParameterDefinition
                    {
                        Id = "t",
                        Value = "0.6",
                        Min = "0",
                        Max = "1"
                    }
                ],
                Objects =
                [
                    new GeometryObjectDefinition
                    {
                        Id = "P",
                        Type = "parametricPoint",
                        X = "t",
                        Y = "0",
                        ParameterX = "t",
                        Draggable = true
                    }
                ],
                Solver = new SolverSettings
                {
                    Targets =
                    [
                        new SolverTargetDefinition
                        {
                            Name = "T",
                            X = "t",
                            Y = "0"
                        }
                    ],
                    UseStraightedge = true,
                    UseCompass = false,
                    UseGrid = false,
                    ValidateParameters = true,
                    ParameterSampleCount = 7
                }
            }, path);
            var viewModel = new MainViewModel(new FakeDialogs(path));
            viewModel.OpenCommand.Execute(null);
            var method = typeof(MainViewModel)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(candidate =>
                    candidate.Name == "BuildParameterizedValidationScenarios" &&
                    candidate.GetParameters() is
                    [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(int));

            var scenarios =
                (IReadOnlyList<ConstructionSearchScenario>)method.Invoke(
                    viewModel,
                    [7])!;

            Assert.Equal(6, scenarios.Count);
            var distances = scenarios
                .Select(scenario =>
                    System.Math.Abs(
                        scenario.Targets.Single().Position.X - 0.6))
                .ToList();
            Assert.True(
                distances.Zip(distances.Skip(1), (left, right) =>
                        left + 1e-12 >= right)
                    .All(result => result));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static GeometryObjectDefinition Point(
        string id,
        double x,
        double y) =>
        new()
        {
            Id = id,
            Type = "fixedPoint",
            X = x.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
            Y = y.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static SolverTargetDefinition Target(
        string name,
        string role) =>
        new()
        {
            Name = name,
            Constraint = new SolverTargetConstraintDefinition
            {
                Type = "angleBisectorPair",
                Role = role,
                PointA = "A",
                PointB = "B",
                PointC = "C",
                PointE = "E",
                PointP = "P"
            }
        };

    private static void AssertClose(Point2 expected, Point2 actual)
    {
        Assert.InRange(
            System.Math.Abs(expected.X - actual.X),
            0,
            1e-7);
        Assert.InRange(
            System.Math.Abs(expected.Y - actual.Y),
            0,
            1e-7);
    }

    private static void SetParameter(
        ProjectDocument document,
        string id,
        double value)
    {
        document.Parameters.Single(parameter => parameter.Id == id).Value =
            value.ToString(
                "G17",
                System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class FakeDialogs(string path) : IFileDialogService
    {
        public string? OpenJson() => path;
        public string? SaveJson(string? currentPath) => path;
        public string? SaveExport(string extension, string filter) => null;
        public string? PickColor(string current) => null;
        public string? PromptText(
            string title,
            string prompt,
            string initialValue) => initialValue;
        public void ShowError(string title, string message) =>
            throw new InvalidOperationException($"{title}: {message}");
    }
}
