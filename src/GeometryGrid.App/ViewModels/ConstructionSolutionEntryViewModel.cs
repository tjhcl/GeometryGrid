using GeometryGrid.Core.Geometry;

namespace GeometryGrid.App.ViewModels;

public sealed class ConstructionSolutionEntryViewModel
{
    public ConstructionSolutionEntryViewModel(
        ConstructionSolution solution,
        bool optimalityProven = true,
        string strategy = "")
    {
        Solution = solution;
        OptimalityProven = optimalityProven;
        Strategy = strategy;
    }

    public ConstructionSolution Solution { get; }
    public bool OptimalityProven { get; }
    public string Strategy { get; }
    private string ResultKind => Strategy == "collinearity-closure"
        ? "共线闭包解"
        : OptimalityProven
            ? "联合最优解"
            : "快速联合解";
    public string Title => Solution.CompletedTargets.Count > 1
        ? $"{string.Join("、", Solution.CompletedTargets.Select(target => target.Name))} · "
          + $"{Solution.StepCount} 笔{ResultKind}"
        : $"{Solution.TargetName} · {Solution.StepCount} 笔"
          + $"{(Strategy == "collinearity-closure" ? "共线闭包解" : OptimalityProven ? "最优解" : "快速解")}";
    public string TargetText => "目标：" + string.Join("；", Solution.CompletedTargets.Select(target =>
        $"{target.Name}({target.Position.X:G8}, {target.Position.Y:G8})"));
    public string StepSummary => Solution.StepCount == 0
        ? "目标点已经存在于已知图中"
        : string.Join(Environment.NewLine, Solution.Steps.Select((step, index) =>
            $"{index + 1}. {step.Description}"));
    public string SearchSummary => Strategy == "collinearity-closure"
        ? $"共线闭包反推在第 {Solution.ExploredStates:N0} 个候选处发现"
        : $"搜索到第 {Solution.ExploredStates:N0} 个状态时发现";
}
