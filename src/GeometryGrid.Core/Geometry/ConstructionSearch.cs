using System.Collections.Concurrent;
using System.Globalization;

namespace GeometryGrid.Core.Geometry;

public enum ConstructionToolKind
{
    StraightLine,
    Circle
}

public sealed record ConstructionSearchPoint(
    string Key,
    Point2 Position,
    string? ExistingPointId = null,
    string? ParentElementA = null,
    string? ParentElementB = null,
    string? Selector = null);

public sealed record ConstructionSearchElement(string Key, GeometryResult Geometry);

public sealed record ConstructionSearchTarget(string Name, Point2 Position);

public sealed record ConstructionSearchScenario(
    IReadOnlyList<ConstructionSearchPoint> InitialPoints,
    IReadOnlyList<ConstructionSearchElement> InitialElements,
    IReadOnlyList<ConstructionSearchTarget> Targets,
    string Name = "");

public sealed record ConstructionResolvedTarget(
    string Name,
    Point2 Position,
    ConstructionSearchPoint Point);

public sealed record ConstructionStep(
    string Key,
    ConstructionToolKind Tool,
    ConstructionSearchPoint PointA,
    ConstructionSearchPoint PointB,
    string Description);

public sealed record ConstructionSolution(
    string TargetName,
    Point2 Target,
    IReadOnlyList<ConstructionStep> Steps,
    ConstructionSearchPoint TargetPoint,
    long ExploredStates,
    IReadOnlyList<ConstructionResolvedTarget>? Targets = null)
{
    public int StepCount => Steps.Count;
    public IReadOnlyList<ConstructionResolvedTarget> CompletedTargets =>
        Targets is { Count: > 0 }
            ? Targets
            : [new ConstructionResolvedTarget(TargetName, Target, TargetPoint)];
}

public sealed record ConstructionSearchOptions(
    int MaxSteps = 5,
    int MaxSolutions = 3,
    long MaxStates = 200_000,
    int MaxPointsPerState = 90,
    bool UseStraightedge = true,
    bool UseCompass = true,
    double Tolerance = 1e-7,
    double? MinX = null,
    double? MaxX = null,
    double? MinY = null,
    double? MaxY = null,
    bool UseCollinearityClosure = true,
    bool UseGuidedSearch = true,
    int GuidedCandidateLimit = 8,
    bool UseExhaustiveSearch = true,
    IReadOnlyList<ConstructionSearchScenario>? ValidationScenarios = null,
    IConstructionCandidateAccelerator? CandidateAccelerator = null,
    int AcceleratedGuidedPreselectionLimit = 8_192,
    int MaxDegreeOfParallelism = 1,
    int MinSteps = 1);

public sealed record ConstructionSearchProgress(
    int CurrentDepth,
    long ExploredStates,
    int SolutionsFound,
    string Message);

public sealed record ConstructionSearchReport(
    IReadOnlyList<ConstructionSolution> Solutions,
    long ExploredStates,
    bool StateLimitReached,
    int SearchedDepth,
    bool OptimalityProven = true,
    string Strategy = "");

/// <summary>
/// Bounded iterative-deepening straightedge-and-compass search. Every newly
/// drawn line/circle is intersected with all existing elements and all new
/// intersection points become available to subsequent steps. Equivalent element
/// sets are merged with a transposition table, so independent strokes can be
/// explored in any valid order without factorial duplication.
/// </summary>
public sealed class ConstructionSearchEngine
{
    private sealed class ParallelSearchContext(
        int depthLimit,
        ConstructionSearchOptions options,
        long exploredBeforeDepth,
        IProgress<ConstructionSearchProgress>? progress)
    {
        private long _exploredAtDepth;
        private int _limitReached;
        private int _solutionCount;

        public int DepthLimit { get; } = depthLimit;
        public ConstructionSearchOptions Options { get; } = options;
        public long ExploredBeforeDepth { get; } = exploredBeforeDepth;
        public IProgress<ConstructionSearchProgress>? Progress { get; } = progress;
        public ConcurrentBag<ConstructionSolution> Solutions { get; } = [];
        public ConcurrentDictionary<string, byte> SolutionSignatures { get; } =
            new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, byte> VisitedStates { get; } =
            new(StringComparer.Ordinal);

        public long ExploredAtDepth => Interlocked.Read(ref _exploredAtDepth);
        public bool LimitReached => Volatile.Read(ref _limitReached) != 0;
        public int SolutionCount => Volatile.Read(ref _solutionCount);
        public bool ShouldStop => LimitReached || SolutionCount >= Options.MaxSolutions;

        public bool TryReserveState()
        {
            var value = Interlocked.Increment(ref _exploredAtDepth);
            if (value > Options.MaxStates)
            {
                Interlocked.Exchange(ref _limitReached, 1);
                return false;
            }

            if (value % 2_000 == 0)
            {
                Progress?.Report(new ConstructionSearchProgress(
                    DepthLimit,
                    ExploredBeforeDepth + value,
                    SolutionCount,
                    $"已并行检查 {ExploredBeforeDepth + value:N0} 个候选状态"));
            }
            return true;
        }

        public void AddSolution(string signature, ConstructionSolution solution)
        {
            if (ShouldStop || !SolutionSignatures.TryAdd(signature, 0))
                return;
            Solutions.Add(solution);
            Interlocked.Increment(ref _solutionCount);
        }
    }

    private sealed record SearchElement(
        string Key,
        string Signature,
        GeometryResult Geometry,
        ConstructionStep? Step);

    private sealed record GuidedCandidate(
        SearchElement Candidate,
        IReadOnlyList<ConstructionSearchPoint> AddedPoints,
        int ResolvedTargets,
        int CarrierScore,
        bool CanFinishNext,
        int Naturalness,
        double TargetDistance);

    private sealed record SingleTargetCarrierPlan(
        ConstructionSearchPoint Anchor,
        SearchElement FirstTemplate,
        SearchElement SecondTemplate,
        Point2 Witness,
        string CarrierSignature,
        int Naturalness);

    private readonly record struct TargetCarrierKey(long A, long B, long C);

    private sealed record ParameterizedGridArmBranch(
        ConstructionSearchPoint PrefixPointA,
        ConstructionSearchPoint PrefixPointB,
        string PrefixIntersectionElementKey,
        string PrefixIntersectionSelector,
        Point2 PrefixIntersection,
        ConstructionSearchPoint ArmAnchor,
        string GridElementKey,
        Point2 GridIntersection);

    private sealed record ParameterizedTwoPrefixBranch(
        ConstructionSearchPoint FirstLinePointA,
        ConstructionSearchPoint FirstLinePointB,
        ConstructionSearchPoint SecondLinePointA,
        ConstructionSearchPoint SecondLinePointB,
        Point2 Intersection);

    private sealed record ParallelStrategyJob(
        string Name,
        ConstructionSearchOptions Options);

    public ConstructionSearchReport Search(
        IReadOnlyList<ConstructionSearchPoint> initialPoints,
        IReadOnlyList<ConstructionSearchElement> initialElements,
        string targetName,
        Point2 target,
        ConstructionSearchOptions options,
        IProgress<ConstructionSearchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Search(
            initialPoints,
            initialElements,
            [new ConstructionSearchTarget(targetName, target)],
            options,
            progress,
            cancellationToken);

    public ConstructionSearchReport Search(
        IReadOnlyList<ConstructionSearchPoint> initialPoints,
        IReadOnlyList<ConstructionSearchElement> initialElements,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        IProgress<ConstructionSearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(options);
        ValidateTargets(targets);
        var basePoints = DistinctPoints(initialPoints, options.Tolerance);
        var baseElements = initialElements
            .Where(item => item.Geometry.IsValid &&
                           (item.Geometry.Line is not null || item.Geometry.Circle is not null || item.Geometry.Arc is not null))
            .Select(item => new SearchElement(
                item.Key,
                ElementSignature(item.Geometry, preserveRange: true),
                item.Geometry,
                null))
            .DistinctBy(item => item.Signature)
            .ToList();

        var knownTargets = ResolveTargets(basePoints, targets, options.Tolerance);
        if (knownTargets.Count == targets.Count &&
            options.MinSteps == 0)
        {
            var first = knownTargets[0];
            var knownSolution = new ConstructionSolution(
                    JointTargetName(targets),
                    first.Position,
                    [],
                    first.Point,
                    0,
                    knownTargets);
            if (SolutionMatchesValidationScenarios(knownSolution, options))
                return new ConstructionSearchReport(
                    [knownSolution],
                    0,
                    false,
                    0);
        }

        if (options.MaxDegreeOfParallelism > 1 &&
            options.UseCollinearityClosure &&
            options.UseGuidedSearch &&
            options.UseExhaustiveSearch)
        {
            return SearchStrategyFamiliesInParallel(
                basePoints,
                baseElements.Select(element =>
                    new ConstructionSearchElement(
                        element.Key,
                        element.Geometry)).ToList(),
                targets,
                options,
                progress,
                cancellationToken);
        }

        var specializedExplored = 0L;
        // 含参搜索先走普通基准图状态树：每个状态只计算当前画面，
        // 仅当完整候选已经命中目标时，才逐个回放额外参数位置。
        // 这条便宜的筛选通道失败后，再进入专用的不定图结构反推。
        if (options.ValidationScenarios is { Count: > 0 } &&
            options.UseGuidedSearch &&
            options.MaxSteps >= 4)
        {
            progress?.Report(new ConstructionSearchProgress(
                options.MaxSteps,
                0,
                0,
                "正在进行基准图候选筛选…"));
            var guidedExplored = 0L;
            var points = basePoints.ToList();
            var elements = baseElements.ToList();
            var steps = new List<ConstructionStep>();
            var visitedStates = new HashSet<string>(StringComparer.Ordinal);
            var baseFirstOptions = options with
            {
                // 这里只承担廉价的第一道筛选，不能抢走专用含参算法的预算。
                MaxStates = System.Math.Min(
                    5_000,
                    System.Math.Max(500, options.MaxStates / 100)),
                GuidedCandidateLimit = System.Math.Min(
                    3,
                    options.GuidedCandidateLimit)
            };
            if (TryGuidedSearch(
                    points,
                    elements,
                    steps,
                    baseFirstOptions.MaxSteps,
                    targets,
                    baseFirstOptions,
                    visitedStates,
                    ref guidedExplored,
                    cancellationToken,
                    out var solution) &&
                solution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [solution!],
                    guidedExplored,
                    false,
                    solution!.StepCount,
                    false,
                    "base-first-guided");
            }
            specializedExplored += guidedExplored;
        }

        if (options.ValidationScenarios is { Count: > 0 } &&
            options.UseCollinearityClosure &&
            options.UseStraightedge &&
            options.MaxSteps >= 4 &&
            targets.Count == 1)
        {
            progress?.Report(new ConstructionSearchProgress(
                4,
                0,
                0,
                "正在进行不定图双层共线反推…"));
            var parameterizedExplored = 0L;
            if (TryParameterizedTwoLevelCollinearitySearch(
                    basePoints,
                    baseElements,
                    targets[0],
                    options,
                    ref parameterizedExplored,
                    cancellationToken,
                    out var parameterizedSolution) &&
                parameterizedSolution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [parameterizedSolution!],
                    parameterizedExplored,
                    false,
                    parameterizedSolution!.StepCount,
                    false,
                    "parameterized-two-level-collinearity");
            }
            specializedExplored += parameterizedExplored;
        }

        if (options.UseCollinearityClosure &&
            options.UseStraightedge &&
            options.MaxSteps >= 2 &&
            targets.Count >= 2)
        {
            progress?.Report(new ConstructionSearchProgress(
                options.MaxSteps,
                specializedExplored,
                0,
                "正在反推“主目标 + 一笔派生目标”构造…"));
            var dependentExplored = 0L;
            if (TryDependentTargetReductionSearch(
                    basePoints,
                    baseElements,
                    targets,
                    options,
                    ref dependentExplored,
                    cancellationToken,
                    out var dependentSolution) &&
                dependentSolution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [dependentSolution!],
                    specializedExplored + dependentExplored,
                    false,
                    dependentSolution!.StepCount,
                    false,
                    "dependent-target-reduction");
            }
            specializedExplored += dependentExplored;
        }

        if (options.UseCollinearityClosure &&
            options.UseStraightedge &&
            options.MaxSteps >= 3 &&
            targets.Count == 1)
        {
            var closureExplored = 0L;
            if (TrySingleTargetDoubleCollinearitySearch(
                    basePoints,
                    baseElements,
                    targets[0],
                    options,
                    ref closureExplored,
                    cancellationToken,
                    out var closureSolution) &&
                closureSolution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [closureSolution!],
                    closureExplored,
                    false,
                    closureSolution!.StepCount,
                    false,
                    "single-target-double-collinearity");
            }
            specializedExplored += closureExplored;
        }

        if (options.UseCollinearityClosure &&
            options.UseStraightedge &&
            options.MaxSteps >= 4 &&
            targets.Count >= 2)
        {
            progress?.Report(new ConstructionSearchProgress(
                options.MaxSteps,
                0,
                0,
                "正在进行共线闭包反推…"));
            var collinearityExplored = 0L;
            if (TryCollinearityClosureSearch(
                    basePoints,
                    baseElements,
                    targets,
                    options,
                    ref collinearityExplored,
                    cancellationToken,
                    out var collinearitySolution) &&
                collinearitySolution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [collinearitySolution!],
                    collinearityExplored,
                    false,
                    collinearitySolution!.StepCount,
                    false,
                    "collinearity-closure");
            }
            specializedExplored += collinearityExplored;
        }

        if (options.ValidationScenarios is not { Count: > 0 } &&
            options.UseGuidedSearch &&
            options.MaxSteps >= 4)
        {
            progress?.Report(new ConstructionSearchProgress(
                options.MaxSteps,
                0,
                0,
                "正在进行目标反推快速搜索…"));
            var guidedExplored = 0L;
            var points = basePoints.ToList();
            var elements = baseElements.ToList();
            var steps = new List<ConstructionStep>();
            var visitedStates = new HashSet<string>(StringComparer.Ordinal);
            if (TryGuidedSearch(
                    points,
                    elements,
                    steps,
                    options.MaxSteps,
                    targets,
                    options,
                    visitedStates,
                    ref guidedExplored,
                    cancellationToken,
                    out var solution) &&
                solution!.StepCount >= options.MinSteps)
            {
                return new ConstructionSearchReport(
                    [solution!],
                    guidedExplored,
                    false,
                    solution!.StepCount,
                    false);
            }
            specializedExplored += guidedExplored;
        }

        if (!options.UseExhaustiveSearch)
            return new ConstructionSearchReport(
                [],
                specializedExplored,
                false,
                options.MaxSteps,
                false,
                options.UseGuidedSearch ? "guided" : "collinearity-closure");

        var explored = 0L;
        var anyLimitReached = false;
        var shallowerDepthIncomplete = false;
        var searchedDepth = 0;
        for (var depth = System.Math.Max(1, options.MinSteps);
             depth <= options.MaxSteps;
             depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            searchedDepth = depth;
            progress?.Report(new ConstructionSearchProgress(
                depth,
                explored,
                0,
                $"正在搜索 {depth} 笔构造…"));

            var points = basePoints.ToList();
            var elements = baseElements.ToList();
            var steps = new List<ConstructionStep>();
            var solutions = new List<ConstructionSolution>();
            var solutionSignatures = new HashSet<string>(StringComparer.Ordinal);
            var visitedStates = new HashSet<string>(StringComparer.Ordinal);
            var exploredAtDepth = 0L;
            var depthLimitReached = false;

            if (options.MaxDegreeOfParallelism > 1)
            {
                var parallel = SearchDepthParallel(
                    points,
                    elements,
                    depth,
                    targets,
                    options,
                    explored,
                    progress,
                    cancellationToken);
                exploredAtDepth = parallel.ExploredAtDepth;
                explored += exploredAtDepth;
                depthLimitReached = parallel.LimitReached;
                solutions.AddRange(parallel.Solutions);
            }
            else
            {
                SearchDepth(
                    points,
                    elements,
                    steps,
                    depth,
                    targets,
                    options,
                    solutions,
                    solutionSignatures,
                    visitedStates,
                    ref explored,
                    ref exploredAtDepth,
                    ref depthLimitReached,
                    progress,
                    cancellationToken);
            }

            anyLimitReached |= depthLimitReached;
            if (solutions.Count > 0)
                return new ConstructionSearchReport(
                    solutions,
                    explored,
                    anyLimitReached,
                    depth,
                    !shallowerDepthIncomplete,
                    "exhaustive");
            if (depthLimitReached)
                shallowerDepthIncomplete = true;
        }

        return new ConstructionSearchReport(
            [],
            explored,
            anyLimitReached,
            searchedDepth,
            !shallowerDepthIncomplete,
            "exhaustive");
    }

    private ConstructionSearchReport SearchStrategyFamiliesInParallel(
        IReadOnlyList<ConstructionSearchPoint> initialPoints,
        IReadOnlyList<ConstructionSearchElement> initialElements,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        IProgress<ConstructionSearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var jobs = new List<ParallelStrategyJob>
        {
            new(
                "共线反推",
                options with
                {
                    UseCollinearityClosure = true,
                    UseGuidedSearch = false,
                    UseExhaustiveSearch = false,
                    MaxDegreeOfParallelism = 1
                })
        };
        if (options.MaxDegreeOfParallelism >= 3)
        {
            jobs.Add(new ParallelStrategyJob(
                "启发式",
                options with
                {
                    UseCollinearityClosure = false,
                    UseGuidedSearch = true,
                    UseExhaustiveSearch = false,
                    MaxDegreeOfParallelism = 1
                }));
        }
        var reservedStrategyThreads = jobs.Count;
        jobs.Add(new ParallelStrategyJob(
            "逐层穷举",
            options with
            {
                UseCollinearityClosure = false,
                UseGuidedSearch = false,
                UseExhaustiveSearch = true,
                MaxDegreeOfParallelism = System.Math.Max(
                    1,
                    options.MaxDegreeOfParallelism -
                    reservedStrategyThreads)
            }));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var tasks = jobs.Select(job =>
        {
            IProgress<ConstructionSearchProgress>? workerProgress =
                progress is null
                    ? null
                    : new Progress<ConstructionSearchProgress>(value =>
                        progress.Report(value with
                        {
                            Message = $"{job.Name}｜{value.Message}"
                        }));
            return (
                Job: job,
                Task: Task.Run(
                    () => Search(
                        initialPoints,
                        initialElements,
                        targets,
                        job.Options,
                        workerProgress,
                        linked.Token),
                    linked.Token));
        }).ToList();

        var explored = 0L;
        var stateLimitReached = false;
        var searchedDepth = 0;
        while (tasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completed = Task.WhenAny(
                    tasks.Select(item => item.Task))
                .GetAwaiter()
                .GetResult();
            var item = tasks.First(candidate =>
                ReferenceEquals(candidate.Task, completed));
            tasks.Remove(item);

            ConstructionSearchReport report;
            try
            {
                report = completed.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (
                linked.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            explored += report.ExploredStates;
            stateLimitReached |= report.StateLimitReached;
            searchedDepth = System.Math.Max(
                searchedDepth,
                report.SearchedDepth);
            if (report.Solutions.Count == 0)
                continue;

            linked.Cancel();
            return report with
            {
                ExploredStates = explored,
                OptimalityProven =
                    item.Job.Name == "逐层穷举" &&
                    report.OptimalityProven,
                Strategy =
                    $"parallel:{item.Job.Name}:{report.Strategy}"
            };
        }

        return new ConstructionSearchReport(
            [],
            explored,
            stateLimitReached,
            searchedDepth,
            false,
            "parallel:auto");
    }

    private bool TryDependentTargetReductionSearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        foreach (var primary in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var secondaryTargets = targets
                .Where(target => !string.Equals(
                    target.Name,
                    primary.Name,
                    StringComparison.Ordinal))
                .ToList();
            var candidateAnchors = basePoints
                .Where(anchor =>
                    anchor.Position.DistanceTo(primary.Position) >
                    options.Tolerance)
                .Where(anchor => secondaryTargets.All(secondary =>
                    PointOnAnyElement(
                        secondary.Position,
                        baseElements,
                        options.Tolerance) &&
                    Collinear(
                        primary.Position,
                        anchor.Position,
                        secondary.Position,
                        options.Tolerance)))
                .OrderByDescending(anchor =>
                    anchor.ExistingPointId is { Length: > 0 })
                .ThenBy(anchor => anchor.Key, StringComparer.Ordinal)
                .ToList();
            if (candidateAnchors.Count == 0)
                continue;

            var primaryScenarios = options.ValidationScenarios?
                .Select(scenario =>
                {
                    var scenarioTarget = scenario.Targets.FirstOrDefault(
                        target => string.Equals(
                            target.Name,
                            primary.Name,
                            StringComparison.Ordinal));
                    return scenarioTarget is null
                        ? null
                        : scenario with { Targets = [scenarioTarget] };
                })
                .Where(scenario => scenario is not null)
                .Cast<ConstructionSearchScenario>()
                .ToList();
            if (options.ValidationScenarios is { Count: > 0 } &&
                primaryScenarios?.Count != options.ValidationScenarios.Count)
                continue;

            var primaryOptions = options with
            {
                MinSteps = 0,
                MaxSteps = options.MaxSteps - 1,
                MaxSolutions = 1,
                MaxStates = System.Math.Max(
                    1_000,
                    options.MaxStates / 8),
                ValidationScenarios = primaryScenarios
            };
            var primaryReport = Search(
                basePoints,
                baseElements.Select(element =>
                    new ConstructionSearchElement(
                        element.Key,
                        element.Geometry)).ToList(),
                [primary],
                primaryOptions,
                cancellationToken: cancellationToken);
            explored += primaryReport.ExploredStates;
            if (primaryReport.Solutions.Count == 0)
                continue;

            foreach (var primarySolution in primaryReport.Solutions)
            foreach (var anchor in candidateAnchors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = new ConstructionStep(
                    $"step:{primarySolution.StepCount}",
                    ConstructionToolKind.StraightLine,
                    primarySolution.TargetPoint,
                    anchor,
                    $"{primary.Name}{anchor.ExistingPointId ?? anchor.Key}");
                var provisional = primarySolution with
                {
                    TargetName = JointTargetName(targets),
                    Steps = [.. primarySolution.Steps, step],
                    Targets = null
                };
                var replayed = ReplaySolution(
                    provisional,
                    new ConstructionSearchScenario(
                        basePoints,
                        baseElements.Select(element =>
                            new ConstructionSearchElement(
                                element.Key,
                                element.Geometry)).ToList(),
                        targets),
                    options with { ValidationScenarios = null });
                if (replayed is null ||
                    replayed.StepCount > options.MaxSteps ||
                    !SolutionMatchesValidationScenarios(replayed, options))
                    continue;
                solution = replayed;
                return true;
            }
        }
        return false;
    }

    private static bool PointOnAnyElement(
        Point2 point,
        IReadOnlyList<SearchElement> elements,
        double tolerance) =>
        elements.Any(element =>
            ContainsTarget(element.Geometry, point, tolerance));

    private static bool Collinear(
        Point2 a,
        Point2 b,
        Point2 c,
        double tolerance)
    {
        var ab = b - a;
        var ac = c - a;
        var scale = System.Math.Max(
            1,
            ab.Length * ac.Length);
        return System.Math.Abs(Vector2.Cross(ab, ac)) <=
               tolerance * scale;
    }

    private static bool TryParameterizedTwoLevelCollinearitySearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        if (options.ValidationScenarios is not { Count: > 0 } scenarios)
            return false;
        if (!baseElements.Any(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance)))
            return false;

        // A genuinely moving construction has to acquire parameter dependence
        // somewhere. Starting from visible named points whose positions change
        // across the validation frames removes the enormous grid-grid prefix
        // without excluding the natural first stroke of a moving-diagram proof.
        var varyingNamedPoints = basePoints
            .Where(point => point.ExistingPointId is { Length: > 0 })
            .Where(point => scenarios.Any(scenario =>
            {
                var replayed = scenario.InitialPoints.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.ExistingPointId,
                        point.ExistingPointId,
                        StringComparison.Ordinal));
                return replayed is null ||
                       replayed.Position.DistanceTo(point.Position) >
                       options.Tolerance * 10;
            }))
            .ToList();
        if (varyingNamedPoints.Count == 0) return false;

        var firstStrokeCandidates = new List<SearchElement>();
        foreach (var movingPoint in varyingNamedPoints)
        foreach (var otherPoint in basePoints)
        {
            if (movingPoint.Position.DistanceTo(otherPoint.Position) <= options.Tolerance)
                continue;
            var candidate = LineCandidate(0, movingPoint, otherPoint);
            if (baseElements.Any(element => element.Signature == candidate.Signature))
                continue;
            firstStrokeCandidates.Add(candidate);
        }

        foreach (var firstStroke in firstStrokeCandidates
                     .DistinctBy(candidate => candidate.Signature)
                     .OrderByDescending(candidate =>
                         CandidateNaturalness(
                             candidate,
                             [target],
                             baseElements,
                             options.Tolerance))
                     .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (explored >= options.MaxStates) return false;
            explored++;

            var firstPoints = basePoints.ToList();
            var firstElements = baseElements.ToList();
            var firstSteps = new List<ConstructionStep>();
            var originalPointCount = firstPoints.Count;
            if (!TryApplyLine(
                    firstStroke.Step!.PointA,
                    firstStroke.Step.PointB,
                    firstPoints,
                    firstElements,
                    firstSteps,
                    options))
                continue;
            var firstStrokePoints = firstPoints
                .Skip(originalPointCount)
                .Where(point =>
                    string.Equals(point.ParentElementA, "step:0", StringComparison.Ordinal) ||
                    string.Equals(point.ParentElementB, "step:0", StringComparison.Ordinal))
                .ToList();
            if (firstStrokePoints.Count < 2) continue;

            var dependentLines = new List<SearchElement>();
            foreach (var newPoint in firstStrokePoints)
            foreach (var basePoint in basePoints)
            {
                if (newPoint.Position.DistanceTo(basePoint.Position) <= options.Tolerance)
                    continue;
                var candidate = LineCandidate(1, newPoint, basePoint);
                if (firstElements.Any(element => element.Signature == candidate.Signature))
                    continue;
                dependentLines.Add(candidate);
            }
            dependentLines = dependentLines
                .DistinctBy(candidate => candidate.Signature)
                .OrderByDescending(candidate =>
                    CandidateNaturalness(
                        candidate,
                        [target],
                        firstElements,
                        options.Tolerance))
                .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal)
                .ToList();

            foreach (var anchor in basePoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (anchor.Position.DistanceTo(target.Position) <= options.Tolerance)
                    continue;
                var revealLine = GeometryResultForLine(
                    "parameterized-reveal",
                    anchor.Position,
                    target.Position - anchor.Position);
                var witnessGroups = new Dictionary<string, List<SearchElement>>(
                    StringComparer.Ordinal);

                foreach (var dependentLine in dependentLines)
                {
                    var intersections = GeometryAlgorithms
                        .Intersections(dependentLine.Geometry, revealLine)
                        .ToList();
                    if (intersections.Count != 1) continue;
                    var witness = intersections[0];
                    if (!IncludesPoint(options, witness) ||
                        witness.DistanceTo(anchor.Position) <= options.Tolerance ||
                        witness.DistanceTo(target.Position) <= options.Tolerance)
                        continue;
                    var witnessKey = WitnessPointKey(witness);
                    if (!witnessGroups.TryGetValue(witnessKey, out var group))
                        witnessGroups[witnessKey] = group = [];

                    foreach (var earlierLine in group.Take(6))
                    {
                        if (earlierLine.Signature == dependentLine.Signature)
                            continue;
                        if (explored >= options.MaxStates) return false;
                        explored++;

                        var points = basePoints.ToList();
                        var elements = baseElements.ToList();
                        var steps = new List<ConstructionStep>();
                        if (!TryApplyLine(
                                firstStroke.Step.PointA,
                                firstStroke.Step.PointB,
                                points,
                                elements,
                                steps,
                                options) ||
                            !TryApplyLine(
                                earlierLine.Step!.PointA,
                                earlierLine.Step.PointB,
                                points,
                                elements,
                                steps,
                                options) ||
                            !TryApplyLine(
                                dependentLine.Step!.PointA,
                                dependentLine.Step.PointB,
                                points,
                                elements,
                                steps,
                                options))
                            continue;
                        var exactWitness = GeometryAlgorithms
                            .Intersections(elements[^2].Geometry, elements[^1].Geometry)
                            .FirstOrDefault(point =>
                                point.DistanceTo(witness) <= options.Tolerance * 10);
                        if (exactWitness == default ||
                            FindPoint(points, exactWitness, options.Tolerance) is not
                                { } witnessPoint ||
                            !TryApplyLine(
                                anchor,
                                witnessPoint,
                                points,
                                elements,
                                steps,
                                options))
                            continue;

                        var resolved = ResolveTargets(points, [target], options.Tolerance);
                        if (resolved.Count != 1) continue;
                        var candidateSolution = new ConstructionSolution(
                            target.Name,
                            target.Position,
                            steps,
                            resolved[0].Point,
                            explored,
                            resolved);
                        if (!SolutionMatchesValidationScenarios(candidateSolution, options))
                            continue;
                        solution = candidateSolution;
                        return true;
                    }
                    if (group.Count < 8)
                        group.Add(dependentLine);
                }
            }
        }
        return false;
    }

    private static bool TryParameterizedGridArmMeetInMiddleSearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        if (options.ValidationScenarios is not { Count: > 0 } scenarios ||
            !baseElements.Any(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance)))
            return false;

        var varyingPoints = basePoints
            .Where(point => scenarios.Any(scenario =>
            {
                var replayedElements = scenario.InitialElements
                    .Where(item => item.Geometry.IsValid)
                    .Select(item => new SearchElement(
                        item.Key,
                        ElementSignature(item.Geometry, preserveRange: true),
                        item.Geometry,
                        null))
                    .ToList();
                var replayed = ResolvePointReference(
                    point,
                    scenario.InitialPoints,
                    replayedElements,
                    options.Tolerance);
                return replayed is null ||
                       replayed.Position.DistanceTo(point.Position) >
                       options.Tolerance * 10;
            }))
            .ToHashSet();
        if (varyingPoints.Count == 0) return false;

        var prefixCandidates = GenerateCandidates(
                basePoints,
                0,
                options,
                [target])
            .Where(candidate =>
                candidate.Step!.Tool == ConstructionToolKind.StraightLine &&
                baseElements.All(existing =>
                    existing.Signature != candidate.Signature) &&
                (varyingPoints.Contains(candidate.Step.PointA) ||
                 varyingPoints.Contains(candidate.Step.PointB)))
            .OrderByDescending(candidate =>
                CandidateNaturalness(
                    candidate,
                    [target],
                    baseElements,
                    options.Tolerance))
            .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal)
            .ToList();
        var armAnchors = basePoints
            .OrderByDescending(point =>
                PointNaturalness(point, [target], baseElements, options.Tolerance))
            .ThenBy(point => point.Key, StringComparer.Ordinal)
            .ToList();
        var secondLevelSupports = baseElements
            .Where(element =>
                element.Geometry.Line is not null ||
                element.Geometry.Circle is not null ||
                element.Geometry.Arc is not null)
            .ToList();
        if (prefixCandidates.Count == 0 ||
            armAnchors.Count == 0 ||
            secondLevelSupports.Count < 2)
            return false;

        var branchesByCarrier =
            new Dictionary<TargetCarrierKey, List<ParameterizedGridArmBranch>>();
        var targetCarrierSignatures = baseElements
            .Where(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance))
            .Select(element => ElementSignature(element.Geometry, preserveRange: false))
            .ToHashSet(StringComparer.Ordinal);
        var processedBranches = 0L;

        foreach (var prefix in prefixCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var support in baseElements)
            {
                var intersections = GeometryAlgorithms
                    .Intersections(prefix.Geometry, support.Geometry)
                    .ToList();
                for (var intersectionIndex = 0;
                     intersectionIndex < intersections.Count;
                     intersectionIndex++)
                {
                    var prefixIntersection = intersections[intersectionIndex];
                    if (!IncludesPoint(options, prefixIntersection) ||
                        FindPoint(
                            basePoints,
                            prefixIntersection,
                            options.Tolerance) is not null)
                        continue;
                    var selector = intersectionIndex == 0 ? "first" : "second";
                    var generatedPoint = new ConstructionSearchPoint(
                        $"grid-arm-prefix:{PointKey(prefixIntersection)}",
                        prefixIntersection,
                        ParentElementA: "step:0",
                        ParentElementB: support.Key,
                        Selector: selector);

                    foreach (var anchor in armAnchors)
                    {
                        if (anchor.Position.DistanceTo(prefixIntersection) <=
                            options.Tolerance)
                            continue;
                        var arm = LineCandidate(1, generatedPoint, anchor);
                        if (arm.Signature == prefix.Signature ||
                            baseElements.Any(element =>
                                element.Signature == arm.Signature))
                            continue;

                        foreach (var secondLevelSupport in secondLevelSupports)
                        {
                            var secondLevelIntersections = GeometryAlgorithms
                                .Intersections(
                                    arm.Geometry,
                                    secondLevelSupport.Geometry)
                                .ToList();
                            for (var secondLevelIndex = 0;
                                 secondLevelIndex <
                                 secondLevelIntersections.Count;
                                 secondLevelIndex++)
                            {
                            var gridIntersection =
                                secondLevelIntersections[secondLevelIndex];
                            if (!IncludesPoint(options, gridIntersection) ||
                                gridIntersection.DistanceTo(target.Position) <=
                                options.Tolerance ||
                                FindPoint(
                                    basePoints,
                                    gridIntersection,
                                    options.Tolerance) is not null)
                                continue;

                            var carrier = GeometryResultForLine(
                                "grid-arm-target-carrier",
                                target.Position,
                                gridIntersection - target.Position);
                            var carrierSignature =
                                ElementSignature(carrier, preserveRange: false);
                            if (targetCarrierSignatures.Contains(carrierSignature))
                                continue;
                            if (!TryTargetCarrierKey(
                                    target.Position,
                                    gridIntersection,
                                    out var carrierKey))
                                continue;

                            processedBranches++;
                            var branch = new ParameterizedGridArmBranch(
                                prefix.Step!.PointA,
                                prefix.Step.PointB,
                                support.Key,
                                selector,
                                prefixIntersection,
                                anchor,
                                secondLevelSupport.Key,
                                gridIntersection);
                            if (!branchesByCarrier.TryGetValue(
                                    carrierKey,
                                    out var earlierBranches))
                            {
                                branchesByCarrier[carrierKey] = [branch];
                                continue;
                            }
                            foreach (var earlier in earlierBranches)
                            {
                                if (earlier.GridIntersection.DistanceTo(
                                        branch.GridIntersection) <= options.Tolerance ||
                                    string.Equals(
                                        earlier.GridElementKey,
                                        branch.GridElementKey,
                                        StringComparison.Ordinal))
                                    continue;
                                if (!ContainsTarget(
                                        GeometryResultForLine(
                                            "grid-arm-final",
                                            earlier.GridIntersection,
                                            branch.GridIntersection -
                                            earlier.GridIntersection),
                                        target.Position,
                                        options.Tolerance * 10))
                                    continue;

                                explored++;
                                if (explored > options.MaxStates)
                                    return false;
                                if (!TryBuildParameterizedGridArmSolution(
                                        earlier,
                                        branch,
                                        basePoints,
                                        baseElements,
                                        target,
                                        options,
                                        explored,
                                        out var candidateSolution))
                                    continue;
                                if (!SolutionMatchesValidationScenarios(
                                        candidateSolution!,
                                        options))
                                    continue;
                                solution = candidateSolution;
                                return true;
                            }
                            if (earlierBranches.Count < 32 &&
                                !earlierBranches.Any(earlier =>
                                    string.Equals(
                                        earlier.GridElementKey,
                                        branch.GridElementKey,
                                        StringComparison.Ordinal) &&
                                    earlier.GridIntersection.DistanceTo(
                                        branch.GridIntersection) <=
                                    options.Tolerance &&
                                    earlier.PrefixIntersection.DistanceTo(
                                        branch.PrefixIntersection) <=
                                    options.Tolerance &&
                                    earlier.ArmAnchor.Position.DistanceTo(
                                        branch.ArmAnchor.Position) <=
                                    options.Tolerance))
                                earlierBranches.Add(branch);
                            }
                        }
                    }
                }
            }
        }
        explored += processedBranches / 10_000;
        return false;
    }

    private static bool TryParameterizedCircleLiftedGridArmSearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        if (options.ValidationScenarios is not { Count: > 0 } scenarios)
            return false;
        var circles = baseElements
            .Where(element =>
                element.Geometry.Circle is not null ||
                element.Geometry.Arc is not null)
            .ToList();
        var anchors = basePoints
            .Where(point => point.ExistingPointId is { Length: > 0 })
            .OrderByDescending(point =>
                PointNaturalness(point, [target], baseElements, options.Tolerance))
            .ToList();

        foreach (var circle in circles)
        foreach (var anchor in anchors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ContainsTarget(
                    circle.Geometry,
                    anchor.Position,
                    options.Tolerance))
                continue;
            var carrier = GeometryResultForLine(
                "circle-lifted-carrier",
                anchor.Position,
                target.Position - anchor.Position);
            var baselineIntersections = GeometryAlgorithms
                .Intersections(carrier, circle.Geometry)
                .Where(point =>
                    point.DistanceTo(anchor.Position) >
                    options.Tolerance * 10)
                .ToList();
            if (baselineIntersections.Count != 1 ||
                !IncludesPoint(options, baselineIntersections[0]) ||
                FindPoint(
                    basePoints,
                    baselineIntersections[0],
                    options.Tolerance) is not null)
                continue;
            var liftedTarget = new ConstructionSearchTarget(
                $"{target.Name}:circle-lifted",
                baselineIntersections[0]);
            var liftedScenarios = new List<ConstructionSearchScenario>();
            var validAcrossScenarios = true;
            foreach (var scenario in scenarios)
            {
                var replayedAnchor = scenario.InitialPoints.FirstOrDefault(point =>
                    string.Equals(
                        point.ExistingPointId,
                        anchor.ExistingPointId,
                        StringComparison.Ordinal));
                var replayedCircle = scenario.InitialElements.FirstOrDefault(element =>
                    string.Equals(
                        element.Key,
                        circle.Key,
                        StringComparison.Ordinal));
                var scenarioTarget = scenario.Targets.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Name,
                        target.Name,
                        StringComparison.Ordinal));
                if (replayedAnchor is null ||
                    replayedCircle is null ||
                    scenarioTarget is null)
                {
                    validAcrossScenarios = false;
                    break;
                }
                var scenarioCarrier = GeometryResultForLine(
                    "circle-lifted-carrier",
                    replayedAnchor.Position,
                    scenarioTarget.Position - replayedAnchor.Position);
                var liftedIntersections = GeometryAlgorithms
                    .Intersections(
                        scenarioCarrier,
                        replayedCircle.Geometry)
                    .Where(point =>
                        point.DistanceTo(replayedAnchor.Position) >
                        options.Tolerance * 10)
                    .ToList();
                if (liftedIntersections.Count != 1)
                {
                    validAcrossScenarios = false;
                    break;
                }
                liftedScenarios.Add(scenario with
                {
                    Targets =
                    [
                        new ConstructionSearchTarget(
                            liftedTarget.Name,
                            liftedIntersections[0])
                    ]
                });
            }
            if (!validAcrossScenarios) continue;

            var liftedOptions = options with
            {
                MaxSteps = 5,
                ValidationScenarios = liftedScenarios
            };
            var liftedExplored = 0L;
            if (!TryParameterizedMixedCostTwoSearch(
                    basePoints,
                    baseElements,
                    liftedTarget,
                    liftedOptions,
                    ref liftedExplored,
                    cancellationToken,
                    out var liftedSolution) &&
                !TryParameterizedGridArmMeetInMiddleSearch(
                    basePoints,
                    baseElements,
                    liftedTarget,
                    liftedOptions,
                    ref liftedExplored,
                    cancellationToken,
                    out liftedSolution))
            {
                explored += liftedExplored;
                continue;
            }
            explored += liftedExplored;

            var finalStep = LineCandidate(
                liftedSolution!.Steps.Count,
                anchor,
                liftedSolution.TargetPoint).Step!;
            var provisional = new ConstructionSolution(
                target.Name,
                target.Position,
                liftedSolution.Steps.Append(finalStep).ToList(),
                new ConstructionSearchPoint(
                    $"target:{target.Name}",
                    target.Position),
                explored);
            var baselineScenario = new ConstructionSearchScenario(
                basePoints,
                baseElements.Select(element =>
                    new ConstructionSearchElement(
                        element.Key,
                        element.Geometry)).ToList(),
                [target],
                "baseline");
            var replayed = ReplaySolution(
                provisional,
                baselineScenario,
                options);
            if (replayed is null ||
                !SolutionMatchesValidationScenarios(replayed, options))
                continue;
            solution = replayed;
            return true;
        }
        return false;
    }

    private static bool TryParameterizedMixedCostTwoSearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        var oneStrokeLines = GenerateCandidates(
                basePoints,
                0,
                options,
                [target])
            .Where(candidate =>
                candidate.Step!.Tool == ConstructionToolKind.StraightLine &&
                baseElements.All(existing =>
                    existing.Signature != candidate.Signature))
            .DistinctBy(candidate => candidate.Signature)
            .OrderByDescending(candidate =>
                CandidateNaturalness(
                    candidate,
                    [target],
                    baseElements,
                    options.Tolerance))
            .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal)
            .ToList();
        var targetCarrierSignatures = baseElements
            .Where(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance))
            .Select(element => ElementSignature(element.Geometry, preserveRange: false))
            .ToHashSet(StringComparer.Ordinal);
        var twoPrefixByCarrier =
            new Dictionary<TargetCarrierKey, List<ParameterizedTwoPrefixBranch>>();

        for (var firstIndex = 0;
             firstIndex < oneStrokeLines.Count - 1;
             firstIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var secondIndex = firstIndex + 1;
                 secondIndex < oneStrokeLines.Count;
                 secondIndex++)
            {
                var first = oneStrokeLines[firstIndex];
                var second = oneStrokeLines[secondIndex];
                var intersections = GeometryAlgorithms
                    .Intersections(first.Geometry, second.Geometry);
                if (intersections.Count != 1) continue;
                var point = intersections[0];
                if (!IncludesPoint(options, point) ||
                    FindPoint(basePoints, point, options.Tolerance) is not null ||
                    point.DistanceTo(target.Position) <= options.Tolerance)
                    continue;
                var carrier = GeometryResultForLine(
                    "mixed-two-prefix-carrier",
                    target.Position,
                    point - target.Position);
                if (targetCarrierSignatures.Contains(
                        ElementSignature(carrier, preserveRange: false)) ||
                    !TryTargetCarrierKey(
                        target.Position,
                        point,
                        out var key))
                    continue;
                var branch = new ParameterizedTwoPrefixBranch(
                    first.Step!.PointA,
                    first.Step.PointB,
                    second.Step!.PointA,
                    second.Step.PointB,
                    point);
                if (!twoPrefixByCarrier.TryGetValue(key, out var group))
                    twoPrefixByCarrier[key] = [branch];
                else if (group.Count < 12)
                    group.Add(branch);
            }
        }
        if (twoPrefixByCarrier.Count == 0)
            return false;

        var varyingPoints = basePoints
            .Where(point => options.ValidationScenarios!.Any(scenario =>
            {
                var replayedElements = scenario.InitialElements
                    .Where(item => item.Geometry.IsValid)
                    .Select(item => new SearchElement(
                        item.Key,
                        ElementSignature(item.Geometry, preserveRange: true),
                        item.Geometry,
                        null))
                    .ToList();
                var replayed = ResolvePointReference(
                    point,
                    scenario.InitialPoints,
                    replayedElements,
                    options.Tolerance);
                return replayed is null ||
                       replayed.Position.DistanceTo(point.Position) >
                       options.Tolerance * 10;
            }))
            .ToHashSet();
        var dependentPrefixes = oneStrokeLines
            .Where(line =>
                varyingPoints.Contains(line.Step!.PointA) ||
                varyingPoints.Contains(line.Step.PointB))
            .ToList();
        var armAnchors = basePoints
            .OrderByDescending(point =>
                PointNaturalness(point, [target], baseElements, options.Tolerance))
            .ThenBy(point => point.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var prefix in dependentPrefixes)
        foreach (var prefixSupport in baseElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefixIntersections = GeometryAlgorithms
                .Intersections(prefix.Geometry, prefixSupport.Geometry)
                .ToList();
            for (var prefixIntersectionIndex = 0;
                 prefixIntersectionIndex < prefixIntersections.Count;
                 prefixIntersectionIndex++)
            {
                var prefixIntersection =
                    prefixIntersections[prefixIntersectionIndex];
                if (!IncludesPoint(options, prefixIntersection) ||
                    FindPoint(
                        basePoints,
                        prefixIntersection,
                        options.Tolerance) is not null)
                    continue;
                var generated = new ConstructionSearchPoint(
                    $"mixed-prefix:{PointKey(prefixIntersection)}",
                    prefixIntersection,
                    ParentElementA: "step:2",
                    ParentElementB: prefixSupport.Key,
                    Selector: prefixIntersectionIndex == 0
                        ? "first"
                        : "second");
                foreach (var anchor in armAnchors)
                {
                    if (anchor.Position.DistanceTo(prefixIntersection) <=
                        options.Tolerance)
                        continue;
                    var arm = LineCandidate(3, generated, anchor);
                    if (baseElements.Any(element =>
                            element.Signature == arm.Signature) ||
                        arm.Signature == prefix.Signature)
                        continue;
                    foreach (var support in baseElements)
                    {
                        var supportedPoints = GeometryAlgorithms
                            .Intersections(arm.Geometry, support.Geometry)
                            .ToList();
                        foreach (var supportedPoint in supportedPoints)
                        {
                            if (!IncludesPoint(options, supportedPoint) ||
                                FindPoint(
                                    basePoints,
                                    supportedPoint,
                                    options.Tolerance) is not null ||
                                !TryTargetCarrierKey(
                                    target.Position,
                                    supportedPoint,
                                    out var carrierKey) ||
                                !twoPrefixByCarrier.TryGetValue(
                                    carrierKey,
                                    out var twoPrefixBranches))
                                continue;

                            foreach (var twoPrefix in twoPrefixBranches)
                            {
                                if (twoPrefix.Intersection.DistanceTo(
                                        supportedPoint) <= options.Tolerance)
                                    continue;
                                var reveal = GeometryResultForLine(
                                    "mixed-final-reveal",
                                    twoPrefix.Intersection,
                                    supportedPoint - twoPrefix.Intersection);
                                if (!ContainsTarget(
                                        reveal,
                                        target.Position,
                                        options.Tolerance * 10) ||
                                    targetCarrierSignatures.Contains(
                                        ElementSignature(
                                            reveal,
                                            preserveRange: false)))
                                    continue;
                                explored++;
                                if (explored > options.MaxStates)
                                    return false;
                                if (!TryBuildParameterizedMixedCostTwoSolution(
                                        twoPrefix,
                                        prefix,
                                        prefixSupport,
                                        prefixIntersection,
                                        anchor,
                                        support,
                                        supportedPoint,
                                        basePoints,
                                        baseElements,
                                        target,
                                        options,
                                        explored,
                                        out var candidate) ||
                                    !SolutionMatchesValidationScenarios(
                                        candidate!,
                                        options))
                                    continue;
                                solution = candidate;
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool TryBuildParameterizedMixedCostTwoSolution(
        ParameterizedTwoPrefixBranch twoPrefix,
        SearchElement dependentPrefix,
        SearchElement dependentPrefixSupport,
        Point2 dependentPrefixIntersection,
        ConstructionSearchPoint armAnchor,
        SearchElement secondLevelSupport,
        Point2 supportedPoint,
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        long explored,
        out ConstructionSolution? solution)
    {
        solution = null;
        var points = basePoints.ToList();
        var elements = baseElements.ToList();
        var steps = new List<ConstructionStep>();
        if (!TryApplyLine(
                twoPrefix.FirstLinePointA,
                twoPrefix.FirstLinePointB,
                points,
                elements,
                steps,
                options) ||
            !TryApplyLine(
                twoPrefix.SecondLinePointA,
                twoPrefix.SecondLinePointB,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, twoPrefix.Intersection, options.Tolerance * 10)
                is not { } twoPrefixIntersection ||
            !TryApplyLine(
                dependentPrefix.Step!.PointA,
                dependentPrefix.Step.PointB,
                points,
                elements,
                steps,
                options) ||
            FindPoint(
                points,
                dependentPrefixIntersection,
                options.Tolerance * 10) is not { } generated ||
            !TryApplyLine(
                generated,
                armAnchor,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, supportedPoint, options.Tolerance * 10)
                is not { } supported ||
            !TryApplyLine(
                twoPrefixIntersection,
                supported,
                points,
                elements,
                steps,
                options))
            return false;
        if (steps.Count > options.MaxSteps)
            return false;
        var resolved = ResolveTargets(points, [target], options.Tolerance * 10);
        if (resolved.Count != 1)
            return false;
        solution = new ConstructionSolution(
            target.Name,
            target.Position,
            steps,
            resolved[0].Point,
            explored,
            resolved);
        return true;
    }

    private static bool TryBuildParameterizedGridArmSolution(
        ParameterizedGridArmBranch first,
        ParameterizedGridArmBranch second,
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        long explored,
        out ConstructionSolution? solution)
    {
        solution = null;
        var points = basePoints.ToList();
        var elements = baseElements.ToList();
        var steps = new List<ConstructionStep>();

        if (!TryApplyLine(
                first.PrefixPointA,
                first.PrefixPointB,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, first.PrefixIntersection, options.Tolerance * 10)
                is not { } firstPrefixIntersection ||
            !TryApplyLine(
                firstPrefixIntersection,
                first.ArmAnchor,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, first.GridIntersection, options.Tolerance * 10)
                is not { } firstGridIntersection ||
            !TryApplyLine(
                second.PrefixPointA,
                second.PrefixPointB,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, second.PrefixIntersection, options.Tolerance * 10)
                is not { } secondPrefixIntersection ||
            !TryApplyLine(
                secondPrefixIntersection,
                second.ArmAnchor,
                points,
                elements,
                steps,
                options) ||
            FindPoint(points, second.GridIntersection, options.Tolerance * 10)
                is not { } secondGridIntersection ||
            !TryApplyLine(
                firstGridIntersection,
                secondGridIntersection,
                points,
                elements,
                steps,
                options))
            return false;

        if (steps.Count > options.MaxSteps)
            return false;
        var resolved = ResolveTargets(points, [target], options.Tolerance * 10);
        if (resolved.Count != 1)
            return false;
        solution = new ConstructionSolution(
            target.Name,
            target.Position,
            steps,
            resolved[0].Point,
            explored,
            resolved);
        return true;
    }

    private static bool TryTargetCarrierKey(
        Point2 target,
        Point2 point,
        out TargetCarrierKey key)
    {
        var direction = (point - target).Normalized;
        if (direction.Length <= GeometryAlgorithms.Epsilon)
        {
            key = default;
            return false;
        }
        var a = -direction.Y;
        var b = direction.X;
        var c = a * target.X + b * target.Y;
        if (a < -1e-12 ||
            (System.Math.Abs(a) <= 1e-12 && b < 0))
        {
            a = -a;
            b = -b;
            c = -c;
        }
        const double scale = 10_000_000d;
        key = new TargetCarrierKey(
            (long)System.Math.Round(a * scale),
            (long)System.Math.Round(b * scale),
            (long)System.Math.Round(c * scale));
        return true;
    }

    private static bool TrySingleTargetDoubleCollinearitySearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        ConstructionSearchTarget target,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        var drawableLines = GenerateCandidates(basePoints, 0, options, [target])
            .Where(candidate =>
                candidate.Step!.Tool == ConstructionToolKind.StraightLine &&
                baseElements.All(existing => existing.Signature != candidate.Signature))
            .ToList();
        var plans = new List<SingleTargetCarrierPlan>();
        var planKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var anchor in basePoints
                     .Where(point =>
                         point.Position.DistanceTo(target.Position) > options.Tolerance)
                     .OrderByDescending(point =>
                         PointNaturalness(point, [target], baseElements, options.Tolerance)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var carrier = GeometryResultForLine(
                "single-target-carrier",
                anchor.Position,
                target.Position - anchor.Position);
            var carrierSignature = ElementSignature(carrier, preserveRange: false);
            var groups = new Dictionary<string, (Point2 Witness, List<SearchElement> Lines)>(
                StringComparer.Ordinal);
            foreach (var candidate in drawableLines)
            {
                var intersections = GeometryAlgorithms
                    .Intersections(candidate.Geometry, carrier)
                    .ToList();
                if (intersections.Count == 0) continue;
                var witness = intersections[0];
                if (!IncludesPoint(options, witness) ||
                    witness.DistanceTo(anchor.Position) <= options.Tolerance ||
                    witness.DistanceTo(target.Position) <= options.Tolerance)
                    continue;
                var key = WitnessPointKey(witness);
                if (!groups.TryGetValue(key, out var group))
                    group = (witness, []);
                group.Lines.Add(candidate);
                groups[key] = group;
            }

            foreach (var group in groups.Values.Where(group => group.Lines.Count >= 2))
            {
                var ranked = group.Lines
                    .OrderByDescending(candidate =>
                        CandidateNaturalness(candidate, [target], baseElements, options.Tolerance))
                    .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal)
                    .Take(8)
                    .ToList();
                for (var firstIndex = 0; firstIndex < ranked.Count - 1; firstIndex++)
                for (var secondIndex = firstIndex + 1; secondIndex < ranked.Count; secondIndex++)
                {
                    var first = ranked[firstIndex];
                    var second = ranked[secondIndex];
                    var key =
                        $"{carrierSignature}|{first.Signature}|{second.Signature}";
                    if (!planKeys.Add(key)) continue;
                    plans.Add(new SingleTargetCarrierPlan(
                        anchor,
                        first,
                        second,
                        group.Witness,
                        carrierSignature,
                        CandidateNaturalness(first, [target], baseElements, options.Tolerance) +
                        CandidateNaturalness(second, [target], baseElements, options.Tolerance) +
                        PointNaturalness(anchor, [target], baseElements, options.Tolerance)));
                }
            }
        }

        var rankedPlans = plans
            .OrderByDescending(plan => plan.Naturalness)
            .ThenBy(plan => plan.Anchor.Position.DistanceTo(target.Position))
            .ThenBy(plan => plan.CarrierSignature, StringComparer.Ordinal)
            .Take(512)
            .ToList();

        if (baseElements.Any(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance)))
        {
            foreach (var plan in rankedPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (explored >= options.MaxStates) return false;
                explored++;
                var points = basePoints.ToList();
                var elements = baseElements.ToList();
                var steps = new List<ConstructionStep>();
                if (!TryApplyCarrierPlan(plan, points, elements, steps, options) ||
                    steps.Count > options.MaxSteps)
                    continue;
                var resolved = ResolveTargets(points, [target], options.Tolerance);
                if (resolved.Count != 1) continue;
                var candidateSolution = new ConstructionSolution(
                    target.Name,
                    target.Position,
                    steps,
                    resolved[0].Point,
                    explored,
                    resolved);
                if (!SolutionMatchesValidationScenarios(candidateSolution, options))
                    continue;
                solution = candidateSolution;
                return true;
            }
        }

        if (options.MaxSteps < 6) return false;
        for (var firstIndex = 0; firstIndex < rankedPlans.Count - 1; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < rankedPlans.Count; secondIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (explored >= options.MaxStates) return false;
                var firstPlan = rankedPlans[firstIndex];
                var secondPlan = rankedPlans[secondIndex];
                if (firstPlan.CarrierSignature == secondPlan.CarrierSignature) continue;
                explored++;

                var points = basePoints.ToList();
                var elements = baseElements.ToList();
                var steps = new List<ConstructionStep>();
                if (!TryApplyCarrierPlan(
                        firstPlan,
                        points,
                        elements,
                        steps,
                        options) ||
                    steps.Count > options.MaxSteps)
                    continue;
                if (!TryApplyCarrierPlan(
                        secondPlan,
                        points,
                        elements,
                        steps,
                        options) ||
                    steps.Count > options.MaxSteps)
                    continue;

                var resolved = ResolveTargets(points, [target], options.Tolerance);
                if (resolved.Count != 1) continue;
                var candidateSolution = new ConstructionSolution(
                    target.Name,
                    target.Position,
                    steps,
                    resolved[0].Point,
                    explored,
                    resolved);
                if (!SolutionMatchesValidationScenarios(candidateSolution, options))
                    continue;
                solution = candidateSolution;
                return true;
            }
        }
        return false;
    }

    private static bool TryApplyCarrierPlan(
        SingleTargetCarrierPlan plan,
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        ConstructionSearchOptions options)
    {
        if (!TryApplyOrReuseLine(
                plan.FirstTemplate.Step!.PointA,
                plan.FirstTemplate.Step.PointB,
                points,
                elements,
                steps,
                options) ||
            !TryApplyOrReuseLine(
                plan.SecondTemplate.Step!.PointA,
                plan.SecondTemplate.Step.PointB,
                points,
                elements,
                steps,
                options))
            return false;
        if (FindPoint(points, plan.Witness, options.Tolerance) is not { } witness)
            return false;
        return TryApplyOrReuseLine(
            plan.Anchor,
            witness,
            points,
            elements,
            steps,
            options);
    }

    private static bool TryApplyOrReuseLine(
        ConstructionSearchPoint pointA,
        ConstructionSearchPoint pointB,
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        ConstructionSearchOptions options)
    {
        var candidate = LineCandidate(steps.Count, pointA, pointB);
        if (elements.Any(element => element.Signature == candidate.Signature))
            return true;
        return TryApplyLine(pointA, pointB, points, elements, steps, options);
    }

    private static bool TryGuidedSearch(
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        int depthLimit,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        HashSet<string> visitedStates,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        if (steps.Count >= depthLimit) return false;

        cancellationToken.ThrowIfCancellationRequested();
        var remainingSteps = depthLimit - steps.Count;
        var evaluations = new List<GuidedCandidate>();
        var unresolvedBefore = targets
            .Where(target => FindPoint(points, target.Position, options.Tolerance) is null)
            .ToList();
        var candidatePoolQuery = GenerateCandidates(
                points,
                steps.Count,
                options,
                targets,
                accelerateOrder: true,
                cancellationToken: cancellationToken)
            .Where(candidate => elements.All(existing =>
                existing.Signature != candidate.Signature));
        if (options.CandidateAccelerator is not null)
            candidatePoolQuery = candidatePoolQuery.Take(
                options.AcceleratedGuidedPreselectionLimit);
        var candidatePool = candidatePoolQuery
            .Select(candidate => new
            {
                Candidate = candidate,
                ImmediateTargets = unresolvedBefore.Count(target =>
                    ContainsTarget(candidate.Geometry, target.Position, options.Tolerance) &&
                    CreatesTarget(candidate, elements, target.Position, options.Tolerance))
            })
            .ToList();
        if (candidatePool.Count == 0) return false;
        var bestImmediateCount = candidatePool.Max(item => item.ImmediateTargets);
        if (bestImmediateCount > 0)
            candidatePool = candidatePool
                .Where(item => item.ImmediateTargets == bestImmediateCount)
                .ToList();

        foreach (var candidateItem in candidatePool)
        {
            var candidate = candidateItem.Candidate;
            var addedPoints = IntersectionsWithExisting(candidate, elements, points, options);
            if (points.Count + addedPoints.Count > options.MaxPointsPerState) continue;

            var prospectivePoints = points.Concat(addedPoints).ToList();
            var prospectiveElements = elements.Append(candidate).ToList();
            var resolved = ResolveTargets(
                prospectivePoints,
                targets,
                options.Tolerance);
            var canFinishNext = remainingSteps == 2 &&
                                resolved.Count < targets.Count &&
                                CanFinishInOneStroke(
                                    prospectivePoints,
                                    prospectiveElements,
                                    targets,
                                    options,
                                    addedPoints);
            if (remainingSteps == 2 &&
                resolved.Count < targets.Count &&
                !canFinishNext)
                continue;
            var carrierScore = targets.Sum(target =>
                System.Math.Min(
                    2,
                    prospectiveElements.Count(element =>
                        ContainsTarget(
                            element.Geometry,
                            target.Position,
                            options.Tolerance))));
            var distance = targets
                .Where(target => FindPoint(
                    prospectivePoints,
                    target.Position,
                    options.Tolerance) is null)
                .Sum(target => TargetDistance(candidate.Geometry, target.Position));
            evaluations.Add(new GuidedCandidate(
                candidate,
                addedPoints,
                resolved.Count,
                carrierScore,
                canFinishNext,
                CandidateNaturalness(candidate, targets, elements, options.Tolerance),
                distance));
        }

        foreach (var evaluation in evaluations
                     .OrderByDescending(item => item.ResolvedTargets)
                     .ThenByDescending(item => item.CanFinishNext)
                     .ThenByDescending(item => item.CarrierScore)
                     .ThenByDescending(item => item.Naturalness)
                     .ThenBy(item => item.TargetDistance)
                     .ThenBy(item => item.Candidate.Signature, StringComparer.Ordinal)
                     .Take(options.GuidedCandidateLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            explored++;
            var previousPointCount = points.Count;
            points.AddRange(evaluation.AddedPoints);
            elements.Add(evaluation.Candidate);
            steps.Add(evaluation.Candidate.Step!);

            var stateSignature = StateSignature(elements);
            if (visitedStates.Add(stateSignature))
            {
                var resolvedTargets = ResolveTargets(points, targets, options.Tolerance);
                if (resolvedTargets.Count == targets.Count)
                {
                    var first = resolvedTargets[0];
                    var candidateSolution = new ConstructionSolution(
                        JointTargetName(targets),
                        first.Position,
                        steps.ToList(),
                        first.Point,
                        explored,
                        resolvedTargets);
                    if (SolutionMatchesValidationScenarios(candidateSolution, options))
                        solution = candidateSolution;
                }
                else if (steps.Count < depthLimit)
                {
                    TryGuidedSearch(
                        points,
                        elements,
                        steps,
                        depthLimit,
                        targets,
                        options,
                        visitedStates,
                        ref explored,
                        cancellationToken,
                        out solution);
                }
            }

            steps.RemoveAt(steps.Count - 1);
            elements.RemoveAt(elements.Count - 1);
            points.RemoveRange(previousPointCount, points.Count - previousPointCount);
            if (solution is not null) return true;
        }
        return false;
    }

    private static bool TryCollinearityClosureSearch(
        IReadOnlyList<ConstructionSearchPoint> basePoints,
        IReadOnlyList<SearchElement> baseElements,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        ref long explored,
        CancellationToken cancellationToken,
        out ConstructionSolution? solution)
    {
        solution = null;
        var patternBudget = System.Math.Min(options.MaxStates, 256);
        var unresolved = targets
            .Where(target =>
                FindPoint(basePoints, target.Position, options.Tolerance) is null)
            .ToList();
        var drawableLines = GenerateCandidates(basePoints, 0, options, targets)
            .Where(candidate =>
                candidate.Step!.Tool == ConstructionToolKind.StraightLine &&
                baseElements.All(existing =>
                    existing.Signature != candidate.Signature))
            .ToList();

        foreach (var primaryTarget in unresolved
                     .OrderByDescending(target => baseElements.Count(element =>
                         (element.Geometry.Circle is not null ||
                          element.Geometry.Arc is not null) &&
                         ContainsTarget(
                             element.Geometry,
                             target.Position,
                             options.Tolerance))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var carrier in baseElements.Where(element =>
                         ContainsTarget(
                             element.Geometry,
                             primaryTarget.Position,
                             options.Tolerance))
                         .OrderByDescending(element =>
                             element.Geometry.Circle is not null ||
                             element.Geometry.Arc is not null)
                         .Take(3))
            {
                var anchors = basePoints
                    .Where(point =>
                        point.Position.DistanceTo(primaryTarget.Position) >
                            options.Tolerance &&
                        ContainsTarget(
                            carrier.Geometry,
                            point.Position,
                            options.Tolerance))
                    .OrderByDescending(point =>
                        PointNaturalness(
                            point,
                            targets,
                            baseElements,
                            options.Tolerance))
                    .Take(8)
                    .ToList();
                foreach (var anchor in anchors)
                {
                    var revealLine = GeometryResultForLine(
                        "collinearity-reveal",
                        anchor.Position,
                        primaryTarget.Position - anchor.Position);
                    var groups = new Dictionary<string, List<SearchElement>>(
                        StringComparer.Ordinal);
                    foreach (var candidate in drawableLines)
                    {
                        var witness = GeometryAlgorithms
                            .Intersections(candidate.Geometry, revealLine)
                            .FirstOrDefault();
                        if (witness == default ||
                            !IncludesPoint(options, witness) ||
                            witness.DistanceTo(anchor.Position) <= options.Tolerance ||
                            witness.DistanceTo(primaryTarget.Position) <= options.Tolerance)
                            continue;
                        var key = WitnessPointKey(witness);
                        if (!groups.TryGetValue(key, out var group))
                            groups[key] = group = [];
                        group.Add(candidate);
                    }

                    foreach (var group in groups.Values
                                 .Where(group => group.Count >= 2)
                                 .OrderByDescending(group => group.Max(candidate =>
                                     CandidateNaturalness(
                                         candidate,
                                         targets,
                                         baseElements,
                                         options.Tolerance))))
                    {
                        for (var firstIndex = 0;
                             firstIndex < group.Count - 1;
                             firstIndex++)
                        {
                            for (var secondIndex = firstIndex + 1;
                                 secondIndex < group.Count;
                                 secondIndex++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (explored >= patternBudget)
                                    return false;
                                explored++;
                                var firstTemplate = group[firstIndex];
                                var secondTemplate = group[secondIndex];
                                if (firstTemplate.Signature == secondTemplate.Signature)
                                    continue;

                                var points = basePoints.ToList();
                                var elements = baseElements.ToList();
                                var steps = new List<ConstructionStep>();
                                if (!TryApplyLine(
                                        firstTemplate.Step!.PointA,
                                        firstTemplate.Step.PointB,
                                        points,
                                        elements,
                                        steps,
                                        options) ||
                                    !TryApplyLine(
                                        secondTemplate.Step!.PointA,
                                        secondTemplate.Step.PointB,
                                        points,
                                        elements,
                                        steps,
                                        options))
                                    continue;

                                var witness = GeometryAlgorithms
                                    .Intersections(
                                        elements[^2].Geometry,
                                        elements[^1].Geometry)
                                    .FirstOrDefault(point =>
                                        ContainsTarget(
                                            revealLine,
                                            point,
                                            options.Tolerance));
                                if (witness == default ||
                                    FindPoint(
                                        points,
                                        witness,
                                        options.Tolerance) is not { } witnessPoint)
                                    continue;
                                if (!TryApplyLine(
                                        anchor,
                                        witnessPoint,
                                        points,
                                        elements,
                                        steps,
                                        options))
                                    continue;

                                var resolved = ResolveTargets(
                                    points,
                                    targets,
                                    options.Tolerance);
                                if (!resolved.Any(target =>
                                        target.Name == primaryTarget.Name))
                                    continue;

                                while (resolved.Count < targets.Count &&
                                       steps.Count < options.MaxSteps &&
                                       TryApplySharedTargetLine(
                                           points,
                                           elements,
                                           steps,
                                           targets,
                                           options))
                                    resolved = ResolveTargets(
                                        points,
                                        targets,
                                        options.Tolerance);
                                if (resolved.Count != targets.Count)
                                    continue;

                                var first = resolved[0];
                                var candidateSolution = new ConstructionSolution(
                                    JointTargetName(targets),
                                    first.Position,
                                    steps,
                                    first.Point,
                                    explored,
                                    resolved);
                                if (!SolutionMatchesValidationScenarios(candidateSolution, options))
                                    continue;
                                solution = candidateSolution;
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool TryApplySharedTargetLine(
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options)
    {
        var unresolved = targets
            .Where(target =>
                FindPoint(points, target.Position, options.Tolerance) is null)
            .ToList();
        if (unresolved.Count == 0) return false;

        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var candidate = LineCandidate(steps.Count, points[i], points[j]);
                if (elements.Any(element =>
                        element.Signature == candidate.Signature) ||
                    unresolved.Any(target =>
                        !ContainsTarget(
                            candidate.Geometry,
                            target.Position,
                            options.Tolerance)) ||
                    unresolved.Any(target =>
                        !CreatesTarget(
                            candidate,
                            elements,
                            target.Position,
                            options.Tolerance)))
                    continue;
                return TryApplyLine(
                    points[i],
                    points[j],
                    points,
                    elements,
                    steps,
                    options);
            }
        }
        return false;
    }

    private static bool TryApplyLine(
        ConstructionSearchPoint pointA,
        ConstructionSearchPoint pointB,
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        ConstructionSearchOptions options)
    {
        var candidate = LineCandidate(steps.Count, pointA, pointB);
        if (elements.Any(element => element.Signature == candidate.Signature))
            return false;
        var addedPoints = IntersectionsWithExisting(
            candidate,
            elements,
            points,
            options);
        if (points.Count + addedPoints.Count > options.MaxPointsPerState)
            return false;
        points.AddRange(addedPoints);
        elements.Add(candidate);
        steps.Add(candidate.Step!);
        return true;
    }

    public static IReadOnlyList<ConstructionSearchPoint> CompleteKnownIntersections(
        IReadOnlyList<ConstructionSearchPoint> initialPoints,
        IReadOnlyList<ConstructionSearchElement> initialElements,
        double tolerance = 1e-7,
        Func<Point2, bool>? includePoint = null)
    {
        var points = DistinctPoints(initialPoints, tolerance);
        var elements = initialElements
            .Where(element => element.Geometry.IsValid &&
                              (element.Geometry.Line is not null ||
                               element.Geometry.Circle is not null ||
                               element.Geometry.Arc is not null))
            .ToList();

        for (var i = 0; i < elements.Count; i++)
        {
            for (var j = i + 1; j < elements.Count; j++)
            {
                var intersections = GeometryAlgorithms
                    .Intersections(elements[i].Geometry, elements[j].Geometry)
                    .ToList();
                for (var index = 0; index < intersections.Count; index++)
                {
                    var point = intersections[index];
                    if (includePoint is not null && !includePoint(point)) continue;
                    if (FindPoint(points, point, tolerance) is not null) continue;
                    points.Add(new ConstructionSearchPoint(
                        $"known-intersection:{PointKey(point)}",
                        point,
                        ParentElementA: elements[i].Key,
                        ParentElementB: elements[j].Key,
                        Selector: index == 0 ? "first" : "second"));
                }
            }
        }
        return points;
    }

    /// <summary>
    /// Replays a construction by point provenance rather than by the numerical
    /// coordinates of the frame in which it was found. This is what lets a
    /// candidate construction be checked against other positions of a moving
    /// diagram without accidentally accepting a one-frame coincidence.
    /// </summary>
    public static bool ReplaysSolution(
        ConstructionSolution solution,
        ConstructionSearchScenario scenario,
        ConstructionSearchOptions options) =>
        ReplaySolution(solution, scenario, options) is not null;

    /// <summary>
    /// Rebuilds every endpoint and intersection at a different parameter
    /// position and returns the position-adjusted solution used by animation.
    /// </summary>
    public static ConstructionSolution? ReplaySolution(
        ConstructionSolution solution,
        ConstructionSearchScenario scenario,
        ConstructionSearchOptions options)
    {
        var points = DistinctPoints(scenario.InitialPoints, options.Tolerance);
        var elements = scenario.InitialElements
            .Where(item => item.Geometry.IsValid &&
                           (item.Geometry.Line is not null ||
                            item.Geometry.Circle is not null ||
                            item.Geometry.Arc is not null))
            .Select(item => new SearchElement(
                item.Key,
                ElementSignature(item.Geometry, preserveRange: true),
                item.Geometry,
                null))
            .DistinctBy(item => item.Key)
            .ToList();
        var replayedSteps = new List<ConstructionStep>();

        foreach (var sourceStep in solution.Steps)
        {
            var pointA = ResolvePointReference(
                sourceStep.PointA,
                points,
                elements,
                options.Tolerance);
            var pointB = ResolvePointReference(
                sourceStep.PointB,
                points,
                elements,
                options.Tolerance);
            if (pointA is null || pointB is null ||
                pointA.Position.DistanceTo(pointB.Position) <= options.Tolerance)
                return null;

            var replayedStep = sourceStep with
            {
                PointA = pointA,
                PointB = pointB
            };
            SearchElement candidate;
            if (sourceStep.Tool == ConstructionToolKind.StraightLine)
            {
                var geometry = GeometryResultForLine(
                    sourceStep.Key,
                    pointA.Position,
                    pointB.Position - pointA.Position);
                candidate = new SearchElement(
                    sourceStep.Key,
                    ElementSignature(geometry, preserveRange: false),
                    geometry,
                    replayedStep);
            }
            else if (sourceStep.Tool == ConstructionToolKind.Circle)
            {
                var geometry = new GeometryResult
                {
                    ObjectId = sourceStep.Key,
                    Type = "circleByCenterPoint",
                    IsValid = true,
                    Circle = new CircleGeometry(
                        pointA.Position,
                        pointA.Position.DistanceTo(pointB.Position))
                };
                candidate = new SearchElement(
                    sourceStep.Key,
                    ElementSignature(geometry, preserveRange: false),
                    geometry,
                    replayedStep);
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
            var addedPoints = IntersectionsWithExisting(
                candidate,
                elements,
                points,
                options);
            if (points.Count + addedPoints.Count > options.MaxPointsPerState)
                return null;
            points.AddRange(addedPoints);
            elements.Add(candidate);
            replayedSteps.Add(replayedStep);
        }

        var resolvedTargets = ResolveTargets(
            points,
            scenario.Targets,
            options.Tolerance);
        if (resolvedTargets.Count != scenario.Targets.Count)
            return null;
        var first = resolvedTargets[0];
        return new ConstructionSolution(
            JointTargetName(scenario.Targets),
            first.Position,
            replayedSteps,
            first.Point,
            solution.ExploredStates,
            resolvedTargets);
    }

    private static bool SolutionMatchesValidationScenarios(
        ConstructionSolution solution,
        ConstructionSearchOptions options) =>
        options.ValidationScenarios is not { Count: > 0 } scenarios ||
        scenarios.All(scenario => ReplaysSolution(solution, scenario, options));

    private static ConstructionSearchPoint? ResolvePointReference(
        ConstructionSearchPoint reference,
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<SearchElement> elements,
        double tolerance)
    {
        if (reference.ExistingPointId is { Length: > 0 } existingPointId)
            return points.FirstOrDefault(point =>
                string.Equals(
                    point.ExistingPointId,
                    existingPointId,
                    StringComparison.Ordinal));

        if (reference.Key.StartsWith("grid-point:", StringComparison.Ordinal))
            return points.FirstOrDefault(point =>
                string.Equals(point.Key, reference.Key, StringComparison.Ordinal));

        if (reference.ParentElementA is { Length: > 0 } parentA &&
            reference.ParentElementB is { Length: > 0 } parentB)
        {
            var match = points.FirstOrDefault(point =>
                SameParentPair(
                    point.ParentElementA,
                    point.ParentElementB,
                    parentA,
                    parentB) &&
                string.Equals(point.Selector, reference.Selector, StringComparison.Ordinal));
            if (match is not null) return match;

            var first = elements.FirstOrDefault(element =>
                string.Equals(element.Key, parentA, StringComparison.Ordinal));
            var second = elements.FirstOrDefault(element =>
                string.Equals(element.Key, parentB, StringComparison.Ordinal));
            if (first is null || second is null) return null;
            var intersections = GeometryAlgorithms
                .Intersections(first.Geometry, second.Geometry)
                .Where(point => point.X is > double.NegativeInfinity and < double.PositiveInfinity &&
                                point.Y is > double.NegativeInfinity and < double.PositiveInfinity)
                .ToList();
            var index = string.Equals(reference.Selector, "second", StringComparison.Ordinal)
                ? 1
                : 0;
            if (intersections.Count <= index) return null;
            var position = intersections[index];
            return new ConstructionSearchPoint(
                $"replayed:{PointKey(position)}",
                position,
                ParentElementA: parentA,
                ParentElementB: parentB,
                Selector: reference.Selector);
        }

        return points.FirstOrDefault(point =>
            string.Equals(point.Key, reference.Key, StringComparison.Ordinal) &&
            point.Position.DistanceTo(reference.Position) <= tolerance);
    }

    private static bool SameParentPair(
        string? firstA,
        string? firstB,
        string secondA,
        string secondB) =>
        string.Equals(firstA, secondA, StringComparison.Ordinal) &&
        string.Equals(firstB, secondB, StringComparison.Ordinal) ||
        string.Equals(firstA, secondB, StringComparison.Ordinal) &&
        string.Equals(firstB, secondA, StringComparison.Ordinal);

    private static (
        IReadOnlyList<ConstructionSolution> Solutions,
        long ExploredAtDepth,
        bool LimitReached) SearchDepthParallel(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<SearchElement> elements,
        int depthLimit,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        long exploredBeforeDepth,
        IProgress<ConstructionSearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var context = new ParallelSearchContext(
            depthLimit,
            options,
            exploredBeforeDepth,
            progress);
        ExploreParallelState(
            points.ToList(),
            elements.ToList(),
            [],
            targets,
            context,
            parallelizeCandidates: true,
            cancellationToken);
        return (
            context.Solutions
                .OrderBy(solution => solution.StepCount)
                .ThenBy(solution => string.Join(
                    "|",
                    solution.Steps.Select(step =>
                        $"{step.Tool}:{PointKey(step.PointA.Position)}:{PointKey(step.PointB.Position)}")),
                    StringComparer.Ordinal)
                .Take(options.MaxSolutions)
                .ToList(),
            System.Math.Min(context.ExploredAtDepth, options.MaxStates),
            context.LimitReached);
    }

    private static void ExploreParallelState(
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ParallelSearchContext context,
        bool parallelizeCandidates,
        CancellationToken cancellationToken)
    {
        if (steps.Count >= context.DepthLimit || context.ShouldStop)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        var remainingSteps = context.DepthLimit - steps.Count;
        var unresolvedTargets = targets
            .Where(target => FindPoint(
                points,
                target.Position,
                context.Options.Tolerance) is null)
            .ToList();
        var targetsWithoutCarrier = unresolvedTargets
            .Where(target => !elements.Any(element =>
                ContainsTarget(
                    element.Geometry,
                    target.Position,
                    context.Options.Tolerance)))
            .ToList();
        var candidates = GenerateCandidates(
                points,
                steps.Count,
                context.Options,
                targets)
            .Where(candidate =>
                elements.All(existing =>
                    existing.Signature != candidate.Signature))
            .ToList();

        if (remainingSteps == 1)
        {
            if (targetsWithoutCarrier.Count > 0)
                return;
            candidates = candidates
                .Where(candidate => unresolvedTargets.All(target =>
                    ContainsTarget(
                        candidate.Geometry,
                        target.Position,
                        context.Options.Tolerance)))
                .ToList();
        }
        else if (remainingSteps == 2 && targetsWithoutCarrier.Count > 0)
        {
            candidates = candidates
                .Where(candidate => targetsWithoutCarrier.All(target =>
                    ContainsTarget(
                        candidate.Geometry,
                        target.Position,
                        context.Options.Tolerance)))
                .ToList();
        }

        candidates = OrderCandidates(
                candidates,
                unresolvedTargets,
                elements,
                targets,
                context.Options.Tolerance)
            .ToList();

        if (parallelizeCandidates && candidates.Count > 1)
        {
            try
            {
                Parallel.ForEach(
                    candidates,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism =
                            context.Options.MaxDegreeOfParallelism
                    },
                    candidate =>
                    {
                        if (context.ShouldStop)
                            return;
                        ProcessParallelCandidate(
                            points.ToList(),
                            elements.ToList(),
                            steps.ToList(),
                            candidate,
                            remainingSteps,
                            targets,
                            context,
                            cancellationToken);
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            return;
        }

        foreach (var candidate in candidates)
        {
            if (context.ShouldStop)
                return;
            ProcessParallelCandidate(
                points,
                elements,
                steps,
                candidate,
                remainingSteps,
                targets,
                context,
                cancellationToken);
        }
    }

    private static void ProcessParallelCandidate(
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        SearchElement candidate,
        int remainingSteps,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ParallelSearchContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.TryReserveState())
            return;

        var previousPointCount = points.Count;
        var addedPoints = IntersectionsWithExisting(
            candidate,
            elements,
            points,
            context.Options);
        if (points.Count + addedPoints.Count > context.Options.MaxPointsPerState)
            return;

        points.AddRange(addedPoints);
        elements.Add(candidate);
        steps.Add(candidate.Step!);

        try
        {
            var stateSignature = StateSignature(elements);
            if (!context.VisitedStates.TryAdd(stateSignature, 0))
                return;

            var resolvedTargets = ResolveTargets(
                points,
                targets,
                context.Options.Tolerance);
            if (resolvedTargets.Count == targets.Count)
            {
                var signature = string.Join("|", steps.Select(step =>
                    $"{step.Tool}:{PointKey(step.PointA.Position)}:{PointKey(step.PointB.Position)}"));
                var first = resolvedTargets[0];
                var candidateSolution = new ConstructionSolution(
                    JointTargetName(targets),
                    first.Position,
                    steps.ToList(),
                    first.Point,
                    context.ExploredBeforeDepth + context.ExploredAtDepth,
                    resolvedTargets);
                if (steps.Count >= context.Options.MinSteps &&
                    SolutionMatchesValidationScenarios(
                        candidateSolution,
                        context.Options))
                    context.AddSolution(signature, candidateSolution);
            }
            else if (steps.Count < context.DepthLimit &&
                     (remainingSteps != 2 ||
                      CanFinishInOneStroke(
                          points,
                          elements,
                          targets,
                          context.Options)))
            {
                ExploreParallelState(
                    points,
                    elements,
                    steps,
                    targets,
                    context,
                    parallelizeCandidates: false,
                    cancellationToken);
            }
        }
        finally
        {
            steps.RemoveAt(steps.Count - 1);
            elements.RemoveAt(elements.Count - 1);
            points.RemoveRange(
                previousPointCount,
                points.Count - previousPointCount);
        }
    }

    private static void SearchDepth(
        List<ConstructionSearchPoint> points,
        List<SearchElement> elements,
        List<ConstructionStep> steps,
        int depthLimit,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        List<ConstructionSolution> solutions,
        HashSet<string> solutionSignatures,
        HashSet<string> visitedStates,
        ref long explored,
        ref long exploredAtDepth,
        ref bool limitReached,
        IProgress<ConstructionSearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (steps.Count >= depthLimit ||
            solutions.Count >= options.MaxSolutions ||
            limitReached)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        var remainingSteps = depthLimit - steps.Count;
        var unresolvedTargets = targets
            .Where(target => FindPoint(points, target.Position, options.Tolerance) is null)
            .ToList();
        var targetsWithoutCarrier = unresolvedTargets
            .Where(target => !elements.Any(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance)))
            .ToList();
        var candidates = GenerateCandidates(
                points,
                steps.Count,
                options,
                targets)
            .Where(candidate => elements.All(existing => existing.Signature != candidate.Signature))
            .ToList();

        // Every unresolved target needs two carriers meeting there. With one
        // stroke left, one carrier must already exist and the final stroke must
        // carry every unresolved target. With two strokes left, the next stroke
        // must carry each target that currently has no carrier. These are
        // necessary conditions and therefore preserve complete search.
        if (remainingSteps == 1)
        {
            if (targetsWithoutCarrier.Count > 0) return;
            candidates = candidates
                .Where(candidate => unresolvedTargets.All(target =>
                    ContainsTarget(candidate.Geometry, target.Position, options.Tolerance)))
                .ToList();
        }
        else if (remainingSteps == 2 && targetsWithoutCarrier.Count > 0)
        {
            candidates = candidates
                .Where(candidate => targetsWithoutCarrier.All(target =>
                    ContainsTarget(candidate.Geometry, target.Position, options.Tolerance)))
                .ToList();
        }

        candidates = OrderCandidates(
                candidates,
                unresolvedTargets,
                elements,
                targets,
                options.Tolerance)
            .ToList();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exploredAtDepth >= options.MaxStates)
            {
                limitReached = true;
                return;
            }
            exploredAtDepth++;
            explored++;
            if (explored % 2_000 == 0)
                progress?.Report(new ConstructionSearchProgress(
                    depthLimit,
                    explored,
                    solutions.Count,
                    $"已检查 {explored:N0} 个候选状态"));

            var previousPointCount = points.Count;
            var addedPoints = IntersectionsWithExisting(candidate, elements, points, options);
            if (points.Count + addedPoints.Count > options.MaxPointsPerState)
                continue;

            points.AddRange(addedPoints);
            elements.Add(candidate);
            steps.Add(candidate.Step!);

            var stateSignature = StateSignature(elements);
            if (!visitedStates.Add(stateSignature))
            {
                steps.RemoveAt(steps.Count - 1);
                elements.RemoveAt(elements.Count - 1);
                points.RemoveRange(previousPointCount, points.Count - previousPointCount);
                continue;
            }

            var resolvedTargets = ResolveTargets(points, targets, options.Tolerance);
            if (resolvedTargets.Count == targets.Count)
            {
                var signature = string.Join("|", steps.Select(step =>
                    $"{step.Tool}:{PointKey(step.PointA.Position)}:{PointKey(step.PointB.Position)}"));
                if (solutionSignatures.Add(signature))
                {
                    var first = resolvedTargets[0];
                    var candidateSolution = new ConstructionSolution(
                        JointTargetName(targets),
                        first.Position,
                        steps.ToList(),
                        first.Point,
                        explored,
                        resolvedTargets);
                    if (steps.Count >= options.MinSteps &&
                        SolutionMatchesValidationScenarios(candidateSolution, options))
                        solutions.Add(candidateSolution);
                }
            }
            else if (steps.Count < depthLimit &&
                     (remainingSteps != 2 ||
                      CanFinishInOneStroke(
                          points,
                          elements,
                          targets,
                          options)))
            {
                SearchDepth(
                    points,
                    elements,
                    steps,
                    depthLimit,
                    targets,
                    options,
                    solutions,
                    solutionSignatures,
                    visitedStates,
                    ref explored,
                    ref exploredAtDepth,
                    ref limitReached,
                    progress,
                    cancellationToken);
            }

            steps.RemoveAt(steps.Count - 1);
            elements.RemoveAt(elements.Count - 1);
            points.RemoveRange(previousPointCount, points.Count - previousPointCount);

            if (solutions.Count >= options.MaxSolutions || limitReached)
                return;
        }
    }

    private static IEnumerable<SearchElement> GenerateCandidates(
        IReadOnlyList<ConstructionSearchPoint> points,
        int stepIndex,
        ConstructionSearchOptions options,
        IReadOnlyList<ConstructionSearchTarget>? targets = null,
        bool accelerateOrder = false,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<SearchElement>();
        IReadOnlyList<ConstructionPointPairRank>? rankedPairs = null;
        if (accelerateOrder &&
            options.CandidateAccelerator is not null &&
            points.Count >= 32 &&
            targets is { Count: > 0 })
        {
            try
            {
                var accelerated = options.CandidateAccelerator.RankPointPairs(
                    points,
                    targets,
                    options.Tolerance,
                    cancellationToken);
                var expected = points.Count * (points.Count - 1) / 2;
                if (accelerated.Count == expected)
                    rankedPairs = accelerated;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Experimental acceleration must never make the solver unusable.
            }
        }

        IEnumerable<(int First, int Second)> pairs = rankedPairs is not null
            ? rankedPairs.Select(pair => (pair.FirstIndex, pair.SecondIndex))
            : EnumeratePointPairs(points.Count);
        foreach (var (i, j) in pairs)
        {
            if (rankedPairs is not null &&
                candidates.Count >= options.AcceleratedGuidedPreselectionLimit)
                break;
            var a = points[i];
            var b = points[j];
            var direction = b.Position - a.Position;
            if (direction.Length <= options.Tolerance) continue;

            if (options.UseStraightedge)
            {
                var geometry = GeometryResultForLine($"step:{stepIndex}", a.Position, direction);
                var step = new ConstructionStep(
                    $"step:{stepIndex}",
                    ConstructionToolKind.StraightLine,
                    a,
                    b,
                    $"过 {DisplayPoint(a)}、{DisplayPoint(b)} 作直线");
                candidates.Add(new SearchElement(
                    step.Key,
                    ElementSignature(geometry, preserveRange: false),
                    geometry,
                    step));
            }

            if (options.UseCompass)
            {
                candidates.Add(CircleCandidate(stepIndex, a, b));
                candidates.Add(CircleCandidate(stepIndex, b, a));
            }
        }

        var unique = candidates.DistinctBy(item => item.Signature);
        return rankedPairs is not null
            ? unique
            : unique
                .OrderBy(item => item.Step!.Tool)
                .ThenBy(item => item.Signature);
    }

    private static IEnumerable<(int First, int Second)> EnumeratePointPairs(
        int pointCount)
    {
        for (var i = 0; i < pointCount; i++)
        for (var j = i + 1; j < pointCount; j++)
            yield return (i, j);
    }

    private static IEnumerable<SearchElement> OrderCandidates(
        IEnumerable<SearchElement> candidates,
        IReadOnlyList<ConstructionSearchTarget> unresolvedTargets,
        IReadOnlyList<SearchElement> elements,
        IReadOnlyList<ConstructionSearchTarget> allTargets,
        double tolerance) =>
        candidates
            .OrderByDescending(candidate => unresolvedTargets.Count(target =>
                ContainsTarget(candidate.Geometry, target.Position, tolerance) &&
                elements.Any(element =>
                    ContainsTarget(element.Geometry, target.Position, tolerance))))
            .ThenByDescending(candidate => unresolvedTargets.Count(target =>
                ContainsTarget(candidate.Geometry, target.Position, tolerance)))
            .ThenByDescending(candidate => CandidateNaturalness(
                candidate,
                allTargets,
                elements,
                tolerance))
            .ThenBy(candidate => unresolvedTargets.Sum(target =>
                TargetDistance(candidate.Geometry, target.Position)))
            .ThenBy(candidate => candidate.Step!.Tool)
            .ThenBy(candidate => candidate.Signature, StringComparer.Ordinal);

    private static int CandidateNaturalness(
        SearchElement candidate,
        IReadOnlyList<ConstructionSearchTarget> targets,
        IReadOnlyList<SearchElement> elements,
        double tolerance) =>
        PointNaturalness(candidate.Step!.PointA, targets, elements, tolerance) +
        PointNaturalness(candidate.Step.PointB, targets, elements, tolerance);

    private static int PointNaturalness(
        ConstructionSearchPoint point,
        IReadOnlyList<ConstructionSearchTarget> targets,
        IReadOnlyList<SearchElement> elements,
        double tolerance)
    {
        if (targets.Any(target => target.Position.DistanceTo(point.Position) <= tolerance))
            return 12;
        if (point.ExistingPointId is not null)
            return 8;
        if (point.ParentElementA is not null && point.ParentElementB is not null)
        {
            var parents = new[] { point.ParentElementA, point.ParentElementB };
            var knownCount = parents.Count(parent => parent.StartsWith("known:", StringComparison.Ordinal));
            var gridCount = parents.Count(parent => parent.StartsWith("grid:", StringComparison.Ordinal));
            if (knownCount > 0 && gridCount > 0)
            {
                var support = elements.FirstOrDefault(element =>
                    parents.Contains(element.Key, StringComparer.Ordinal));
                if (support?.Geometry.Line is not null) return 11;
                if (support?.Geometry.Circle is not null || support?.Geometry.Arc is not null) return 8;
                return 9;
            }
            if (knownCount == 2) return 8;
            if (parents.Any(parent => parent.StartsWith("step:", StringComparison.Ordinal))) return 6;
            if (gridCount == 2) return 2;
        }
        if (point.Key.StartsWith("grid-point:", StringComparison.Ordinal))
            return 2;
        return 3;
    }

    private static string StateSignature(IEnumerable<SearchElement> elements) =>
        string.Join("|", elements
            .Where(element => element.Step is not null)
            .Select(element => element.Signature)
            .OrderBy(signature => signature, StringComparer.Ordinal));

    private static List<ConstructionResolvedTarget> ResolveTargets(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<ConstructionSearchTarget> targets,
        double tolerance)
    {
        var resolved = new List<ConstructionResolvedTarget>();
        foreach (var target in targets)
        {
            if (FindPoint(points, target.Position, tolerance) is { } point)
                resolved.Add(new ConstructionResolvedTarget(target.Name, target.Position, point));
        }
        return resolved;
    }

    private static string JointTargetName(IReadOnlyList<ConstructionSearchTarget> targets) =>
        string.Join("、", targets.Select(target => target.Name));

    private static SearchElement CircleCandidate(
        int stepIndex,
        ConstructionSearchPoint center,
        ConstructionSearchPoint edge)
    {
        var key = $"step:{stepIndex}";
        var circle = new CircleGeometry(center.Position, center.Position.DistanceTo(edge.Position));
        var geometry = new GeometryResult
        {
            ObjectId = key,
            Type = "circleByCenterPoint",
            IsValid = true,
            Circle = circle
        };
        var step = new ConstructionStep(
            key,
            ConstructionToolKind.Circle,
            center,
            edge,
            $"以 {DisplayPoint(center)} 为圆心、{DisplayPoint(center)}—{DisplayPoint(edge)} 为半径作圆");
        return new SearchElement(key, ElementSignature(geometry, preserveRange: false), geometry, step);
    }

    private static List<ConstructionSearchPoint> IntersectionsWithExisting(
        SearchElement candidate,
        IReadOnlyList<SearchElement> elements,
        IReadOnlyList<ConstructionSearchPoint> points,
        ConstructionSearchOptions options)
    {
        var added = new List<ConstructionSearchPoint>();
        foreach (var existing in elements)
        {
            var intersections = GeometryAlgorithms.Intersections(candidate.Geometry, existing.Geometry).ToList();
            for (var index = 0; index < intersections.Count; index++)
            {
                var point = intersections[index];
                if (!IncludesPoint(options, point) ||
                    FindPoint(points, point, options.Tolerance) is not null ||
                    FindPoint(added, point, options.Tolerance) is not null)
                    continue;
                added.Add(new ConstructionSearchPoint(
                    $"point:{PointKey(point)}",
                    point,
                    ParentElementA: candidate.Key,
                    ParentElementB: existing.Key,
                    Selector: index == 0 ? "first" : "second"));
            }
        }
        return added;
    }

    private static bool CanFinishInOneStroke(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<SearchElement> elements,
        IReadOnlyList<ConstructionSearchTarget> targets,
        ConstructionSearchOptions options,
        IReadOnlyList<ConstructionSearchPoint>? requiredNewPoints = null)
    {
        var unresolved = targets
            .Where(target => FindPoint(points, target.Position, options.Tolerance) is null)
            .ToList();
        if (unresolved.Count == 0) return true;

        // A final stroke can only reveal a target by meeting a carrier that is
        // already present. Rejecting the state here is an exact, not heuristic,
        // prune.
        if (unresolved.Any(target => !elements.Any(element =>
                ContainsTarget(element.Geometry, target.Position, options.Tolerance))))
            return false;

        if (options.UseStraightedge &&
            CanFinishWithStraightLine(
                points,
                elements,
                unresolved,
                options,
                requiredNewPoints))
            return true;

        if (!options.UseCompass) return false;

        for (var i = 0; i < points.Count; i++)
        {
            for (var j = 0; j < points.Count; j++)
            {
                if (i == j) continue;
                if (requiredNewPoints is { Count: > 0 } &&
                    !ContainsPoint(requiredNewPoints, points[i], options.Tolerance) &&
                    !ContainsPoint(requiredNewPoints, points[j], options.Tolerance))
                    continue;
                var center = points[i];
                var edge = points[j];
                if (center.Position.DistanceTo(edge.Position) <= options.Tolerance) continue;
                var candidate = CircleCandidate(0, center, edge);
                if (elements.Any(existing => existing.Signature == candidate.Signature)) continue;
                if (unresolved.All(target =>
                        ContainsTarget(candidate.Geometry, target.Position, options.Tolerance) &&
                        CreatesTarget(candidate, elements, target.Position, options.Tolerance)))
                    return true;
            }
        }
        return false;
    }

    private static bool CanFinishWithStraightLine(
        IReadOnlyList<ConstructionSearchPoint> points,
        IReadOnlyList<SearchElement> elements,
        IReadOnlyList<ConstructionSearchTarget> unresolved,
        ConstructionSearchOptions options,
        IReadOnlyList<ConstructionSearchPoint>? requiredNewPoints)
    {
        if (unresolved.Count >= 2)
        {
            var first = unresolved[0].Position;
            var second = unresolved[1].Position;
            var direction = second - first;
            if (direction.Length <= options.Tolerance) return false;
            var requiredGeometry = GeometryResultForLine("finish", first, direction);
            if (unresolved.Any(target =>
                    !ContainsTarget(requiredGeometry, target.Position, options.Tolerance)))
                return false;

            for (var i = 0; i < points.Count; i++)
            {
                if (!ContainsTarget(requiredGeometry, points[i].Position, options.Tolerance)) continue;
                for (var j = i + 1; j < points.Count; j++)
                {
                    if (!ContainsTarget(requiredGeometry, points[j].Position, options.Tolerance)) continue;
                    if (requiredNewPoints is { Count: > 0 } &&
                        !ContainsPoint(requiredNewPoints, points[i], options.Tolerance) &&
                        !ContainsPoint(requiredNewPoints, points[j], options.Tolerance))
                        continue;
                    var candidate = LineCandidate(0, points[i], points[j]);
                    if (elements.Any(existing => existing.Signature == candidate.Signature)) continue;
                    if (unresolved.All(target =>
                            CreatesTarget(candidate, elements, target.Position, options.Tolerance)))
                        return true;
                }
            }
            return false;
        }

        var targetPosition = unresolved[0].Position;
        if (requiredNewPoints is { Count: > 0 })
        {
            var oldCarrierLines = new Dictionary<string, ConstructionSearchPoint>(StringComparer.Ordinal);
            var targetPoint = new ConstructionSearchPoint("guided-target", targetPosition);
            foreach (var point in points)
            {
                if (ContainsPoint(requiredNewPoints, point, options.Tolerance) ||
                    point.Position.DistanceTo(targetPosition) <= options.Tolerance)
                    continue;
                var carrier = LineCandidate(0, targetPoint, point);
                oldCarrierLines.TryAdd(carrier.Signature, point);
            }

            var newCarrierLines = new Dictionary<string, ConstructionSearchPoint>(StringComparer.Ordinal);
            foreach (var newPoint in requiredNewPoints)
            {
                if (newPoint.Position.DistanceTo(targetPosition) <= options.Tolerance) continue;
                var carrier = LineCandidate(0, targetPoint, newPoint);
                ConstructionSearchPoint? other = null;
                if (oldCarrierLines.TryGetValue(carrier.Signature, out var oldPoint))
                    other = oldPoint;
                else if (newCarrierLines.TryGetValue(carrier.Signature, out var earlierNewPoint))
                    other = earlierNewPoint;
                else
                    newCarrierLines[carrier.Signature] = newPoint;

                if (other is not null)
                {
                    var candidate = LineCandidate(0, newPoint, other);
                    if (!ContainsTarget(candidate.Geometry, targetPosition, options.Tolerance) ||
                        elements.Any(existing => existing.Signature == candidate.Signature))
                        continue;
                    if (CreatesTarget(candidate, elements, targetPosition, options.Tolerance))
                        return true;
                }
            }
            return false;
        }

        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                var candidate = LineCandidate(0, points[i], points[j]);
                if (!ContainsTarget(candidate.Geometry, targetPosition, options.Tolerance) ||
                    elements.Any(existing => existing.Signature == candidate.Signature))
                    continue;
                if (CreatesTarget(candidate, elements, targetPosition, options.Tolerance))
                    return true;
            }
        }
        return false;
    }

    private static bool ContainsPoint(
        IReadOnlyList<ConstructionSearchPoint> points,
        ConstructionSearchPoint candidate,
        double tolerance) =>
        points.Any(point => point.Position.DistanceTo(candidate.Position) <= tolerance);

    private static bool CreatesTarget(
        SearchElement candidate,
        IReadOnlyList<SearchElement> elements,
        Point2 target,
        double tolerance) =>
        elements.Any(existing =>
            ContainsTarget(existing.Geometry, target, tolerance) &&
            GeometryAlgorithms
                .Intersections(candidate.Geometry, existing.Geometry)
                .Any(point => point.DistanceTo(target) <= tolerance));

    private static SearchElement LineCandidate(
        int stepIndex,
        ConstructionSearchPoint pointA,
        ConstructionSearchPoint pointB)
    {
        var key = $"step:{stepIndex}";
        var geometry = GeometryResultForLine(key, pointA.Position, pointB.Position - pointA.Position);
        var step = new ConstructionStep(
            key,
            ConstructionToolKind.StraightLine,
            pointA,
            pointB,
            $"过 {DisplayPoint(pointA)}、{DisplayPoint(pointB)} 作直线");
        return new SearchElement(
            key,
            ElementSignature(geometry, preserveRange: false),
            geometry,
            step);
    }

    private static bool IncludesPoint(ConstructionSearchOptions options, Point2 point) =>
        (!options.MinX.HasValue || point.X >= options.MinX.Value - options.Tolerance) &&
        (!options.MaxX.HasValue || point.X <= options.MaxX.Value + options.Tolerance) &&
        (!options.MinY.HasValue || point.Y >= options.MinY.Value - options.Tolerance) &&
        (!options.MaxY.HasValue || point.Y <= options.MaxY.Value + options.Tolerance);

    private static GeometryResult GeometryResultForLine(string id, Point2 origin, Vector2 direction) =>
        new()
        {
            ObjectId = id,
            Type = "lineThroughPoints",
            IsValid = true,
            Line = new LineGeometry(origin, direction)
        };

    private static List<ConstructionSearchPoint> DistinctPoints(
        IReadOnlyList<ConstructionSearchPoint> points,
        double tolerance)
    {
        var result = new List<ConstructionSearchPoint>();
        foreach (var point in points)
            if (FindPoint(result, point.Position, tolerance) is null)
                result.Add(point);
        return result;
    }

    private static ConstructionSearchPoint? FindPoint(
        IReadOnlyList<ConstructionSearchPoint> points,
        Point2 target,
        double tolerance) =>
        points.FirstOrDefault(point => point.Position.DistanceTo(target) <= tolerance);

    private static string ElementSignature(GeometryResult geometry, bool preserveRange)
    {
        if (geometry.Line is { } line)
        {
            var direction = line.Direction.Normalized;
            var a = -direction.Y;
            var b = direction.X;
            var c = a * line.Origin.X + b * line.Origin.Y;
            if (a < -1e-12 || (System.Math.Abs(a) <= 1e-12 && b < 0))
            {
                a = -a;
                b = -b;
                c = -c;
            }
            var range = preserveRange
                ? $":{FiniteKey(line.MinParameter)}:{FiniteKey(line.MaxParameter)}"
                : ":infinite";
            return $"L:{Rounded(a)}:{Rounded(b)}:{Rounded(c)}{range}";
        }
        if (geometry.Circle is { } circle)
            return $"C:{Rounded(circle.Center.X)}:{Rounded(circle.Center.Y)}:{Rounded(circle.Radius)}";
        if (geometry.Arc is { } arc)
            return $"A:{Rounded(arc.Center.X)}:{Rounded(arc.Center.Y)}:{Rounded(arc.Radius)}:{Rounded(arc.StartAngleRadians)}:{Rounded(arc.SweepRadians)}";
        return $"unsupported:{geometry.ObjectId}";
    }

    private static double TargetDistance(GeometryResult geometry, Point2 target)
    {
        if (geometry.Line is { } line)
        {
            var numerator = System.Math.Abs(Vector2.Cross(target - line.Origin, line.Direction));
            return numerator / System.Math.Max(line.Direction.Length, 1e-12);
        }
        if (geometry.Circle is { } circle)
            return System.Math.Abs(circle.Center.DistanceTo(target) - circle.Radius);
        return double.MaxValue;
    }

    private static bool ContainsTarget(GeometryResult geometry, Point2 target, double tolerance)
    {
        if (geometry.Line is { } line)
        {
            var lengthSquared = Vector2.Dot(line.Direction, line.Direction);
            if (lengthSquared <= tolerance * tolerance) return false;
            var parameter = Vector2.Dot(target - line.Origin, line.Direction) / lengthSquared;
            var parameterTolerance = tolerance / System.Math.Sqrt(lengthSquared);
            return parameter >= line.MinParameter - parameterTolerance &&
                   parameter <= line.MaxParameter + parameterTolerance &&
                   line.At(parameter).DistanceTo(target) <= tolerance;
        }
        if (geometry.Circle is { } circle)
            return System.Math.Abs(circle.Center.DistanceTo(target) - circle.Radius) <= tolerance;
        if (geometry.Arc is { } arc)
            return System.Math.Abs(arc.Center.DistanceTo(target) - arc.Radius) <= tolerance &&
                   arc.Contains(target);
        return false;
    }

    private static string PointKey(Point2 point) => $"{Rounded(point.X)},{Rounded(point.Y)}";
    private static string WitnessPointKey(Point2 point) =>
        $"{System.Math.Round(point.X, 7):G17},{System.Math.Round(point.Y, 7):G17}";
    private static string Rounded(double value) =>
        (System.Math.Abs(value) < 0.5e-9 ? 0 : System.Math.Round(value, 9))
        .ToString("G17", CultureInfo.InvariantCulture);
    private static string FiniteKey(double value) =>
        double.IsNegativeInfinity(value) ? "-inf" :
        double.IsPositiveInfinity(value) ? "+inf" :
        Rounded(value);
    private static string DisplayPoint(ConstructionSearchPoint point) =>
        point.ExistingPointId ?? $"({point.Position.X:G5},{point.Position.Y:G5})";

    private static void Validate(ConstructionSearchOptions options)
    {
        if (options.MinSteps is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(options.MinSteps));
        if (options.MaxSteps is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(options.MaxSteps));
        if (options.MinSteps > options.MaxSteps)
            throw new ArgumentException("最小笔数不能大于最大笔数。", nameof(options));
        if (options.MaxSolutions is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(options.MaxSolutions));
        if (options.MaxStates < 1) throw new ArgumentOutOfRangeException(nameof(options.MaxStates));
        if (options.GuidedCandidateLimit is < 1 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(options.GuidedCandidateLimit));
        if (options.AcceleratedGuidedPreselectionLimit is < 256 or > 1_000_000)
            throw new ArgumentOutOfRangeException(
                nameof(options.AcceleratedGuidedPreselectionLimit));
        if (options.MaxDegreeOfParallelism is < 1 or > 256)
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxDegreeOfParallelism));
        if ((options.MinX.HasValue && options.MaxX.HasValue &&
             options.MinX.Value > options.MaxX.Value) ||
            (options.MinY.HasValue && options.MaxY.HasValue &&
             options.MinY.Value > options.MaxY.Value))
            throw new ArgumentException("搜索边界无效。", nameof(options));
        if (!options.UseStraightedge && !options.UseCompass)
            throw new ArgumentException("至少启用直尺或圆规中的一种工具。", nameof(options));
    }

    private static void ValidateTargets(IReadOnlyList<ConstructionSearchTarget> targets)
    {
        if (targets.Count == 0)
            throw new ArgumentException("至少提供一个构造目标。", nameof(targets));
        if (targets.Any(target =>
                string.IsNullOrWhiteSpace(target.Name) ||
                !double.IsFinite(target.Position.X) ||
                !double.IsFinite(target.Position.Y)))
            throw new ArgumentException("目标名称和坐标必须有效。", nameof(targets));
        if (targets.Select(target => target.Name).Distinct(StringComparer.Ordinal).Count() != targets.Count)
            throw new ArgumentException("目标名称不能重复。", nameof(targets));
    }
}
