using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;

namespace GeometryGrid.Core.Rendering;

public sealed class RenderScene
{
    public required CanvasSettings Canvas { get; init; }
    public required GridSettings Grid { get; init; }
    public string? AnimationCaption { get; init; }
    public List<RenderItem> Items { get; init; } = [];
}

public sealed class RenderItem
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required GeometryResult Geometry { get; init; }
    public required ResolvedStyle Style { get; init; }
    public LabelDefinition? Label { get; init; }
}

public sealed record ResolvedStyle(bool Visible, double StrokeWidth, string StrokeStyle, string Color, double Opacity, double PointRadius, int ZIndex);

public static class StyleResolver
{
    public static ResolvedStyle Resolve(ProjectDocument document, GeometryObjectDefinition item)
    {
        var global = document.Styles.Global;
        document.Styles.Types.TryGetValue(item.Type, out var type);
        var own = item.Style;
        return new ResolvedStyle(
            Pick(own?.Visible, type?.Visible, global.Visible, true),
            Pick(own?.StrokeWidth, type?.StrokeWidth, global.StrokeWidth, 3.5),
            Pick(own?.StrokeStyle, type?.StrokeStyle, global.StrokeStyle, "solid"),
            Pick(own?.Color, type?.Color, global.Color, "#000000"),
            System.Math.Clamp(Pick(own?.Opacity, type?.Opacity, global.Opacity, 1), 0, 1),
            Pick(own?.PointRadius, type?.PointRadius, global.PointRadius, 2),
            Pick(own?.ZIndex, type?.ZIndex, global.ZIndex, 0));
    }

    private static T Pick<T>(T? first, T? second, T? third, T fallback) where T : struct => first ?? second ?? third ?? fallback;
    private static string Pick(string? first, string? second, string? third, string fallback) => first ?? second ?? third ?? fallback;
}
