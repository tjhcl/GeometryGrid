using System.Text.Json.Serialization;

namespace GeometryGrid.Core.Models;

public sealed class ProjectDocument
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "1.0";
    [JsonPropertyName("canvas")] public CanvasSettings Canvas { get; set; } = new();
    [JsonPropertyName("grid")] public GridSettings Grid { get; set; } = new();
    [JsonPropertyName("styles")] public StyleSheet Styles { get; set; } = new();
    [JsonPropertyName("parameters")] public List<ParameterDefinition> Parameters { get; set; } = [];
    [JsonPropertyName("objects")] public List<GeometryObjectDefinition> Objects { get; set; } = [];
    [JsonPropertyName("solver")] public SolverSettings Solver { get; set; } = new();
    [JsonPropertyName("exports")] public ExportSettings Exports { get; set; } = new();
}

public sealed class CanvasSettings
{
    [JsonPropertyName("minX")] public double MinX { get; set; }
    [JsonPropertyName("maxX")] public double MaxX { get; set; } = 8;
    [JsonPropertyName("minY")] public double MinY { get; set; }
    [JsonPropertyName("maxY")] public double MaxY { get; set; } = 8;
    [JsonPropertyName("padding")] public double Padding { get; set; } = 28;
    [JsonPropertyName("background")] public string Background { get; set; } = "#FFFFFF";
}

public sealed class GridSettings
{
    [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    [JsonPropertyName("step")] public double Step { get; set; } = 1;
    [JsonPropertyName("color")] public string Color { get; set; } = "#202020";
    [JsonPropertyName("strokeWidth")] public double StrokeWidth { get; set; } = 1;
    [JsonPropertyName("borderWidth")] public double BorderWidth { get; set; } = 1.2;
    [JsonPropertyName("showAxes")] public bool ShowAxes { get; set; }
    [JsonPropertyName("showTickLabels")] public bool ShowTickLabels { get; set; }
}

public sealed class StyleSheet
{
    [JsonPropertyName("global")] public GeometryStyle Global { get; set; } = GeometryStyle.CreateDefault();
    [JsonPropertyName("types")] public Dictionary<string, GeometryStyle> Types { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GeometryStyle
{
    [JsonPropertyName("visible")] public bool? Visible { get; set; }
    [JsonPropertyName("strokeWidth")] public double? StrokeWidth { get; set; }
    [JsonPropertyName("strokeStyle")] public string? StrokeStyle { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("opacity")] public double? Opacity { get; set; }
    [JsonPropertyName("pointRadius")] public double? PointRadius { get; set; }
    [JsonPropertyName("zIndex")] public int? ZIndex { get; set; }

    public static GeometryStyle CreateDefault() => new()
    {
        Visible = true, StrokeWidth = 3.5, StrokeStyle = "solid", Color = "#000000",
        Opacity = 1, PointRadius = 2, ZIndex = 0
    };
}

public sealed class LabelDefinition
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("visible")] public bool Visible { get; set; } = true;
    [JsonPropertyName("fontSize")] public double FontSize { get; set; } = 45;
    [JsonPropertyName("offsetX")] public double OffsetX { get; set; } = 8;
    [JsonPropertyName("offsetY")] public double OffsetY { get; set; } = -30;
    [JsonIgnore] public double DefaultOffsetX { get; set; }
    [JsonIgnore] public double DefaultOffsetY { get; set; }
}

public sealed class ParameterDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "0";
    [JsonPropertyName("min")] public string Min { get; set; } = "0";
    [JsonPropertyName("max")] public string Max { get; set; } = "1";
}

public sealed class GeometryObjectDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("x")] public string? X { get; set; }
    [JsonPropertyName("y")] public string? Y { get; set; }
    [JsonPropertyName("pointA")] public string? PointA { get; set; }
    [JsonPropertyName("pointB")] public string? PointB { get; set; }
    [JsonPropertyName("pointC")] public string? PointC { get; set; }
    [JsonPropertyName("center")] public string? Center { get; set; }
    [JsonPropertyName("radius")] public string? Radius { get; set; }
    [JsonPropertyName("ratio")] public string? Ratio { get; set; }
    [JsonPropertyName("objectA")] public string? ObjectA { get; set; }
    [JsonPropertyName("objectB")] public string? ObjectB { get; set; }
    [JsonPropertyName("support")] public string? Support { get; set; }
    [JsonPropertyName("parameter")] public string? Parameter { get; set; }
    [JsonPropertyName("parameterX")] public string? ParameterX { get; set; }
    [JsonPropertyName("parameterY")] public string? ParameterY { get; set; }
    [JsonPropertyName("selector")] public string? Selector { get; set; }
    [JsonPropertyName("closestTo")] public string? ClosestTo { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("startAngle")] public string? StartAngle { get; set; }
    [JsonPropertyName("endAngle")] public string? EndAngle { get; set; }
    [JsonPropertyName("gridAxis")] public string? GridAxis { get; set; }
    [JsonPropertyName("gridValue")] public string? GridValue { get; set; }
    [JsonPropertyName("displayMinParameter")] public double? DisplayMinParameter { get; set; }
    [JsonPropertyName("displayMaxParameter")] public double? DisplayMaxParameter { get; set; }
    [JsonPropertyName("draggable")] public bool Draggable { get; set; }
    [JsonPropertyName("style")] public GeometryStyle? Style { get; set; }
    [JsonPropertyName("label")] public LabelDefinition? Label { get; set; }
}

public sealed class ExportSettings
{
    [JsonPropertyName("width")] public int Width { get; set; } = 1600;
    [JsonPropertyName("height")] public int Height { get; set; } = 1600;
    [JsonPropertyName("dpi")] public double Dpi { get; set; } = 144;
    [JsonPropertyName("jpegQuality")] public int JpegQuality { get; set; } = 92;
    [JsonPropertyName("transparentPng")] public bool TransparentPng { get; set; }
    [JsonPropertyName("background")] public string Background { get; set; } = "#FFFFFF";
    [JsonPropertyName("emfTextMode")] public string EmfTextMode { get; set; } = "text";
}

public sealed class SolverSettings
{
    [JsonPropertyName("targets")] public List<SolverTargetDefinition> Targets { get; set; } = [];
    [JsonPropertyName("minSteps")] public int MinSteps { get; set; } = 1;
    [JsonPropertyName("maxSteps")] public int MaxSteps { get; set; } = 5;
    [JsonPropertyName("maxSolutionsPerTarget")] public int MaxSolutionsPerTarget { get; set; } = 3;
    [JsonPropertyName("maxStates")] public long MaxStates { get; set; } = 200000;
    [JsonPropertyName("useStraightedge")] public bool UseStraightedge { get; set; } = true;
    [JsonPropertyName("useCompass")] public bool UseCompass { get; set; }
    [JsonPropertyName("useGrid")] public bool UseGrid { get; set; } = true;
    [JsonPropertyName("searchStrategy")] public int SearchStrategy { get; set; }
    [JsonPropertyName("validateParameters")] public bool ValidateParameters { get; set; }
    [JsonPropertyName("parameterSampleCount")] public int ParameterSampleCount { get; set; } = 5;
    [JsonPropertyName("cpuThreads")] public int CpuThreads { get; set; }
}

public sealed class SolverTargetDefinition
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("x")] public string X { get; set; } = "";
    [JsonPropertyName("y")] public string Y { get; set; } = "";
    [JsonPropertyName("constraint")] public SolverTargetConstraintDefinition? Constraint { get; set; }
}

public sealed class SolverTargetConstraintDefinition
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("pointA")] public string PointA { get; set; } = "";
    [JsonPropertyName("pointB")] public string PointB { get; set; } = "";
    [JsonPropertyName("pointC")] public string PointC { get; set; } = "";
    [JsonPropertyName("pointE")] public string PointE { get; set; } = "";
    [JsonPropertyName("pointP")] public string PointP { get; set; } = "";
    [JsonPropertyName("selector")] public string Selector { get; set; } = "first";
}
