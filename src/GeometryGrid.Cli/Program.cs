using System.Diagnostics;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Serialization;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
    Console.WriteLine("\n正在停止搜索……");
};

try
{
    if (args.Length == 0 ||
        args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    return args[0].ToLowerInvariant() switch
    {
        "search" => RunSearch(args[1..], cancellation.Token),
        "replay" => RunReplay(args[1..]),
        _ => Fail($"未知命令：{args[0]}\n使用 --help 查看用法。")
    };
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误：{ex.Message}");
    return 1;
}

static int RunSearch(string[] args, CancellationToken cancellationToken)
{
    if (args.Length == 0)
        return Fail("缺少 GeometryGrid JSON 文件路径。");

    var projectPath = Path.GetFullPath(args[0]);
    var parsed = ParseOptions(args[1..]);
    var document = ProjectSerializer.Load(projectPath);
    var settings = document.Solver;

    var minSteps = parsed.Int("min-steps", settings.MinSteps);
    var maxSteps = parsed.Int(
        "max-steps",
        parsed.Int("steps", settings.MaxSteps));
    var maxSolutions = parsed.Int(
        "solutions",
        settings.MaxSolutionsPerTarget);
    var maxStates = parsed.Long("states", settings.MaxStates);
    var threads = parsed.Int(
        "threads",
        settings.CpuThreads > 0
            ? settings.CpuThreads
            : Environment.ProcessorCount);
    var parameterSamples = parsed.Int(
        "samples",
        settings.ParameterSampleCount);
    var useGrid = parsed.Has("no-grid")
        ? false
        : parsed.Has("grid")
            ? true
            : settings.UseGrid;
    var validateParameters = parsed.Has("validate")
        ? true
        : parsed.Has("no-validate")
            ? false
            : settings.ValidateParameters;
    settings.ValidateParameters = validateParameters;
    settings.ParameterSampleCount = parameterSamples;
    settings.UseGrid = useGrid;

    var strategy = ParseStrategy(
        parsed.String("strategy") ??
        settings.SearchStrategy.ToString());
    var problem = ConstructionSearchProblemBuilder.Build(
        document,
        useGrid,
        parameterSamples);
    var bounds = problem.Evaluation.Scene.Canvas;
    var options = new ConstructionSearchOptions(
        MinSteps: minSteps,
        MaxSteps: maxSteps,
        MaxSolutions: maxSolutions,
        MaxStates: maxStates,
        MaxPointsPerState: useGrid ? 240 : 120,
        UseStraightedge: settings.UseStraightedge,
        UseCompass: settings.UseCompass,
        MinX: bounds.MinX,
        MaxX: bounds.MaxX,
        MinY: bounds.MinY,
        MaxY: bounds.MaxY,
        UseCollinearityClosure: strategy is 0 or 1,
        UseGuidedSearch: strategy is 0 or 2,
        UseExhaustiveSearch: strategy is 0 or 3,
        ValidationScenarios: problem.ValidationScenarios,
        MaxDegreeOfParallelism: System.Math.Clamp(threads, 1, 256));

    Console.WriteLine("GeometryGrid 无界面暴算器 v3.01");
    Console.WriteLine($"项目：{projectPath}");
    Console.WriteLine(
        $"已知点 {problem.Points.Count}，已知对象 {problem.Elements.Count}，"
        + $"目标 {problem.Targets.Count}");
    Console.WriteLine(
        $"CPU 并行线程 {options.MaxDegreeOfParallelism}，"
        + $"笔数范围 {minSteps}–{maxSteps}，每层状态上限 {maxStates:N0}");
    if (problem.ValidationScenarios.Count > 0)
        Console.WriteLine(
            $"不定图：当前图先筛选，命中后再验证 "
            + $"{problem.ValidationScenarios.Count} 个位置");

    var watch = Stopwatch.StartNew();
    var lastProgress = TimeSpan.Zero;
    var progress = new Progress<ConstructionSearchProgress>(value =>
    {
        if (watch.Elapsed - lastProgress < TimeSpan.FromMilliseconds(180) &&
            value.ExploredStates % 10_000 != 0)
            return;
        lastProgress = watch.Elapsed;
        var rate = value.ExploredStates /
                   System.Math.Max(watch.Elapsed.TotalSeconds, 1e-9);
        Console.Write(
            $"\r[{watch.Elapsed:hh\\:mm\\:ss}] {value.Message} | "
            + $"深度 {value.CurrentDepth} | "
            + $"状态 {value.ExploredStates:N0} | "
            + $"{rate:N0}/秒 | 解 {value.SolutionsFound}      ");
    });

    var engine = new ConstructionSearchEngine();
    var report = engine.Search(
        problem.Points,
        problem.Elements,
        problem.Targets,
        options,
        progress,
        cancellationToken);
    watch.Stop();
    Console.WriteLine();
    Console.WriteLine(
        $"完成：{watch.Elapsed}，检查 {report.ExploredStates:N0} 个状态，"
        + $"找到 {report.Solutions.Count} 个解。");

    foreach (var solution in report.Solutions)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"{solution.TargetName}：{solution.StepCount} 笔");
        foreach (var item in solution.Steps.Select(
                     (step, index) => (step, index)))
            Console.WriteLine(
                $"  {item.index + 1}. {item.step.Description}");
    }

    if (report.Solutions.Count == 0)
        return 2;

    var outputPath = Path.GetFullPath(
        parsed.String("out") ??
        Path.Combine(
            Path.GetDirectoryName(projectPath)!,
            Path.GetFileNameWithoutExtension(projectPath)
            + ".ggsteps.json"));
    ConstructionSolutionSerializer.Save(
        new ConstructionSolutionPackage
        {
            SourceProject = projectPath,
            Strategy = report.Strategy,
            OptimalityProven = report.OptimalityProven,
            Solutions = report.Solutions.ToList()
        },
        outputPath);
    Console.WriteLine($"步骤已导出：{outputPath}");
    return 0;
}

static int RunReplay(string[] args)
{
    if (args.Length < 2)
        return Fail(
            "用法：GeometryGrid.Solver replay <project.json> <steps.ggsteps.json>");
    var projectPath = Path.GetFullPath(args[0]);
    var stepsPath = Path.GetFullPath(args[1]);
    var document = ProjectSerializer.Load(projectPath);
    var problem = ConstructionSearchProblemBuilder.Build(document);
    var package = ConstructionSolutionSerializer.Load(stepsPath);
    var bounds = problem.Evaluation.Scene.Canvas;
    var options = new ConstructionSearchOptions(
        MinSteps: 0,
        MaxSteps: document.Solver.MaxSteps,
        MaxPointsPerState: document.Solver.UseGrid ? 300 : 160,
        UseStraightedge: document.Solver.UseStraightedge,
        UseCompass: document.Solver.UseCompass,
        MinX: bounds.MinX,
        MaxX: bounds.MaxX,
        MinY: bounds.MinY,
        MaxY: bounds.MaxY,
        ValidationScenarios: problem.ValidationScenarios);
    var baseScenario = new ConstructionSearchScenario(
        problem.Points,
        problem.Elements,
        problem.Targets,
        "当前图");

    var valid = 0;
    foreach (var solution in package.Solutions)
    {
        var replayed = ConstructionSearchEngine.ReplaySolution(
            solution,
            baseScenario,
            options);
        var allFramesValid = replayed is not null &&
                             problem.ValidationScenarios.All(scenario =>
                                 ConstructionSearchEngine.ReplaysSolution(
                                     solution,
                                     scenario,
                                     options));
        Console.WriteLine(
            $"{solution.TargetName}："
            + (allFramesValid ? "验证通过" : "验证失败"));
        if (allFramesValid)
            valid++;
    }
    Console.WriteLine(
        $"通过 {valid}/{package.Solutions.Count} 个解。");
    return valid == package.Solutions.Count ? 0 : 3;
}

static ParsedOptions ParseOptions(string[] args)
{
    var values = new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index++)
    {
        var token = args[index];
        if (!token.StartsWith("--", StringComparison.Ordinal))
            throw new InvalidOperationException($"无法识别参数：{token}");
        var name = token[2..];
        if (index + 1 < args.Length &&
            !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            values[name] = args[++index];
        else
            values[name] = null;
    }
    return new ParsedOptions(values);
}

static int ParseStrategy(string text) =>
    text.ToLowerInvariant() switch
    {
        "0" or "auto" => 0,
        "1" or "collinearity" => 1,
        "2" or "guided" => 2,
        "3" or "exhaustive" => 3,
        _ => throw new InvalidOperationException(
            $"搜索策略无效：{text}")
    };

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        GeometryGrid 无界面暴算器 v3.01

        搜索：
          GeometryGrid.Solver search <题目.json> [选项]

        验证/导入步骤：
          GeometryGrid.Solver replay <题目.json> <步骤.ggsteps.json>

        常用选项：
          --threads N       CPU 并行线程数，默认使用全部逻辑处理器
          --min-steps N     起始作图笔数；直接跳过更浅层
          --max-steps N     最大作图笔数
          --steps N         --max-steps 的兼容别名
          --states N        每个笔数层的状态上限
          --solutions N     保留解数
          --strategy NAME   auto / collinearity / guided / exhaustive
          --samples N       不定图联合检查总位置数（含当前图）
          --grid            强制把网格作为已知
          --no-grid         不把网格作为已知
          --validate        启用不定图验证
          --no-validate     关闭不定图验证
          --out PATH        作图步骤输出路径

        搜索过程中按 Ctrl+C 可停止。软件窗口不需要启动。
        """);
}

file sealed class ParsedOptions(
    IReadOnlyDictionary<string, string?> values)
{
    public bool Has(string name) => values.ContainsKey(name);
    public string? String(string name) =>
        values.TryGetValue(name, out var value) ? value : null;
    public int Int(string name, int fallback) =>
        String(name) is { } value
            ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;
    public long Long(string name, long fallback) =>
        String(name) is { } value
            ? long.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
            : fallback;
}
