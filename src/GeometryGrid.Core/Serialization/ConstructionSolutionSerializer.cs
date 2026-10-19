using System.Text.Json;
using System.Text.Json.Serialization;
using GeometryGrid.Core.Geometry;

namespace GeometryGrid.Core.Serialization;

public sealed class ConstructionSolutionPackage
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "GeometryGrid.ConstructionSteps";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceProject")]
    public string? SourceProject { get; set; }

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "";

    [JsonPropertyName("optimalityProven")]
    public bool OptimalityProven { get; set; }

    [JsonPropertyName("solutions")]
    public List<ConstructionSolution> Solutions { get; set; } = [];
}

public static class ConstructionSolutionSerializer
{
    public static readonly JsonSerializerOptions Options = new(
        ProjectSerializer.Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false)
        }
    };

    public static ConstructionSolutionPackage Load(string path)
    {
        var package = JsonSerializer.Deserialize<ConstructionSolutionPackage>(
                          File.ReadAllText(path),
                          Options)
                      ?? throw new InvalidOperationException(
                          "作图步骤文件为空。");
        Validate(package);
        return package;
    }

    public static void Save(
        ConstructionSolutionPackage package,
        string path)
    {
        Validate(package);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(package, Options));
    }

    private static void Validate(ConstructionSolutionPackage package)
    {
        if (!string.Equals(
                package.Format,
                "GeometryGrid.ConstructionSteps",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"不支持的作图步骤格式：{package.Format}");
        if (package.Version != 1)
            throw new InvalidOperationException(
                $"不支持的作图步骤版本：{package.Version}");
        if (package.Solutions.Count == 0)
            throw new InvalidOperationException(
                "作图步骤文件中没有解。");
        if (package.Solutions.Any(solution =>
                solution.Steps.Any(step =>
                    string.IsNullOrWhiteSpace(step.Key) ||
                    step.PointA is null ||
                    step.PointB is null)))
            throw new InvalidOperationException(
                "作图步骤文件包含无效步骤。");
    }
}
