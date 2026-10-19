using System.Text.Json;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Core.Serialization;

public static class ProjectSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static ProjectDocument Load(string path)
    {
        return LoadJson(File.ReadAllText(path), path);
    }

    public static ProjectDocument LoadJson(string json, string sourceName)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ProjectDocument>(json, Options)
                           ?? throw new ProjectLoadException(sourceName, 0, 0, "$", null, "JSON 文档为空。", "请提供包含 schemaVersion、canvas、grid、styles、parameters、objects 和 exports 的对象。");
            document.Solver ??= new SolverSettings();
            document.Solver.Targets ??= [];
            Validate(document, sourceName);
            foreach (var item in document.Objects.Where(o => o.Label is not null))
            {
                item.Label!.DefaultOffsetX = item.Label.OffsetX;
                item.Label.DefaultOffsetY = item.Label.OffsetY;
            }
            return document;
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException(sourceName, (int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1,
                ex.Path ?? "$", null, ex.Message, "检查该位置附近的逗号、引号和字段类型。", ex);
        }
    }

    public static void Save(ProjectDocument document, string path)
    {
        var json = JsonSerializer.Serialize(document, Options);
        File.WriteAllText(path, json);
    }

    public static ProjectDocument Clone(ProjectDocument document)
    {
        var clone = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(document, Options), Options)!;
        foreach (var item in clone.Objects.Where(o => o.Label is not null))
        {
            var source = document.Objects.First(o => o.Id == item.Id).Label!;
            item.Label!.DefaultOffsetX = source.DefaultOffsetX;
            item.Label.DefaultOffsetY = source.DefaultOffsetY;
        }
        return clone;
    }

    private static void Validate(ProjectDocument document, string path)
    {
        if (document.SchemaVersion != "1.0")
            throw new ProjectLoadException(path, 0, 0, "schemaVersion", null, $"不支持的 Schema 版本“{document.SchemaVersion}”。", "请将 schemaVersion 设置为 1.0。");
        if (document.Canvas.MaxX <= document.Canvas.MinX || document.Canvas.MaxY <= document.Canvas.MinY)
            throw new ProjectLoadException(path, 0, 0, "canvas", null, "画布范围无效。", "确保 maxX > minX 且 maxY > minY。");
        if (document.Grid.Step <= 0)
            throw new ProjectLoadException(path, 0, 0, "grid.step", null, "网格步长必须大于零。", "把 grid.step 设置为正数。");

        if (document.Solver.MinSteps is < 0 or > 8)
            throw new ProjectLoadException(path, 0, 0, "solver.minSteps", null, "起始笔数必须是 0–8。", "请修改 solver.minSteps。");
        if (document.Solver.MaxSteps is < 1 or > 8)
            throw new ProjectLoadException(path, 0, 0, "solver.maxSteps", null, "最大作图笔数必须是 1–8。", "请修改 solver.maxSteps。");
        if (document.Solver.MinSteps > document.Solver.MaxSteps)
            throw new ProjectLoadException(path, 0, 0, "solver.minSteps", null, "起始笔数不能大于最大笔数。", "请调整 minSteps/maxSteps。");
        if (document.Solver.MaxSolutionsPerTarget is < 1 or > 20)
            throw new ProjectLoadException(path, 0, 0, "solver.maxSolutionsPerTarget", null, "保留联合解数必须是 1–20。", "请修改 solver.maxSolutionsPerTarget。");
        if (document.Solver.MaxStates < 100)
            throw new ProjectLoadException(path, 0, 0, "solver.maxStates", null, "状态上限不能小于 100。", "请提高 solver.maxStates。");
        if (!document.Solver.UseStraightedge && !document.Solver.UseCompass)
            throw new ProjectLoadException(path, 0, 0, "solver", null, "直尺和圆规不能同时关闭。", "至少启用 useStraightedge 或 useCompass。");
        if (document.Solver.ParameterSampleCount is < 3 or > 11)
            throw new ProjectLoadException(
                path,
                0,
                0,
                "solver.parameterSampleCount",
                null,
                "含参联合验解的抽样位置数必须是 3—11。",
                "请将 parameterSampleCount 设置为 3–11；推荐使用 5 或 7。");
        if (document.Solver.CpuThreads is < 0 or > 256)
            throw new ProjectLoadException(
                path,
                0,
                0,
                "solver.cpuThreads",
                null,
                "CPU 线程数必须是 0–256；0 表示自动使用全部逻辑处理器。",
                "请把 cpuThreads 设置为 0，或填写希望使用的线程数。");
        var duplicateTarget = document.Solver.Targets
            .GroupBy(target => target.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateTarget is not null)
            throw new ProjectLoadException(path, 0, 0, "solver.targets[].name", duplicateTarget.Key, "求解目标名称为空或重复。", "为每个目标设置唯一名称。");
        if (document.Solver.Targets.Any(target =>
                target.Constraint is null &&
                (string.IsNullOrWhiteSpace(target.X) ||
                 string.IsNullOrWhiteSpace(target.Y))))
            throw new ProjectLoadException(
                path,
                0,
                0,
                "solver.targets[]",
                null,
                "求解目标必须填写 x、y 表达式，或提供 constraint。",
                "普通目标填写 x 和 y；超级不定图可使用 angleBisectorPair 约束目标。");
        var invalidConstraintTarget = document.Solver.Targets.FirstOrDefault(target =>
            target.Constraint is { } constraint &&
            (string.IsNullOrWhiteSpace(constraint.Type) ||
             string.IsNullOrWhiteSpace(constraint.Role)));
        if (invalidConstraintTarget is not null)
            throw new ProjectLoadException(
                path,
                0,
                0,
                "solver.targets[].constraint",
                invalidConstraintTarget.Name,
                "约束目标缺少 type 或 role。",
                "angleBisectorPair 目标需要 type、role、pointA、pointB、pointC、pointE 和 pointP。");

        var duplicates = document.Objects.GroupBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (duplicates is not null)
            throw new ProjectLoadException(path, 0, 0, "objects[].id", duplicates.Key, "对象 ID 为空或重复。", "为每个对象设置唯一且非空的 id。");
        var duplicateParameter = document.Parameters.GroupBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1);
        if (duplicateParameter is not null)
            throw new ProjectLoadException(path, 0, 0, "parameters[].id", duplicateParameter.Key, "参数 ID 为空或重复。", "为每个参数设置唯一且非空的 id。");
    }
}

public sealed class ProjectLoadException(
    string filePath, int line, int column, string field, string? objectId, string reason, string suggestion, Exception? inner = null)
    : Exception(BuildMessage(filePath, line, column, field, objectId, reason, suggestion), inner)
{
    public string FilePath { get; } = filePath;
    public int Line { get; } = line;
    public int Column { get; } = column;
    public string Field { get; } = field;
    public string? ObjectId { get; } = objectId;
    public string Reason { get; } = reason;
    public string Suggestion { get; } = suggestion;

    private static string BuildMessage(string path, int line, int column, string field, string? objectId, string reason, string suggestion) =>
        $"{path}{(line > 0 ? $"（第 {line} 行，第 {column} 列）" : "")}：字段 {field}{(objectId is null ? "" : $"，对象 {objectId}")}。{reason} 建议：{suggestion}";
}
