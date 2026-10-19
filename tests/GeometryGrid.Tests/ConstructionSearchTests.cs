using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Tests;

public sealed class ConstructionSearchTests
{
    [Fact]
    public void Search_MinStepsSkipsShallowerExhaustiveLayers()
    {
        var progressDepths = new List<int>();
        var report = new ConstructionSearchEngine().Search(
            [
                Point("A", 0, 0),
                Point("B", 1, 0),
                Point("C", 0, 1)
            ],
            [],
            "T",
            new Point2(0.314159, 0.271828),
            new ConstructionSearchOptions(
                MinSteps: 3,
                MaxSteps: 3,
                MaxSolutions: 1,
                MaxStates: 100,
                UseStraightedge: true,
                UseCompass: false,
                UseCollinearityClosure: false,
                UseGuidedSearch: false,
                UseExhaustiveSearch: true),
            new InlineProgress(value => progressDepths.Add(value.CurrentDepth)));

        Assert.Empty(report.Solutions);
        Assert.Contains(3, progressDepths);
        Assert.DoesNotContain(1, progressDepths);
        Assert.DoesNotContain(2, progressDepths);
    }

    [Fact]
    public void Search_MinStepsReturnsOnlySolutionsInsideRequestedRange()
    {
        var points = new[]
        {
            Point("A", 0, 0),
            Point("B", 2, 0),
            Point("C", 0, -1),
            Point("D", 2, 1)
        };
        var vertical = new GeometryResult
        {
            ObjectId = "x1",
            Type = "infiniteLine",
            IsValid = true,
            Line = new LineGeometry(
                new Point2(1, 0),
                new Vector2(0, 1))
        };

        var report = new ConstructionSearchEngine().Search(
            points,
            [new ConstructionSearchElement("known:x1", vertical)],
            "T",
            new Point2(1, 0),
            new ConstructionSearchOptions(
                MinSteps: 2,
                MaxSteps: 2,
                MaxSolutions: 1,
                MaxStates: 20_000,
                UseStraightedge: true,
                UseCompass: false,
                UseCollinearityClosure: false,
                UseGuidedSearch: false,
                UseExhaustiveSearch: true));

        Assert.NotEmpty(report.Solutions);
        Assert.All(report.Solutions, solution =>
            Assert.InRange(solution.StepCount, 2, 2));
    }

    [Fact]
    public void Search_ParallelExhaustiveModeFindsSameOneStrokeTarget()
    {
        var points = new[]
        {
            Point("A", 0, 0),
            Point("B", 2, 0),
            Point("C", 0, -1),
            Point("D", 2, 1)
        };
        var vertical = new GeometryResult
        {
            ObjectId = "x1",
            Type = "infiniteLine",
            IsValid = true,
            Line = new LineGeometry(
                new Point2(1, 0),
                new Vector2(0, 1))
        };

        var report = new ConstructionSearchEngine().Search(
            points,
            [new ConstructionSearchElement("known:x1", vertical)],
            "T",
            new Point2(1, 0),
            new ConstructionSearchOptions(
                MaxSteps: 2,
                MaxSolutions: 2,
                MaxStates: 20_000,
                UseStraightedge: true,
                UseCompass: false,
                UseCollinearityClosure: false,
                UseGuidedSearch: false,
                UseExhaustiveSearch: true,
                MaxDegreeOfParallelism: 4));

        Assert.NotEmpty(report.Solutions);
        Assert.All(
            report.Solutions,
            solution => Assert.Equal(1, solution.StepCount));
    }

    private sealed class InlineProgress(
        Action<ConstructionSearchProgress> report)
        : IProgress<ConstructionSearchProgress>
    {
        public void Report(ConstructionSearchProgress value) => report(value);
    }

    [Fact]
    public void Search_SingleTargetCanUseGuidedOnlyMode()
    {
        var report = new ConstructionSearchEngine().Search(
            [
                Point("A", 0, 0),
                Point("B", 1, 0),
                Point("C", 0, 1)
            ],
            [],
            "P",
            new Point2(0.321, 0.654),
            new ConstructionSearchOptions(
                MaxSteps: 4,
                MaxSolutions: 1,
                MaxStates: 10_000,
                UseStraightedge: true,
                UseCompass: false,
                UseCollinearityClosure: false,
                UseGuidedSearch: true,
                GuidedCandidateLimit: 4,
                UseExhaustiveSearch: false));

        Assert.Empty(report.Solutions);
        Assert.Equal("guided", report.Strategy);
        Assert.False(report.OptimalityProven);
    }

    [Fact]
    public void Search_FindsClassicalMidpointInThreeSteps()
    {
        var a = new ConstructionSearchPoint("known-point:A", new Point2(0, 0), "A");
        var d = new ConstructionSearchPoint("known-point:D", new Point2(2, 0), "D");
        var segment = new GeometryResult
        {
            ObjectId = "AD",
            Type = "segment",
            IsValid = true,
            Line = new LineGeometry(a.Position, d.Position - a.Position, 0, 1)
        };

        var report = new ConstructionSearchEngine().Search(
            [a, d],
            [new ConstructionSearchElement("known:AD", segment)],
            "P",
            new Point2(1, 0),
            new ConstructionSearchOptions(
                MaxSteps: 3,
                MaxSolutions: 2,
                MaxStates: 100_000,
                UseStraightedge: true,
                UseCompass: true));

        Assert.NotEmpty(report.Solutions);
        Assert.All(report.Solutions, solution => Assert.Equal(3, solution.StepCount));
        MathAndDependencyTests.AssertClose(new Point2(1, 0), report.Solutions[0].TargetPoint.Position);
    }

    [Fact]
    public void Search_ReturnsSeveralDifferentShortestSolutions()
    {
        var points = new[]
        {
            Point("A", 0, 0),
            Point("B", 2, 0),
            Point("C", 0, -1),
            Point("D", 2, 1)
        };
        var vertical = new GeometryResult
        {
            ObjectId = "x1",
            Type = "infiniteLine",
            IsValid = true,
            Line = new LineGeometry(new Point2(1, 0), new Vector2(0, 1))
        };

        var report = new ConstructionSearchEngine().Search(
            points,
            [new ConstructionSearchElement("known:x1", vertical)],
            "T",
            new Point2(1, 0),
            new ConstructionSearchOptions(
                MaxSteps: 2,
                MaxSolutions: 2,
                MaxStates: 20_000,
                UseStraightedge: true,
                UseCompass: false));

        Assert.Equal(2, report.Solutions.Count);
        Assert.All(report.Solutions, solution => Assert.Equal(1, solution.StepCount));
        Assert.Equal(2, report.Solutions.Select(solution => solution.Steps[0].Description).Distinct().Count());
    }

    [Fact]
    public void Search_DefaultVisibleDiagramFindsRequestedPointP()
    {
        var scene = new GeometryEngine().Evaluate(TestProject.Load()).Scene;
        var points = scene.Items
            .Where(item => item.Style.Visible && item.Geometry.Point is not null)
            .Select(item => new ConstructionSearchPoint(
                $"known-point:{item.Id}",
                item.Geometry.Point!.Value,
                item.Id))
            .ToList();
        var elements = scene.Items
            .Where(item => item.Style.Visible &&
                           (item.Geometry.Line is not null || item.Geometry.Circle is not null || item.Geometry.Arc is not null))
            .Select(item => new ConstructionSearchElement($"known:{item.Id}", item.Geometry))
            .ToList();

        var report = new ConstructionSearchEngine().Search(
            points,
            elements,
            "P",
            new Point2(7d / 8, 5d / 2),
            new ConstructionSearchOptions(
                MaxSteps: 3,
                MaxSolutions: 3,
                MaxStates: 500_000,
                UseStraightedge: true,
                UseCompass: true));

        Assert.NotEmpty(report.Solutions);
        Assert.InRange(report.Solutions[0].StepCount, 1, 3);
    }

    [Fact]
    public void Search_DefaultGridDiagramFindsJointPmnSolutionInFourSteps()
    {
        var scene = new GeometryEngine().Evaluate(TestProject.Load()).Scene;
        var points = scene.Items
            .Where(item => item.Style.Visible && item.Geometry.Point is not null)
            .Select(item => new ConstructionSearchPoint(
                $"known-point:{item.Id}",
                item.Geometry.Point!.Value,
                item.Id))
            .ToList();
        var elements = scene.Items
            .Where(item => item.Style.Visible &&
                           (item.Geometry.Line is not null ||
                            item.Geometry.Circle is not null ||
                            item.Geometry.Arc is not null))
            .Select(item => new ConstructionSearchElement($"known:{item.Id}", item.Geometry))
            .ToList();

        for (var x = 0; x <= 8; x++)
        {
            elements.Add(new ConstructionSearchElement(
                $"grid:x:{x}",
                InfiniteLine($"grid:x:{x}", new Point2(x, 0), new Vector2(0, 1))));
            for (var y = 0; y <= 8; y++)
                points.Add(new ConstructionSearchPoint(
                    $"grid-point:{x}:{y}",
                    new Point2(x, y)));
        }
        for (var y = 0; y <= 8; y++)
            elements.Add(new ConstructionSearchElement(
                $"grid:y:{y}",
                InfiniteLine($"grid:y:{y}", new Point2(0, y), new Vector2(1, 0))));

        var completedPoints = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point => point.X is >= 0 and <= 8 && point.Y is >= 0 and <= 8);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var report = new ConstructionSearchEngine().Search(
            completedPoints,
            elements,
            [
                new ConstructionSearchTarget("P", new Point2(7d / 8, 5d / 2)),
                new ConstructionSearchTarget("M", new Point2(17d / 6, 19d / 6)),
                new ConstructionSearchTarget("N", new Point2(7d / 2, 29d / 24))
            ],
            new ConstructionSearchOptions(
                MaxSteps: 4,
                MaxSolutions: 1,
                MaxStates: 50_000,
                MaxPointsPerState: 240,
                UseStraightedge: true,
                UseCompass: false,
                MinX: 0,
                MaxX: 8,
                MinY: 0,
                MaxY: 8));
        stopwatch.Stop();

        Assert.True(
            report.Solutions.Count == 1,
            $"solutions={report.Solutions.Count}, explored={report.ExploredStates}, "
            + $"limit={report.StateLimitReached}, depth={report.SearchedDepth}");
        var solution = report.Solutions[0];
        Assert.Equal(4, solution.StepCount);
        Assert.Equal(3, solution.CompletedTargets.Count);
        Assert.False(report.StateLimitReached);
        Assert.InRange(report.ExploredStates, 1, 1_000);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"目标反推搜索耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");
    }

    [Fact]
    public void Search_Tianjin2026GridTargetsShareTextbookThreeStrokeSolution()
    {
        var a = Point("A", 1, 1);
        var b = Point("B", 3, 4);
        var c = Point("C", 7, 4d / 3);
        var points = new List<ConstructionSearchPoint> { a, b, c };
        for (var x = 0; x <= 8; x++)
        for (var y = 0; y <= 8; y++)
            points.Add(new ConstructionSearchPoint(
                $"grid-point:{x}:{y}",
                new Point2(x, y)));

        var center = new Point2(
            (a.Position.X + c.Position.X) / 2,
            (a.Position.Y + c.Position.Y) / 2);
        var radius = a.Position.DistanceTo(c.Position) / 2;
        var angleA = System.Math.Atan2(
            a.Position.Y - center.Y,
            a.Position.X - center.X);
        var angleC = System.Math.Atan2(
            c.Position.Y - center.Y,
            c.Position.X - center.X);
        var midpointFromA = new Point2(
            center.X + radius * System.Math.Cos(angleA + System.Math.PI / 2),
            center.Y + radius * System.Math.Sin(angleA + System.Math.PI / 2));
        var upperArcStart = midpointFromA.Y >= center.Y ? angleA : angleC;
        var elements = new List<ConstructionSearchElement>
        {
            new("known:segment_AB", Segment("segment_AB", a.Position, b.Position)),
            new("known:segment_BC", Segment("segment_BC", b.Position, c.Position)),
            new("known:segment_AC", Segment("segment_AC", a.Position, c.Position)),
            new("known:arc_AC", new GeometryResult
            {
                ObjectId = "arc_AC",
                Type = "semicircleByDiameter",
                IsValid = true,
                Arc = new ArcGeometry(center, radius, upperArcStart, System.Math.PI)
            })
        };
        for (var x = 0; x <= 8; x++)
            elements.Add(new ConstructionSearchElement(
                $"grid:x:{x}",
                InfiniteLine($"grid:x:{x}", new Point2(x, 0), new Vector2(0, 1))));
        for (var y = 0; y <= 8; y++)
            elements.Add(new ConstructionSearchElement(
                $"grid:y:{y}",
                InfiniteLine($"grid:y:{y}", new Point2(0, y), new Vector2(1, 0))));

        var completedPoints = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point => point.X is >= 0 and <= 8 && point.Y is >= 0 and <= 8);
        var e = new Point2(2, 19d / 18);
        var f = new Point2(4, 7d / 6);
        Assert.Contains(completedPoints, point =>
            point.Position.DistanceTo(e) < 1e-7 &&
            point.ParentElementA is not null &&
            point.ParentElementB is not null);
        Assert.Contains(completedPoints, point =>
            point.Position.DistanceTo(f) < 1e-7 &&
            point.ParentElementA is not null &&
            point.ParentElementB is not null);

        var engine = new ConstructionSearchEngine();
        var sharedOptions = new ConstructionSearchOptions(
            MaxSteps: 3,
            MaxSolutions: 3,
            MaxStates: 200_000,
            MaxPointsPerState: 180,
            UseStraightedge: true,
            UseCompass: false);

        var targetM = new Point2(13d / 3, 28d / 9);
        var targetN = new Point2(207d / 97, 500d / 291);
        var report = engine.Search(
            completedPoints,
            elements,
            [
                new ConstructionSearchTarget("M", targetM),
                new ConstructionSearchTarget("N", targetN)
            ],
            sharedOptions);

        Assert.NotEmpty(report.Solutions);
        Assert.Equal(3, report.Solutions[0].StepCount);
        Assert.True(report.OptimalityProven);
        Assert.False(report.StateLimitReached);
        Assert.Equal(2, report.Solutions[0].CompletedTargets.Count);
        MathAndDependencyTests.AssertClose(
            targetM,
            report.Solutions[0].CompletedTargets.Single(target => target.Name == "M").Point.Position);
        MathAndDependencyTests.AssertClose(
            targetN,
            report.Solutions[0].CompletedTargets.Single(target => target.Name == "N").Point.Position);

        var definingPoints = report.Solutions[0].Steps
            .SelectMany(step => new[] { step.PointA.Position, step.PointB.Position })
            .ToList();
        Assert.Contains(definingPoints, point => point.DistanceTo(e) < 1e-7);
        Assert.Contains(definingPoints, point => point.DistanceTo(f) < 1e-7);
        Assert.Contains(definingPoints, point => point.DistanceTo(new Point2(5, 7)) < 1e-7);
        Assert.Contains(definingPoints, point => point.DistanceTo(new Point2(3, 6)) < 1e-7);
    }

    [Fact]
    public void Search_Wht2UsesCollinearityClosureAndFindsFourStrokeJointSolution()
    {
        var root17 = System.Math.Sqrt(17);
        var a = Point("A", 1, 4);
        var b = Point("B", 5, 3);
        var e = Point("E", 6, 1);
        var c = Point("C", 2 + root17, (15 - root17) / 4);
        var d = Point("D", (3 + root17) / 2, 6);
        var f = Point(
            "F",
            (551 + 105 * root17) / 159,
            (1125 + 7 * root17) / 318);
        var points = new List<ConstructionSearchPoint> { a, b, c, d, e, f };
        var circleCenter = new Point2(
            (a.Position.X + c.Position.X) / 2,
            (a.Position.Y + c.Position.Y) / 2);
        var elements = new List<ConstructionSearchElement>
        {
            new("known:circle_AC", new GeometryResult
            {
                ObjectId = "circle_AC",
                Type = "circleByDiameter",
                IsValid = true,
                Circle = new CircleGeometry(
                    circleCenter,
                    a.Position.DistanceTo(c.Position) / 2)
            }),
            new("known:line_CE", InfiniteLine(
                "line_CE",
                c.Position,
                e.Position - c.Position)),
            new("known:AC", Segment("AC", a.Position, c.Position)),
            new("known:AF", Segment("AF", a.Position, f.Position))
        };
        for (var x = 0; x <= 7; x++)
        {
            elements.Add(new ConstructionSearchElement(
                $"grid:x:{x}",
                InfiniteLine(
                    $"grid:x:{x}",
                    new Point2(x, 0),
                    new Vector2(0, 1))));
            for (var y = 0; y <= 7; y++)
                points.Add(new ConstructionSearchPoint(
                    $"grid-point:{x}:{y}",
                    new Point2(x, y)));
        }
        for (var y = 0; y <= 7; y++)
            elements.Add(new ConstructionSearchElement(
                $"grid:y:{y}",
                InfiniteLine(
                    $"grid:y:{y}",
                    new Point2(0, y),
                    new Vector2(1, 0))));

        var completedPoints = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point =>
                point.X is >= 0 and <= 7 &&
                point.Y is >= 0 and <= 7);
        var targetM = new Point2((21 + 35 * root17) / 26, 6);
        var targetN = new Point2((367 + 35 * root17) / 86, 6);
        var targetP = new Point2(
            (6880 + 2847 * root17) / 3617,
            (15289 + 1091 * root17) / 3617);
        Assert.Contains(completedPoints, point =>
            point.Position.DistanceTo(targetM) < 1e-7);
        var q = new Point2(1, (15 - root17) / 4);
        Assert.Contains(completedPoints, point =>
            point.Position.DistanceTo(q) < 1e-7);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var report = new ConstructionSearchEngine().Search(
            completedPoints,
            elements,
            [
                new ConstructionSearchTarget("M", targetM),
                new ConstructionSearchTarget("N", targetN),
                new ConstructionSearchTarget("P", targetP)
            ],
            new ConstructionSearchOptions(
                MaxSteps: 6,
                MaxSolutions: 1,
                MaxStates: 100,
                MaxPointsPerState: 240,
                UseStraightedge: true,
                UseCompass: false,
                MinX: 0,
                MaxX: 7,
                MinY: 0,
                MaxY: 7));
        stopwatch.Stop();

        var solution = Assert.Single(report.Solutions);
        Assert.Equal(4, solution.StepCount);
        Assert.Equal(3, solution.CompletedTargets.Count);
        Assert.Equal("collinearity-closure", report.Strategy);
        Assert.InRange(report.ExploredStates, 1, 100);
        var playback = ConstructionPlaybackPlanner.Create(solution);
        Assert.Equal(
            [
                ConstructionDisplayKind.Segment,
                ConstructionDisplayKind.Segment,
                ConstructionDisplayKind.Ray,
                ConstructionDisplayKind.Ray
            ],
            playback.Select(step => step.DisplayKind));
        MathAndDependencyTests.AssertClose(targetP, playback[2].End);
        MathAndDependencyTests.AssertClose(targetN, playback[3].End);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"共线闭包搜索耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。");
        MathAndDependencyTests.AssertClose(
            targetP,
            solution.CompletedTargets.Single(target => target.Name == "P").Point.Position);
        MathAndDependencyTests.AssertClose(
            targetN,
            solution.CompletedTargets.Single(target => target.Name == "N").Point.Position);
    }

    [Fact]
    public void CompleteKnownIntersections_AddsLineGridPointAtZeroCost()
    {
        var a = Point("A", 1, 1);
        var c = Point("C", 7, 4d / 3);
        var segment = new ConstructionSearchElement(
            "known:AC",
            Segment("AC", a.Position, c.Position));
        var gridLine = new ConstructionSearchElement(
            "grid:x:2",
            InfiniteLine("grid:x:2", new Point2(2, 0), new Vector2(0, 1)));

        var points = ConstructionSearchEngine.CompleteKnownIntersections(
            [a, c],
            [segment, gridLine]);

        var intersection = Assert.Single(points.Where(point =>
            point.Position.DistanceTo(new Point2(2, 19d / 18)) < 1e-7));
        Assert.Equal("known:AC", intersection.ParentElementA);
        Assert.Equal("grid:x:2", intersection.ParentElementB);
    }

    [Fact]
    public void ParameterizedReplay_AcceptsUniversalFourStrokeConstruction()
    {
        var baseline = Tianjin2025MovingScenario(1.5);
        var c = baseline.InitialPoints.Single(point => point.ExistingPointId == "C");
        var g37 = baseline.InitialPoints.Single(point => point.Key == "grid-point:3:7");
        var g35 = baseline.InitialPoints.Single(point => point.Key == "grid-point:3:5");
        var e = baseline.InitialPoints.Single(point => point.Key == "grid-point:0:5");
        var r = baseline.InitialPoints.Single(point =>
            SameParents(point, "known:AC", "grid:y:2"));
        var u = new ConstructionSearchPoint(
            "moving:U",
            new Point2(41d / 11, 3),
            ParentElementA: "step:0",
            ParentElementB: "grid:y:3",
            Selector: "first");
        var v = new ConstructionSearchPoint(
            "moving:V",
            new Point2(37d / 11, 5),
            ParentElementA: "step:0",
            ParentElementB: "grid:y:5",
            Selector: "first");
        var w = new ConstructionSearchPoint(
            "moving:W",
            new Point2(3.623376623376623, 23d / 7),
            ParentElementA: "step:1",
            ParentElementB: "step:2",
            Selector: "first");
        var target = MovingTarget(1.5);
        var solution = new ConstructionSolution(
            "P",
            target,
            [
                new ConstructionStep("step:0", ConstructionToolKind.StraightLine, c, g37, "CG"),
                new ConstructionStep("step:1", ConstructionToolKind.StraightLine, u, g35, "UH"),
                new ConstructionStep("step:2", ConstructionToolKind.StraightLine, v, r, "VR"),
                new ConstructionStep("step:3", ConstructionToolKind.StraightLine, e, w, "EW")
            ],
            new ConstructionSearchPoint("target:P", target),
            0);
        var options = new ConstructionSearchOptions(
            MaxSteps: 4,
            MaxPointsPerState: 300,
            UseStraightedge: true,
            UseCompass: false,
            MinX: 0,
            MaxX: 10,
            MinY: 0,
            MaxY: 9);

        foreach (var t in new[] { 1.05, 1.2, 1.5, 1.8, 1.95 })
        {
            var replayed = ConstructionSearchEngine.ReplaySolution(
                solution,
                Tianjin2025MovingScenario(t),
                options);
            Assert.NotNull(replayed);
            Assert.Equal(solution.Steps.Select(step => step.Key), replayed.Steps.Select(step => step.Key));
            MathAndDependencyTests.AssertClose(
                new Point2(4, t),
                replayed.Steps[0].PointA.Position);
        }
    }

    [Fact]
    public void ParameterizedReplay_RejectsThreeStrokeSingleFrameCoincidence()
    {
        var baseline = Tianjin2025MovingScenario(1.5);
        var q = baseline.InitialPoints.Single(point =>
            SameParents(point, "known:AC", "grid:x:3"));
        var s = baseline.InitialPoints.Single(point =>
            SameParents(point, "known:CD", "grid:x:5"));
        var a = baseline.InitialPoints.Single(point => point.ExistingPointId == "A");
        var g19 = baseline.InitialPoints.Single(point => point.Key == "grid-point:1:9");
        var e = baseline.InitialPoints.Single(point => point.Key == "grid-point:0:5");
        var witness = new ConstructionSearchPoint(
            "single-frame:W",
            new Point2(3.623376623376623, 23d / 7),
            ParentElementA: "step:0",
            ParentElementB: "step:1",
            Selector: "first");
        var target = MovingTarget(1.5);
        var solution = new ConstructionSolution(
            "P",
            target,
            [
                new ConstructionStep("step:0", ConstructionToolKind.StraightLine, q, s, "QS"),
                new ConstructionStep("step:1", ConstructionToolKind.StraightLine, a, g19, "AG"),
                new ConstructionStep("step:2", ConstructionToolKind.StraightLine, e, witness, "EW")
            ],
            new ConstructionSearchPoint("target:P", target),
            0);
        var options = new ConstructionSearchOptions(
            MaxSteps: 3,
            MaxPointsPerState: 300,
            UseStraightedge: true,
            UseCompass: false,
            MinX: 0,
            MaxX: 10,
            MinY: 0,
            MaxY: 9);

        Assert.True(ConstructionSearchEngine.ReplaysSolution(solution, baseline, options));
        Assert.False(ConstructionSearchEngine.ReplaysSolution(
            solution,
            Tianjin2025MovingScenario(1.25),
            options));
    }

    [Fact]
    public void Search_MovingTianjinDiagramOnlyReturnsCrossPositionSolution()
    {
        var baseline = Tianjin2025MovingScenario(1.5);
        var validationScenarios = new[] { 1.1, 1.25, 1.75, 1.9 }
            .Select(Tianjin2025MovingScenario)
            .ToList();
        var report = new ConstructionSearchEngine().Search(
            baseline.InitialPoints,
            baseline.InitialElements,
            baseline.Targets,
            new ConstructionSearchOptions(
                MaxSteps: 4,
                MaxSolutions: 1,
                MaxStates: 500_000,
                MaxPointsPerState: 300,
                UseStraightedge: true,
                UseCompass: false,
                MinX: 0,
                MaxX: 10,
                MinY: 0,
                MaxY: 9,
                ValidationScenarios: validationScenarios));

        var solution = Assert.Single(report.Solutions);
        Assert.Equal(4, solution.StepCount);
        Assert.Equal("parameterized-two-level-collinearity", report.Strategy);
        Assert.All(validationScenarios, scenario =>
            Assert.True(ConstructionSearchEngine.ReplaysSolution(
                solution,
                scenario,
                new ConstructionSearchOptions(
                    MaxSteps: 4,
                    MaxPointsPerState: 300,
                    UseStraightedge: true,
                    UseCompass: false,
                    MinX: 0,
                    MaxX: 10,
                    MinY: 0,
                    MaxY: 9))));
    }

    private static ConstructionSearchScenario Tianjin2025MovingScenario(double t)
    {
        var a = Point("A", 2, 7);
        var b = Point("B", 6, 3);
        var c = Point("C", 4, t);
        var d = Point("D", (t * t - 10 * t + 29) / 2, 3);
        var points = new List<ConstructionSearchPoint> { a, b, c, d };
        var center = new Point2(4, 5);
        var elements = new List<ConstructionSearchElement>
        {
            new("known:AB", Segment("AB", a.Position, b.Position)),
            new("known:AC", Segment("AC", a.Position, c.Position)),
            new("known:CD", Segment("CD", c.Position, d.Position)),
            new("known:circle_AB", new GeometryResult
            {
                ObjectId = "circle_AB",
                Type = "circleByDiameter",
                IsValid = true,
                Circle = new CircleGeometry(center, 2 * System.Math.Sqrt(2))
            })
        };
        for (var x = 0; x <= 10; x++)
        {
            elements.Add(new ConstructionSearchElement(
                $"grid:x:{x}",
                InfiniteLine($"grid:x:{x}", new Point2(x, 0), new Vector2(0, 1))));
            for (var y = 0; y <= 9; y++)
                points.Add(new ConstructionSearchPoint(
                    $"grid-point:{x}:{y}",
                    new Point2(x, y)));
        }
        for (var y = 0; y <= 9; y++)
            elements.Add(new ConstructionSearchElement(
                $"grid:y:{y}",
                InfiniteLine($"grid:y:{y}", new Point2(0, y), new Vector2(1, 0))));

        var completed = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point =>
                point.X is >= 0 and <= 10 &&
                point.Y is >= 0 and <= 9);
        return new ConstructionSearchScenario(
            completed,
            elements,
            [new ConstructionSearchTarget("P", MovingTarget(t))],
            $"t={t}");
    }

    private static Point2 MovingTarget(double t)
    {
        var m = 2 * (t - 7) / (t * t - 12 * t + 39);
        var x = (4 - 2 * System.Math.Sqrt(2 * (1 - m * m))) / (1 + m * m);
        return new Point2(x, 5 + m * x);
    }

    private static bool SameParents(
        ConstructionSearchPoint point,
        string first,
        string second) =>
        point.ParentElementA == first && point.ParentElementB == second ||
        point.ParentElementA == second && point.ParentElementB == first;

    private static GeometryResult Segment(string id, Point2 a, Point2 b) =>
        new()
        {
            ObjectId = id,
            Type = "segment",
            IsValid = true,
            Line = new LineGeometry(a, b - a, 0, 1)
        };

    private static GeometryResult InfiniteLine(
        string id,
        Point2 origin,
        Vector2 direction) =>
        new()
        {
            ObjectId = id,
            Type = "infiniteLine",
            IsValid = true,
            Line = new LineGeometry(origin, direction)
        };

    private static ConstructionSearchPoint Point(string id, double x, double y) =>
        new($"known-point:{id}", new Point2(x, y), id);
}
