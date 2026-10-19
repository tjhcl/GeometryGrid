using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using GeometryGrid.App.Services;
using GeometryGrid.Core.Expressions;
using GeometryGrid.Core.Geometry;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Rendering;
using GeometryGrid.Core.Serialization;
using GeometryGrid.Export;

namespace GeometryGrid.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _dialogs;
    private readonly GeometryEngine _engine = new();
    private readonly DragRefreshGate _playbackDragRefreshGate =
        new(TimeSpan.FromMilliseconds(80));
    private readonly Stack<ProjectDocument> _undo = new();
    private readonly Stack<ProjectDocument> _redo = new();
    private bool _editing;
    private string? _selectedId;
    private ProjectDocument? _document;
    private SceneEvaluation? _evaluation;
    private string? _currentPath;
    private string _mouseCoordinateText = "坐标：—";
    private string _validationStatus = "尚未加载 JSON";
    private string _exportStatus = "";
    private ObjectEntryViewModel? _selectedObjectEntry;
    private string _selectedDefinitionJson = "";
    private bool _definitionDirty;
    private string _activeTool = DrawingTools.Select;
    private string _toolInstruction = "选择工具：单击对象进行选择";
    private readonly List<PointPick> _pointPicks = [];
    private string? _toolLineId;
    private GridLineReference? _toolGridLine;
    private double _currentToleranceWorld = .1;
    private CancellationTokenSource? _constructionSearchCancellation;
    private CancellationTokenSource? _constructionPlaybackCancellation;
    private ProjectDocument? _constructionPlaybackBaseDocument;
    private ConstructionSolution? _constructionPlaybackSourceSolution;
    private ConstructionSolution? _constructionPlaybackSolution;
    private IReadOnlyList<ConstructionPlaybackStepPlan> _constructionPlaybackPlans = [];
    private int _constructionPlaybackStep;
    private bool _constructionPlaybackHistoryCaptured;
    private bool _isConstructionPlaybackPlaying;
    private bool _isConstructionPlaybackAnimating;
    private bool _isConstructionSearching;
    private string _constructionSearchStatus = "输入目标坐标后，从当前可见图形开始搜索最少作图笔数。";
    private ConstructionSolutionEntryViewModel? _selectedConstructionSolution;
    private bool _solverUseStraightedge = true;
    private bool _solverUseCompass;
    private bool _solverUseGrid = true;
    private bool _solverValidateParameters;
    private int _solverSearchStrategy;
    private bool _isAnimationExporting;
    private CancellationTokenSource? _animationExportCancellation;

    public event EventHandler? SceneChanged;

    public MainViewModel(IFileDialogService dialogs)
    {
        _dialogs = dialogs;
        NewCommand = new RelayCommand(NewDocument);
        OpenCommand = new RelayCommand(Open);
        ReloadCommand = new RelayCommand(Reload, () => _currentPath is not null);
        SaveCommand = new RelayCommand(Save, () => _document is not null);
        SaveAsCommand = new RelayCommand(SaveAs, () => _document is not null);
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => SelectedObject is not null);
        RestoreLayoutCommand = new RelayCommand(RestoreLayout, () => _document is not null);
        ApplyCanvasSizeCommand = new RelayCommand(ApplyCanvasSize, () => _document is not null);
        ExportPngCommand = new RelayCommand(ExportPng, () => _evaluation is not null);
        ExportJpgCommand = new RelayCommand(ExportJpg, () => _evaluation is not null);
        ExportEmfCommand = new RelayCommand(ExportEmf, () => _evaluation is not null);
        ExportGifCommand = new RelayCommand(
            ExportConstructionGif,
            () => SelectedConstructionSolution is not null &&
                  !IsConstructionSearching &&
                  !IsConstructionPlaybackAnimating && !IsConstructionPlaybackPlaying && !IsAnimationExporting);
        ExportVideoCommand = new RelayCommand(async () => await ExportConstructionVideoAsync(),
            () => SelectedConstructionSolution is not null && !IsConstructionSearching &&
                  !IsConstructionPlaybackAnimating && !IsConstructionPlaybackPlaying && !IsAnimationExporting);
        CancelAnimationExportCommand = new RelayCommand(() => _animationExportCancellation?.Cancel(),
            () => IsAnimationExporting);
        RestoreSelectedLabelCommand = new RelayCommand(RestoreSelectedLabel, () => SelectedObject?.Label is not null);
        PickColorCommand = new RelayCommand(PickColor, () => SelectedObject is not null);
        SetCommonColorCommand = new RelayParameterCommand(parameter =>
        {
            if (parameter is string color) SelectedColor = color;
        }, _ => SelectedObject is not null);
        ApplyDefinitionCommand = new RelayCommand(ApplySelectedDefinition, () => SelectedObject is not null);
        AddObjectCommand = new RelayCommand(AddObject, () => _document is not null);
        SetToolCommand = new RelayParameterCommand(parameter => SetDrawingTool(parameter as string ?? DrawingTools.Select));
        CancelToolCommand = new RelayCommand(() => SetDrawingTool(DrawingTools.Select));
        StartConstructionSearchCommand = new RelayCommand(async () => await StartConstructionSearchAsync(),
            () => _evaluation is not null && !IsConstructionSearching);
        CancelConstructionSearchCommand = new RelayCommand(CancelConstructionSearch,
            () => IsConstructionSearching);
        PlayConstructionSolutionCommand = new RelayCommand(async () => await PlayConstructionSolutionAsync(),
            () => SelectedConstructionSolution is not null && !IsConstructionSearching);
        PreviousConstructionStepCommand = new RelayCommand(PreviousConstructionStep,
            () => SelectedConstructionSolution is not null &&
                  ConstructionPlaybackStep > 0 &&
                  !IsConstructionSearching &&
                  !IsConstructionPlaybackAnimating);
        NextConstructionStepCommand = new RelayCommand(async () => await NextConstructionStepAsync(),
            () => SelectedConstructionSolution is not null &&
                  ConstructionPlaybackStep < ConstructionPlaybackStepCount &&
                  !IsConstructionSearching &&
                  !IsConstructionPlaybackPlaying &&
                  !IsConstructionPlaybackAnimating);
        ImportConstructionStepsCommand = new RelayCommand(
            ImportConstructionSteps,
            () => _evaluation is not null && !IsConstructionSearching);
        ExportConstructionStepsCommand = new RelayCommand(
            ExportConstructionSteps,
            () => SelectedConstructionSolution is not null &&
                  !IsConstructionSearching);
    }

    public RelayCommand NewCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand RestoreLayoutCommand { get; }
    public RelayCommand ApplyCanvasSizeCommand { get; }
    public RelayCommand ExportPngCommand { get; }
    public RelayCommand ExportJpgCommand { get; }
    public RelayCommand ExportEmfCommand { get; }
    public RelayCommand ExportGifCommand { get; }
    public RelayCommand ExportVideoCommand { get; }
    public RelayCommand CancelAnimationExportCommand { get; }
    public bool IsAnimationExporting
    {
        get => _isAnimationExporting;
        private set
        {
            _isAnimationExporting = value;
            Raise(); Raise(nameof(AnimationExportCancelVisibility));
            ExportGifCommand.Refresh(); ExportVideoCommand.Refresh(); CancelAnimationExportCommand.Refresh();
        }
    }
    public Visibility AnimationExportCancelVisibility => IsAnimationExporting ? Visibility.Visible : Visibility.Collapsed;
    public RelayCommand RestoreSelectedLabelCommand { get; }
    public RelayCommand PickColorCommand { get; }
    public RelayParameterCommand SetCommonColorCommand { get; }
    public RelayCommand ApplyDefinitionCommand { get; }
    public RelayCommand AddObjectCommand { get; }
    public RelayParameterCommand SetToolCommand { get; }
    public RelayCommand CancelToolCommand { get; }
    public RelayCommand StartConstructionSearchCommand { get; }
    public RelayCommand CancelConstructionSearchCommand { get; }
    public RelayCommand PlayConstructionSolutionCommand { get; }
    public RelayCommand PreviousConstructionStepCommand { get; }
    public RelayCommand NextConstructionStepCommand { get; }
    public RelayCommand ImportConstructionStepsCommand { get; }
    public RelayCommand ExportConstructionStepsCommand { get; }
    public ObservableCollection<ObjectEntryViewModel> ObjectEntries { get; } = [];
    public ObservableCollection<ConstructionSolutionEntryViewModel> ConstructionSolutions { get; } = [];

    public string VersionText => "v.3.02";
    public ProjectDocument? Document => _document;
    public SceneEvaluation? Evaluation => _evaluation;
    public DrawingPreview? Preview { get; private set; }
    public ConstructionPlaybackVisual? PlaybackVisual { get; private set; }
    public GeometryObjectDefinition? SelectedObject => _document?.Objects.FirstOrDefault(o => o.Id == _selectedId);
    public bool HasSelection => SelectedObject is not null;
    public Visibility NoSelectionVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SelectionVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;
    public string SelectionStatus => SelectedObject is null
        ? "对象：未选择"
        : string.IsNullOrWhiteSpace(UserDisplayName(SelectedObject))
            ? $"对象：未命名 · {SelectedObject.Id} · {SelectedObject.Type}"
            : $"对象：{UserDisplayName(SelectedObject)} · {SelectedObject.Id} · {SelectedObject.Type}";
    public string ParameterStatus => _document is null ? "参数：—" : $"参数：{string.Join(", ", _document.Parameters.Select(p => $"{p.Id}={p.Value}"))}";
    public string MouseCoordinateText { get => _mouseCoordinateText; private set { _mouseCoordinateText = value; Raise(); } }
    public string ValidationStatus { get => _validationStatus; private set { _validationStatus = value; Raise(); } }
    public string ExportStatus { get => _exportStatus; private set { _exportStatus = value; Raise(); } }
    public string ActiveTool { get => _activeTool; private set { _activeTool = value; Raise(); } }
    public string ToolInstruction { get => _toolInstruction; private set { _toolInstruction = value; Raise(); } }
    public string CanvasWidthInput { get; set; } = "8";
    public string CanvasHeightInput { get; set; } = "8";
    public string ConstructionTargetsInput { get; set; } = "";
    public string ConstructionMinStepsInput { get; set; } = "1";
    public string ConstructionMaxStepsInput { get; set; } = "5";
    public string ConstructionMaxSolutionsInput { get; set; } = "3";
    public string ConstructionMaxStatesInput { get; set; } = "200000";
    public string ConstructionParameterSamplesInput { get; set; } = "5";
    public string ConstructionCpuThreadsInput { get; set; } =
        Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
    public bool SolverUseStraightedge
    {
        get => _solverUseStraightedge;
        set { _solverUseStraightedge = value; Raise(); }
    }
    public bool SolverUseCompass
    {
        get => _solverUseCompass;
        set { _solverUseCompass = value; Raise(); }
    }
    public bool SolverUseGrid
    {
        get => _solverUseGrid;
        set { _solverUseGrid = value; Raise(); }
    }
    public bool SolverValidateParameters
    {
        get => _solverValidateParameters;
        set { _solverValidateParameters = value; Raise(); }
    }
    public double SolverSearchStrategy
    {
        get => _solverSearchStrategy;
        set
        {
            _solverSearchStrategy = System.Math.Clamp((int)System.Math.Round(value), 0, 3);
            Raise();
            Raise(nameof(SolverSearchStrategyName), nameof(SolverSearchStrategyDescription));
        }
    }
    public string SolverSearchStrategyName => _solverSearchStrategy switch
    {
        1 => "共线反推",
        2 => "启发式搜索",
        3 => "纯穷举",
        _ => "自动选择"
    };
    public string SolverSearchStrategyDescription => _solverSearchStrategy switch
    {
        1 => "优先利用共线、已知载体和目标闭包，复杂多目标题最快。",
        2 => "按接近目标程度排序候选，速度与通用性较均衡。",
        3 => "不使用题型捷径，覆盖最朴素但通常最慢。",
        _ => "依次尝试共线反推、启发式搜索与完整逐层穷举。"
    };
    public bool IsConstructionSearching
    {
        get => _isConstructionSearching;
        private set
        {
            _isConstructionSearching = value;
            Raise();
            StartConstructionSearchCommand.Refresh();
            CancelConstructionSearchCommand.Refresh();
            ExportGifCommand.Refresh();
            ExportVideoCommand.Refresh();
            ImportConstructionStepsCommand.Refresh();
            ExportConstructionStepsCommand.Refresh();
            RaisePlaybackCommands();
        }
    }
    public string ConstructionSearchStatus
    {
        get => _constructionSearchStatus;
        private set { _constructionSearchStatus = value; Raise(); }
    }
    public ConstructionSolutionEntryViewModel? SelectedConstructionSolution
    {
        get => _selectedConstructionSolution;
        set
        {
            if (!ReferenceEquals(_selectedConstructionSolution, value))
                ResetConstructionPlaybackSession(restoreBase: true);
            _selectedConstructionSolution = value;
            Raise();
            RaisePlaybackState();
            ExportGifCommand.Refresh();
            ExportVideoCommand.Refresh();
            ExportConstructionStepsCommand.Refresh();
        }
    }
    public int ConstructionPlaybackStep
    {
        get => _constructionPlaybackStep;
        private set
        {
            _constructionPlaybackStep = value;
            Raise();
            Raise(nameof(ConstructionPlaybackProgressText));
            RaisePlaybackCommands();
        }
    }
    public int ConstructionPlaybackStepCount =>
        SelectedConstructionSolution?.Solution.StepCount ?? 0;
    public string ConstructionPlaybackProgressText =>
        SelectedConstructionSolution is null
            ? "请选择一个解"
            : $"步骤 {ConstructionPlaybackStep} / {ConstructionPlaybackStepCount}";
    public bool IsConstructionPlaybackPlaying
    {
        get => _isConstructionPlaybackPlaying;
        private set
        {
            _isConstructionPlaybackPlaying = value;
            Raise();
            Raise(nameof(ConstructionPlaybackPlayText));
            RaisePlaybackCommands();
        }
    }
    public bool IsConstructionPlaybackAnimating
    {
        get => _isConstructionPlaybackAnimating;
        private set
        {
            _isConstructionPlaybackAnimating = value;
            Raise();
            RaisePlaybackCommands();
        }
    }
    public string ConstructionPlaybackPlayText =>
        IsConstructionPlaybackPlaying ? "Ⅱ  暂停" :
        ConstructionPlaybackStep == 0 ? "▶  播放" : "▶  继续";
    public string CanvasOriginText => _document is null
        ? "左下角：—"
        : $"左下角保持 ({_document.Canvas.MinX:G6}, {_document.Canvas.MinY:G6})";

    public string SelectedDisplayName
    {
        get => SelectedObject is null ? "" : UserDisplayName(SelectedObject);
        set
        {
            if (SelectedObject is null || string.IsNullOrWhiteSpace(value)) return;
            var name = value.Trim();
            Apply(() =>
            {
                SelectedObject.Name = name;
                SelectedObject.Label ??= NewLabel(name);
                SelectedObject.Label.Text = name;
                SelectedObject.Label.Visible = true;
            });
        }
    }
    public bool IsSelectTool => ActiveTool == DrawingTools.Select;
    public bool IsPointTool => ActiveTool == DrawingTools.Point;
    public bool IsSegmentTool => ActiveTool == DrawingTools.Segment;
    public bool IsRayTool => ActiveTool == DrawingTools.Ray;
    public bool IsLineTool => ActiveTool == DrawingTools.Line;
    public bool IsMidpointTool => ActiveTool == DrawingTools.Midpoint;
    public bool IsPerpendicularTool => ActiveTool == DrawingTools.Perpendicular;
    public bool IsParallelTool => ActiveTool == DrawingTools.Parallel;
    public bool IsLineRelationTool => ActiveTool is DrawingTools.Perpendicular or DrawingTools.Parallel;
    public bool RelationNeedsLine => IsLineRelationTool && _pointPicks.Count > 0;
    public bool RelationNeedsPoint => IsLineRelationTool && (_toolLineId is not null || _toolGridLine is not null);
    public string? PendingRelationLineId => IsLineRelationTool ? _toolLineId : null;
    public GridLineReference? PendingRelationGridLine => IsLineRelationTool ? _toolGridLine : null;
    public bool IsCircleTool => ActiveTool is DrawingTools.CircleDiameter or DrawingTools.CircleRadiusSegment
        or DrawingTools.CircleCenterRadius or DrawingTools.CircleCenterDistance or DrawingTools.CircleThreePoints;
    public bool SelectedVisible { get => ResolvedStyle?.Visible ?? false; set => SetStyle(s => s.Visible = value); }
    public double SelectedStrokeWidth { get => ResolvedStyle?.StrokeWidth ?? 0; set => SetStyle(s => s.StrokeWidth = System.Math.Max(.1, value)); }
    public string SelectedStrokeStyle { get => ResolvedStyle?.StrokeStyle ?? "solid"; set => SetStyle(s => s.StrokeStyle = value); }
    public string SelectedColor
    {
        get => ResolvedStyle?.Color ?? "#000000";
        set
        {
            var color = value ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
            {
                ValidationStatus = "颜色格式应为 #RRGGBB 或 #AARRGGBB。";
                Raise();
                return;
            }
            SetStyle(s => s.Color = color.ToUpperInvariant());
        }
    }
    public double SelectedOpacity { get => ResolvedStyle?.Opacity ?? 1; set => SetStyle(s => s.Opacity = System.Math.Clamp(value, 0, 1)); }
    public double SelectedPointRadius { get => ResolvedStyle?.PointRadius ?? 2; set => SetStyle(s => s.PointRadius = System.Math.Max(.5, value)); }
    public int SelectedZIndex { get => ResolvedStyle?.ZIndex ?? 0; set => SetStyle(s => s.ZIndex = value); }
    public bool SelectedLabelVisible { get => SelectedObject?.Label?.Visible ?? false; set => SetLabel(l => l.Visible = value); }
    public double SelectedFontSize { get => SelectedObject?.Label?.FontSize ?? 45; set => SetLabel(l => l.FontSize = System.Math.Max(6, value)); }
    public double SelectedOffsetX { get => SelectedObject?.Label?.OffsetX ?? 0; set => SetLabel(l => l.OffsetX = value); }
    public double SelectedOffsetY { get => SelectedObject?.Label?.OffsetY ?? 0; set => SetLabel(l => l.OffsetY = value); }
    private ResolvedStyle? ResolvedStyle => _document is not null && SelectedObject is { } selected ? StyleResolver.Resolve(_document, selected) : null;
    public ObjectEntryViewModel? SelectedObjectEntry
    {
        get => _selectedObjectEntry;
        set
        {
            if (ReferenceEquals(_selectedObjectEntry, value)) return;
            _selectedObjectEntry = value;
            Raise();
            if (value is not null && value.Id != _selectedId) Select(value.Id);
        }
    }
    public string SelectedDefinitionJson
    {
        get => _selectedDefinitionJson;
        set
        {
            if (_selectedDefinitionJson == value) return;
            _selectedDefinitionJson = value;
            _definitionDirty = true;
            Raise();
        }
    }

    public void OpenDefaultExample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "examples", "tianjin_2026_q18.json");
            if (File.Exists(candidate)) { Load(candidate); return; }
            directory = directory.Parent;
        }
        try
        {
            using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream(
                "GeometryGrid.App.Resources.tianjin_2026_q18.json")
                ?? throw new InvalidOperationException("内置示例资源不存在。");
            using var reader = new StreamReader(stream);
            _document = ProjectSerializer.LoadJson(reader.ReadToEnd(), "内置示例 tianjin_2026_q18.json");
            _currentPath = null;
            _undo.Clear();
            _redo.Clear();
            _selectedId = null;
            _definitionDirty = false;
            SetDrawingTool(DrawingTools.Select);
            LoadSolverSettingsFromDocument();
            Recalculate();
            ExportStatus = "已加载内置示例";
        }
        catch (Exception ex)
        {
            ValidationStatus = $"内置示例加载失败：{ex.Message}";
        }
    }

    public void Select(string? id)
    {
        _selectedId = id;
        _selectedObjectEntry = ObjectEntries.FirstOrDefault(entry => entry.Id == id);
        Raise(nameof(SelectedObjectEntry));
        UpdateDefinitionEditor(true);
        RaiseSelection();
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMouseCoordinate(Point2 point) => MouseCoordinateText = $"坐标：x={point.X:0.###}, y={point.Y:0.###}";

    public void HandleCanvasClick(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        if (_document is null || _evaluation is null) return;
        _currentToleranceWorld = System.Math.Max(toleranceWorld, GeometryAlgorithms.Epsilon);
        var hitId = hitIds.FirstOrDefault(IsPoint) ?? hitIds.FirstOrDefault();
        if (ActiveTool == DrawingTools.Select)
        {
            Select(hitId);
            return;
        }

        switch (ActiveTool)
        {
            case DrawingTools.Point:
                HandleSmartPoint(world, hitIds, gridLines, toleranceWorld);
                break;

            case DrawingTools.Segment:
                CollectTwoPoints(world, hitIds, gridLines, toleranceWorld, "segment", "segment", "线段");
                break;

            case DrawingTools.Ray:
                CollectTwoPoints(world, hitIds, gridLines, toleranceWorld, "ray", "ray", "射线");
                break;

            case DrawingTools.Line:
                CollectTwoPoints(world, hitIds, gridLines, toleranceWorld, "lineThroughPoints", "line", "直线");
                break;

            case DrawingTools.Midpoint:
                CollectTwoPoints(world, hitIds, gridLines, toleranceWorld, "midpoint", "midpoint", "中点", resultIsPoint: true);
                break;

            case DrawingTools.Perpendicular:
                HandleLineRelation(world, hitIds, gridLines, toleranceWorld, perpendicular: true);
                break;

            case DrawingTools.Parallel:
                HandleLineRelation(world, hitIds, gridLines, toleranceWorld, perpendicular: false);
                break;

            case DrawingTools.CircleDiameter:
                HandleDiameterCircle(world, hitIds, gridLines, toleranceWorld);
                break;

            case DrawingTools.CircleRadiusSegment:
                HandleRadiusSegmentCircle(world, hitIds, gridLines, toleranceWorld);
                break;

            case DrawingTools.CircleCenterRadius:
                HandleCenterRadiusCircle(world, hitIds, gridLines, toleranceWorld);
                break;

            case DrawingTools.CircleCenterDistance:
                HandleCenterDistanceCircle(world, hitIds, gridLines, toleranceWorld);
                break;

            case DrawingTools.CircleThreePoints:
                HandleThreePointCircle(world, hitIds, gridLines, toleranceWorld);
                break;

        }
    }

    public void CancelDrawing() => SetDrawingTool(DrawingTools.Select);

    public void UpdateDrawingPreview(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        if (_document is null || _evaluation is null || ActiveTool == DrawingTools.Select)
        {
            Preview = null;
            return;
        }

        var candidate = ResolveConstructionCandidate(world, hitIds, gridLines, toleranceWorld);
        LineGeometry? line = null;
        CircleGeometry? circle = null;
        if (IsLineRelationTool)
        {
            var relationSource = ResolveRelationPreviewSource(hitIds, gridLines);
            var throughPoint = _pointPicks.Count > 0 ? _pointPicks[0].World : candidate;
            if (relationSource is not null &&
                (_pointPicks.Count > 0 || _toolLineId is not null || _toolGridLine is not null))
            {
                var direction = ActiveTool == DrawingTools.Perpendicular
                    ? new Vector2(-relationSource.Direction.Y, relationSource.Direction.X)
                    : relationSource.Direction;
                if (direction.Length > GeometryAlgorithms.Epsilon)
                    line = new LineGeometry(throughPoint, direction);
            }
        }
        if (_pointPicks.Count > 0 && !IsLineRelationTool)
        {
            var first = _pointPicks[0].World;
            var direction = candidate - first;
            if (direction.Length > GeometryAlgorithms.Epsilon)
            {
                line = ActiveTool switch
                {
                    DrawingTools.Segment or DrawingTools.Midpoint => new LineGeometry(first, direction, 0, 1),
                    DrawingTools.Ray => new LineGeometry(first, direction, 0),
                    DrawingTools.Line => new LineGeometry(first, direction),
                    _ => null
                };
                if (ActiveTool == DrawingTools.CircleRadiusSegment)
                    circle = new CircleGeometry(first, direction.Length);
                else if (ActiveTool == DrawingTools.CircleDiameter)
                {
                    var center = new Point2((first.X + candidate.X) / 2, (first.Y + candidate.Y) / 2);
                    circle = new CircleGeometry(center, first.DistanceTo(candidate) / 2);
                }
            }

            if (ActiveTool == DrawingTools.CircleThreePoints && _pointPicks.Count >= 2)
                circle = Circumcircle(_pointPicks[0].World, _pointPicks[1].World, candidate);
            else if (ActiveTool == DrawingTools.CircleCenterDistance && _pointPicks.Count >= 2)
                circle = new CircleGeometry(_pointPicks[0].World, _pointPicks[1].World.DistanceTo(candidate));
        }
        Preview = new DrawingPreview(candidate, line, circle);
    }

    private void SetDrawingTool(string tool)
    {
        ActiveTool = tool;
        _pointPicks.Clear();
        _toolLineId = null;
        _toolGridLine = null;
        Preview = null;
        ToolInstruction = DefaultToolInstruction(tool);
        Raise(nameof(IsSelectTool), nameof(IsPointTool), nameof(IsSegmentTool), nameof(IsRayTool), nameof(IsLineTool),
            nameof(IsMidpointTool), nameof(IsPerpendicularTool), nameof(IsParallelTool), nameof(IsLineRelationTool),
            nameof(RelationNeedsLine), nameof(RelationNeedsPoint), nameof(PendingRelationLineId),
            nameof(PendingRelationGridLine), nameof(IsCircleTool));
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string DefaultToolInstruction(string tool) => tool switch
    {
        DrawingTools.Point => "点工具：点击空白、对象或交点；已有点不会重复创建",
        DrawingTools.Segment => "线段工具：依次点击两个点；点击空白可同时新建点",
        DrawingTools.Ray => "射线工具：依次点击起点、经过点",
        DrawingTools.Line => "直线工具：依次点击两个点",
        DrawingTools.Midpoint => "中点工具：依次点击两个点",
        DrawingTools.Perpendicular => "垂直工具：选择点和几何线/背景格线，顺序不限；移动鼠标可预览",
        DrawingTools.Parallel => "平行工具：选择点和几何线/背景格线，顺序不限；移动鼠标可预览",
        DrawingTools.CircleDiameter => "直径圆：点击一条线段，或依次点击两个端点",
        DrawingTools.CircleRadiusSegment => "半径线段圆：点击圆心，再点击圆上一点；移动鼠标可实时预览",
        DrawingTools.CircleCenterRadius => "圆心+半径：点击圆心，再输入半径表达式",
        DrawingTools.CircleCenterDistance => "圆心+两点距离：先选圆心，再选择两个点作为半径长度",
        DrawingTools.CircleThreePoints => "三点圆：依次点击三个不共线点",
        _ => "选择工具：单击对象进行选择"
    };

    private void HandleSmartPoint(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        var directlyHitPoint = hitIds.FirstOrDefault(IsPoint)
            ?? FindExactPoint(world);
        if (directlyHitPoint is { } existing)
        {
            Select(existing);
            ToolInstruction = "此处已有点，已选中原点；不会重复创建";
            return;
        }

        var geometries = hitIds.Where(IsIntersectable).Distinct(StringComparer.Ordinal).ToList();
        var candidates = new List<SmartIntersection>();
        for (var i = 0; i < geometries.Count; i++)
        {
            for (var j = i + 1; j < geometries.Count; j++)
            {
                var points = GeometryAlgorithms.Intersections(
                    _evaluation!.Results[geometries[i]],
                    _evaluation.Results[geometries[j]]).ToList();
                if (points.Count == 0) continue;
                var nearest = points.MinBy(point => point.DistanceTo(world));
                if (nearest.DistanceTo(world) <= toleranceWorld * 1.8)
                    candidates.Add(new SmartIntersection(nearest, geometries[i], geometries[j], null,
                        SelectorNearest(points, world)));
            }
        }

        foreach (var geometryId in geometries)
        {
            foreach (var grid in gridLines)
            {
                var synthetic = GridGeometry(grid);
                var points = GeometryAlgorithms.Intersections(_evaluation!.Results[geometryId], synthetic).ToList();
                if (points.Count == 0) continue;
                var nearest = points.MinBy(point => point.DistanceTo(world));
                if (nearest.DistanceTo(world) <= toleranceWorld * 1.8)
                    candidates.Add(new SmartIntersection(nearest, geometryId, null, grid, SelectorNearest(points, world)));
            }
        }

        if (candidates.Count > 0)
        {
            var candidate = candidates.MinBy(item => item.Point.DistanceTo(world));
            if (FindExactPoint(candidate.Point) is { } duplicate)
            {
                Select(duplicate);
                ToolInstruction = "该交点已经存在，已选中原交点";
                return;
            }
            if (candidate.Grid is { } grid)
                CreateIntersection(candidate.ObjectA, null, grid.Axis, FormatNumber(grid.Value), candidate.Selector);
            else
                CreateIntersection(candidate.ObjectA, candidate.ObjectB, null, null, candidate.Selector);
            ToolInstruction = "交点已创建；点工具仍保持启用";
            return;
        }

        if (geometries.Count > 0)
        {
            CreatePointOnObject(world, geometries[0], toleranceWorld);
            return;
        }

        var verticals = gridLines.Where(line => line.Axis == "x").ToList();
        var horizontals = gridLines.Where(line => line.Axis == "y").ToList();
        if (verticals.Count > 0 && horizontals.Count > 0)
        {
            var vertical = verticals.MinBy(line => System.Math.Abs(line.Value - world.X));
            var horizontal = horizontals.MinBy(line => System.Math.Abs(line.Value - world.Y));
            var intersection = new Point2(vertical.Value, horizontal.Value);
            if (intersection.DistanceTo(world) <= toleranceWorld * 1.2)
            {
                if (FindExactPoint(intersection) is { } duplicate)
                {
                    Select(duplicate);
                    ToolInstruction = "该格线交点已经存在，已选中原点";
                    return;
                }
                string? gridPointId = null;
                Apply(() =>
                {
                    var point = CreateFixedPoint(intersection, draggable: false);
                    _document!.Objects.Add(point);
                    gridPointId = point.Id;
                });
                Select(gridPointId);
                ToolInstruction = $"已创建两条格线的固定交点 ({vertical.Value:G6}, {horizontal.Value:G6})";
                return;
            }
        }

        if (gridLines.Count > 0)
        {
            var nearestGrid = gridLines.MinBy(line => line.Axis == "x"
                ? System.Math.Abs(line.Value - world.X)
                : System.Math.Abs(line.Value - world.Y));
            CreatePointOnGridLine(world, nearestGrid, toleranceWorld);
            return;
        }

        var snapped = world;
        if (FindExactPoint(snapped) is { } snappedDuplicate)
        {
            Select(snappedDuplicate);
            ToolInstruction = "此处已有点，已选中原点；不会重复创建";
            return;
        }

        string? createdId = null;
        Apply(() =>
        {
            var point = CreateFixedPoint(snapped);
            _document!.Objects.Add(point);
            createdId = point.Id;
        });
        Select(createdId);
        ToolInstruction = "点已创建；可继续点击空白、对象或交点";
    }

    private void CreatePointOnGridLine(Point2 world, GridLineReference grid, double toleranceWorld)
    {
        var parameterValue = grid.Axis == "x" ? world.Y : world.X;
        var projected = grid.Axis == "x"
            ? new Point2(grid.Value, parameterValue)
            : new Point2(parameterValue, grid.Value);
        if (FindExactPoint(projected) is { } duplicate)
        {
            Select(duplicate);
            ToolInstruction = "格线上的该点已经存在，已选中原点";
            return;
        }

        string? createdId = null;
        Apply(() =>
        {
            var parameterId = NextParameterId();
            var canvas = _document!.Canvas;
            _document.Parameters.Add(new ParameterDefinition
            {
                Id = parameterId,
                Value = FormatNumber(parameterValue),
                Min = FormatNumber(grid.Axis == "x" ? canvas.MinY : canvas.MinX),
                Max = FormatNumber(grid.Axis == "x" ? canvas.MaxY : canvas.MaxX)
            });
            var point = CreateObject("pointOnGridLine", "point", true);
            point.GridAxis = grid.Axis;
            point.GridValue = FormatNumber(grid.Value);
            point.Parameter = parameterId;
            point.Draggable = true;
            _document.Objects.Add(point);
            createdId = point.Id;
        });
        Select(createdId);
        ToolInstruction = $"已在格线 {grid} 上创建可拖动点";
    }

    private void CreatePointOnObject(Point2 world, string supportId, double toleranceWorld)
    {
        var support = _evaluation!.Results[supportId];
        string type;
        double parameterValue;
        string min;
        string max;
        Point2 projected;
        if (support.Line is { } line)
        {
            var squared = Vector2.Dot(line.Direction, line.Direction);
            if (squared < GeometryAlgorithms.Epsilon) return;
            parameterValue = Vector2.Dot(world - line.Origin, line.Direction) / squared;
            parameterValue = System.Math.Clamp(parameterValue, line.MinParameter, line.MaxParameter);
            projected = line.At(parameterValue);
            type = FindObject(supportId)?.Type == "segment" ? "pointOnSegment" : "pointOnLine";
            min = double.IsFinite(line.MinParameter) ? FormatNumber(line.MinParameter) : "-100";
            max = double.IsFinite(line.MaxParameter) ? FormatNumber(line.MaxParameter) : "100";
        }
        else if (support.Circle is { } circle)
        {
            parameterValue = GeometryAlgorithms.NormalizeAngle(
                System.Math.Atan2(world.Y - circle.Center.Y, world.X - circle.Center.X)) / (2 * System.Math.PI);
            projected = new Point2(
                circle.Center.X + circle.Radius * System.Math.Cos(parameterValue * 2 * System.Math.PI),
                circle.Center.Y + circle.Radius * System.Math.Sin(parameterValue * 2 * System.Math.PI));
            type = "pointOnCircle";
            min = "0";
            max = "1";
        }
        else if (support.Arc is { } arc)
        {
            var angle = System.Math.Atan2(world.Y - arc.Center.Y, world.X - arc.Center.X);
            parameterValue = GeometryAlgorithms.NormalizeAngle(angle - arc.StartAngleRadians) / System.Math.Abs(arc.SweepRadians);
            parameterValue = System.Math.Clamp(parameterValue, 0, 1);
            var projectedAngle = arc.StartAngleRadians + parameterValue * arc.SweepRadians;
            projected = new Point2(
                arc.Center.X + arc.Radius * System.Math.Cos(projectedAngle),
                arc.Center.Y + arc.Radius * System.Math.Sin(projectedAngle));
            type = "pointOnArc";
            min = "0";
            max = "1";
        }
        else return;

        if (FindExactPoint(projected) is { } duplicate)
        {
            Select(duplicate);
            ToolInstruction = "对象上的该点已经存在，已选中原点";
            return;
        }

        string? createdId = null;
        Apply(() =>
        {
            var parameterId = NextParameterId();
            _document!.Parameters.Add(new ParameterDefinition
            {
                Id = parameterId,
                Value = FormatNumber(parameterValue),
                Min = min,
                Max = max
            });
            var point = CreateObject(type, "point", true);
            point.Support = supportId;
            point.Parameter = parameterId;
            point.Draggable = true;
            _document.Objects.Add(point);
            createdId = point.Id;
        });
        Select(createdId);
        ToolInstruction = "已在对象上创建受约束点；拖动时会保持在对象上";
    }

    private GeometryResult GridGeometry(GridLineReference grid) => new()
    {
        ObjectId = $"grid:{grid}",
        Type = "gridLine",
        IsValid = true,
        Line = grid.Axis == "x"
            ? new LineGeometry(new Point2(grid.Value, 0), new Vector2(0, 1))
            : new LineGeometry(new Point2(0, grid.Value), new Vector2(1, 0))
    };

    private bool TryAddSmartPointPick(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        HandleSmartPoint(world, hitIds, gridLines, toleranceWorld);
        if (_selectedId is null || _evaluation?.Results.GetValueOrDefault(_selectedId)?.Point is not { } point)
        {
            ToolInstruction = "此步骤需要点；请点击已有点、对象、交点或画布空白位置";
            return false;
        }
        var pick = new PointPick(_selectedId, point);
        if (_pointPicks.Any(existing => existing.ExistingId == pick.ExistingId))
        {
            ToolInstruction = "不能重复选择同一个点，请选择另一个点";
            return false;
        }
        _pointPicks.Add(pick);
        Preview = null;
        return true;
    }

    private Point2 ResolveConstructionCandidate(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        foreach (var id in hitIds)
            if (_evaluation!.Results.GetValueOrDefault(id)?.Point is { } point)
                return point;

        var geometries = hitIds.Where(IsIntersectable).Distinct(StringComparer.Ordinal).ToList();
        var intersections = new List<Point2>();
        for (var i = 0; i < geometries.Count; i++)
            for (var j = i + 1; j < geometries.Count; j++)
                intersections.AddRange(GeometryAlgorithms.Intersections(
                    _evaluation!.Results[geometries[i]], _evaluation.Results[geometries[j]]));
        foreach (var geometryId in geometries)
            foreach (var grid in gridLines)
                intersections.AddRange(GeometryAlgorithms.Intersections(
                    _evaluation!.Results[geometryId], GridGeometry(grid)));
        if (intersections.Count > 0)
        {
            var nearest = intersections.MinBy(point => point.DistanceTo(world));
            if (nearest.DistanceTo(world) <= toleranceWorld * 1.8) return nearest;
        }

        if (geometries.Count > 0)
        {
            var support = _evaluation!.Results[geometries[0]];
            if (support.Line is { } line)
            {
                var squared = Vector2.Dot(line.Direction, line.Direction);
                if (squared > GeometryAlgorithms.Epsilon)
                {
                    var t = Vector2.Dot(world - line.Origin, line.Direction) / squared;
                    return line.At(System.Math.Clamp(t, line.MinParameter, line.MaxParameter));
                }
            }
            var circle = support.Circle ?? (support.Arc is { } arc
                ? new CircleGeometry(arc.Center, arc.Radius)
                : null);
            if (circle is not null)
            {
                var direction = world - circle.Center;
                if (direction.Length > GeometryAlgorithms.Epsilon)
                    return circle.Center + direction.Normalized * circle.Radius;
            }
        }

        var vertical = gridLines.Where(line => line.Axis == "x")
            .OrderBy(line => System.Math.Abs(line.Value - world.X)).FirstOrDefault();
        var horizontal = gridLines.Where(line => line.Axis == "y")
            .OrderBy(line => System.Math.Abs(line.Value - world.Y)).FirstOrDefault();
        if (vertical.Axis == "x") world = world with { X = vertical.Value };
        if (horizontal.Axis == "y") world = world with { Y = horizontal.Value };
        return world;
    }

    private static CircleGeometry? Circumcircle(Point2 a, Point2 b, Point2 c)
    {
        var determinant = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (System.Math.Abs(determinant) < GeometryAlgorithms.Epsilon) return null;
        var aa = a.X * a.X + a.Y * a.Y;
        var bb = b.X * b.X + b.Y * b.Y;
        var cc = c.X * c.X + c.Y * c.Y;
        var center = new Point2(
            (aa * (b.Y - c.Y) + bb * (c.Y - a.Y) + cc * (a.Y - b.Y)) / determinant,
            (aa * (c.X - b.X) + bb * (a.X - c.X) + cc * (b.X - a.X)) / determinant);
        return new CircleGeometry(center, center.DistanceTo(a));
    }

    private void CollectTwoPoints(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld,
        string type,
        string idPrefix,
        string friendlyName,
        bool resultIsPoint = false)
    {
        if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        if (_pointPicks.Count == 1)
        {
            ToolInstruction = $"{friendlyName}：已选择第一个点，请选择第二个点";
            return;
        }
        var picks = _pointPicks.ToList();
        _pointPicks.Clear();
        string? createdId = null;
        Apply(() =>
        {
            var ids = MaterializePoints(picks);
            var item = CreateObject(type, idPrefix, resultIsPoint);
            item.PointA = ids[0];
            item.PointB = ids[1];
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = $"{friendlyName}已创建；可继续选择两个点";
    }

    private void HandleLineRelation(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld,
        bool perpendicular)
    {
        var toolName = perpendicular ? "垂直工具" : "平行工具";
        var resultName = perpendicular ? "垂线" : "平行线";
        var resultType = perpendicular ? "perpendicularLine" : "parallelLine";
        var idPrefix = perpendicular ? "perpendicular" : "parallel";
        var hitLineId = hitIds.FirstOrDefault(IsLine);
        var hitPointId = hitIds.FirstOrDefault(IsPoint);
        GridLineReference? hitGridLine = gridLines.Count > 0 ? gridLines[0] : null;

        // A point has already been fixed: from now on this step only accepts a line.
        // This prevents another nearby point from stealing the second click.
        if (_pointPicks.Count > 0)
        {
            if (hitLineId is null && hitGridLine is null)
            {
                ToolInstruction = $"{toolName}：点已确定，现在请选择几何直线、线段、射线或背景格线";
                return;
            }
            _toolLineId = hitLineId;
            _toolGridLine = hitLineId is null ? hitGridLine : null;
        }
        // A line has already been fixed: the remaining click is interpreted as a point
        // and may create a free/constrained/intersection point just like the point tool.
        else if (_toolLineId is not null || _toolGridLine is not null)
        {
            if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld))
            {
                ToolInstruction = $"{toolName}：基准直线已确定，现在请选择或创建一个点";
                return;
            }
        }
        // On the first click, an explicitly hit point wins over a line crossing it.
        else if (hitPointId is not null)
        {
            if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        }
        else if (hitLineId is not null)
        {
            _toolLineId = hitLineId;
        }
        else if (hitGridLine is not null)
        {
            _toolGridLine = hitGridLine;
        }
        else
        {
            if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        }

        if ((_toolLineId is null && _toolGridLine is null) || _pointPicks.Count == 0)
        {
            ToolInstruction = _toolLineId is null && _toolGridLine is null
                ? $"{toolName}：点已确定，现在请选择几何直线、线段、射线或背景格线"
                : $"{toolName}：基准线已确定，现在请选择或创建一个点";
            RaiseRelationState();
            return;
        }

        var pointPick = _pointPicks[0];
        var sourceLine = _toolLineId;
        var sourceGrid = _toolGridLine;
        _pointPicks.Clear();
        _toolLineId = null;
        _toolGridLine = null;
        string? createdId = null;
        Apply(() =>
        {
            var pointId = MaterializePoints([pointPick])[0];
            var item = CreateObject(resultType, idPrefix, false);
            item.PointA = pointId;
            item.ObjectA = sourceLine;
            item.GridAxis = sourceGrid?.Axis;
            item.GridValue = sourceGrid is null ? null : FormatNumber(sourceGrid.Value.Value);
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        RaiseRelationState();
        ToolInstruction = $"{resultName}已创建；可继续按任意顺序选择基准线和点";
    }

    private LineGeometry? ResolveRelationPreviewSource(
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines)
    {
        if (_toolLineId is not null)
            return _evaluation?.Results.GetValueOrDefault(_toolLineId)?.Line;
        if (_toolGridLine is { } selectedGrid)
            return GridGeometry(selectedGrid).Line;
        var hoveredLineId = hitIds.FirstOrDefault(IsLine);
        if (hoveredLineId is not null)
            return _evaluation?.Results.GetValueOrDefault(hoveredLineId)?.Line;
        return gridLines.Count > 0 ? GridGeometry(gridLines[0]).Line : null;
    }

    private void RaiseRelationState()
    {
        Raise(nameof(RelationNeedsLine), nameof(RelationNeedsPoint),
            nameof(PendingRelationLineId), nameof(PendingRelationGridLine));
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDiameterCircle(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        var hitId = hitIds.FirstOrDefault(IsPoint) ?? hitIds.FirstOrDefault();
        if (_pointPicks.Count == 0 && hitId is not null && FindObject(hitId) is { Type: "segment", PointA: not null, PointB: not null } segment)
        {
            CreateCircleByPoints("circleByDiameter", segment.PointA, segment.PointB, null);
            ToolInstruction = "直径圆已由所选线段创建；可继续选择线段或两个点";
            return;
        }
        if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        if (_pointPicks.Count < 2)
        {
            ToolInstruction = "直径圆：已选择一个端点，请选择另一个端点";
            return;
        }
        var picks = _pointPicks.ToList();
        _pointPicks.Clear();
        string? createdId = null;
        Apply(() =>
        {
            var ids = MaterializePoints(picks);
            var item = CreateObject("circleByDiameter", "circle", false);
            item.PointA = ids[0];
            item.PointB = ids[1];
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = "直径圆已创建；可继续选择线段或两个点";
    }

    private void HandleRadiusSegmentCircle(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        if (_pointPicks.Count == 1)
        {
            ToolInstruction = "已选择圆心；移动鼠标预览半径，再点击圆上一点";
            return;
        }
        var picks = _pointPicks.ToList();
        _pointPicks.Clear();
        string? createdId = null;
        Apply(() =>
        {
            var ids = MaterializePoints(picks);
            var item = CreateObject("circleByCenterPoint", "circle", false);
            item.Center = ids[0];
            item.PointA = ids[1];
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = "半径线段圆已创建；可继续点击圆心作圆";
    }

    private void HandleCenterRadiusCircle(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        HandleSmartPoint(world, hitIds, gridLines, toleranceWorld);
        if (_selectedId is null || _evaluation?.Results.GetValueOrDefault(_selectedId)?.Point is not { } centerPoint)
        {
            ToolInstruction = "圆心+半径：请点击一个点或画布空白位置作为圆心";
            return;
        }
        var centerPick = new PointPick(_selectedId, centerPoint);
        var radius = _dialogs.PromptText(
            "给定圆心和半径作圆",
            "请输入半径（支持分数、参数、sqrt、pi、幂与三角函数）：",
            "1");
        if (radius is null) return;
        try
        {
            var value = ExpressionParser.Parse(radius).Evaluate(_evaluation!.Parameters).Approximate;
            if (!double.IsFinite(value) || value <= 0) throw new InvalidOperationException("半径必须大于 0。");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("半径无效", ex.Message);
            ToolInstruction = "半径无效，圆未创建";
            return;
        }
        string? createdId = null;
        Apply(() =>
        {
            var centerId = MaterializePoints([centerPick])[0];
            var item = CreateObject("circleByCenterRadius", "circle", false);
            item.Center = centerId;
            item.Radius = radius;
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = "圆已创建；可继续点击圆心并输入半径";
    }

    private void HandleCenterDistanceCircle(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        if (_pointPicks.Count < 3)
        {
            ToolInstruction = _pointPicks.Count == 1
                ? "已选择圆心；请选择半径长度的第一个点"
                : "已选择长度起点；请选择第二个点确定半径";
            return;
        }

        var picks = _pointPicks.ToList();
        _pointPicks.Clear();
        string? createdId = null;
        Apply(() =>
        {
            var ids = MaterializePoints(picks);
            var item = CreateObject("circleByCenterDistance", "circle", false);
            item.Center = ids[0];
            item.PointA = ids[1];
            item.PointB = ids[2];
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = "圆已创建；半径等于所选两点之间的距离";
    }

    private void HandleThreePointCircle(
        Point2 world,
        IReadOnlyList<string> hitIds,
        IReadOnlyList<GridLineReference> gridLines,
        double toleranceWorld)
    {
        if (!TryAddSmartPointPick(world, hitIds, gridLines, toleranceWorld)) return;
        if (_pointPicks.Count < 3)
        {
            ToolInstruction = $"三点圆：已选择 {_pointPicks.Count}/3 个点";
            return;
        }
        var picks = _pointPicks.ToList();
        _pointPicks.Clear();
        string? createdId = null;
        Apply(() =>
        {
            var ids = MaterializePoints(picks);
            var item = CreateObject("circleByThreePoints", "circle", false);
            item.PointA = ids[0];
            item.PointB = ids[1];
            item.PointC = ids[2];
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
        ToolInstruction = "三点圆已创建；可继续选择三个点";
    }

    private void CreateIntersection(string objectA, string? objectB, string? gridAxis, string? gridValue, string selector)
    {
        string? createdId = null;
        Apply(() =>
        {
            var item = CreateObject(objectB is null ? "gridIntersectionPoint" : "intersectionPoint", "intersection", true);
            item.ObjectA = objectA;
            item.ObjectB = objectB;
            item.GridAxis = gridAxis;
            item.GridValue = gridValue;
            item.Selector = selector;
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
    }

    private void CreateCircleByPoints(string type, string pointA, string pointB, string? pointC)
    {
        string? createdId = null;
        Apply(() =>
        {
            var item = CreateObject(type, "circle", false);
            if (type == "circleByCenterPoint")
            {
                item.Center = pointA;
                item.PointA = pointB;
            }
            else
            {
                item.PointA = pointA;
                item.PointB = pointB;
                item.PointC = pointC;
            }
            _document!.Objects.Add(item);
            createdId = item.Id;
        });
        Select(createdId);
    }

    private bool TryAddPointPick(Point2 world, string? hitId)
    {
        if (!TryMakePointPick(world, hitId, out var pick))
        {
            ToolInstruction = "此步骤需要点；请选择已有点或点击画布空白位置";
            return false;
        }
        if (_pointPicks.Any(existing =>
                (existing.ExistingId is not null && existing.ExistingId == pick.ExistingId) ||
                (existing.ExistingId is null && pick.ExistingId is null && existing.World.DistanceTo(pick.World) < GeometryAlgorithms.Epsilon)))
        {
            ToolInstruction = "不能重复选择同一个点，请选择另一个点";
            return false;
        }
        _pointPicks.Add(pick);
        return true;
    }

    private bool TryMakePointPick(Point2 world, string? hitId, out PointPick pick)
    {
        if (FindExactPoint(world) is { } nearby)
        {
            pick = new PointPick(nearby, _evaluation!.Results[nearby].Point!.Value);
            return true;
        }
        if (hitId is not null)
        {
            if (_evaluation?.Results.GetValueOrDefault(hitId)?.Point is not { } point)
            {
                pick = default;
                return false;
            }
            pick = new PointPick(hitId, point);
            return true;
        }
        pick = new PointPick(null, world);
        return true;
    }

    private List<string> MaterializePoints(IReadOnlyList<PointPick> picks)
    {
        var ids = new List<string>(picks.Count);
        foreach (var pick in picks)
        {
            if (pick.ExistingId is not null)
            {
                ids.Add(pick.ExistingId);
                continue;
            }
            var point = CreateFixedPoint(pick.World);
            _document!.Objects.Add(point);
            ids.Add(point.Id);
        }
        return ids;
    }

    private GeometryObjectDefinition CreateFixedPoint(Point2 world, bool draggable = true)
    {
        var id = NextPointId();
        return new GeometryObjectDefinition
        {
            Id = id,
            Type = "fixedPoint",
            X = FormatNumber(world.X),
            Y = FormatNumber(world.Y),
            Draggable = draggable,
            Style = new GeometryStyle { ZIndex = 30 }
        };
    }

    private GeometryObjectDefinition CreateObject(string type, string idPrefix, bool resultIsPoint)
    {
        var id = resultIsPoint ? NextPointId() : NextId(idPrefix);
        return new GeometryObjectDefinition
        {
            Id = id,
            Type = type,
            Style = new GeometryStyle { ZIndex = resultIsPoint ? 30 : type.Contains("circle", StringComparison.OrdinalIgnoreCase) ? 8 : 10 }
        };
    }

    private static LabelDefinition NewLabel(string text) => new()
    {
        Text = text,
        Visible = true,
        FontSize = 45,
        OffsetX = 8,
        OffsetY = -30,
        DefaultOffsetX = 8,
        DefaultOffsetY = -30
    };

    private string NextPointId()
    {
        var used = _document!.Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        for (var ch = 'A'; ch <= 'Z'; ch++)
            if (!used.Contains(ch.ToString())) return ch.ToString();
        return NextId("point");
    }

    private string NextId(string prefix)
    {
        var used = _document!.Objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private string NextParameterId()
    {
        var used = _document!.Parameters.Select(parameter => parameter.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var candidate = $"t{index}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private GeometryObjectDefinition? FindObject(string id) => _document?.Objects.FirstOrDefault(o => o.Id == id);
    private bool IsPoint(string id) => _evaluation?.Results.GetValueOrDefault(id)?.Point is not null;
    private bool IsLine(string id) => _evaluation?.Results.GetValueOrDefault(id)?.Line is not null;
    private bool IsIntersectable(string id)
    {
        var result = _evaluation?.Results.GetValueOrDefault(id);
        return result is { IsValid: true } && (result.Line is not null || result.Circle is not null || result.Arc is not null);
    }

    private string? FindPointNear(Point2 world, double tolerance)
    {
        if (_evaluation is null) return null;
        return _evaluation.Results.Values
            .Where(result => result.IsValid && result.Point is not null)
            .Select(result => (result.ObjectId, Point: result.Point!.Value, Distance: result.Point.Value.DistanceTo(world)))
            .Where(item => item.Distance <= tolerance)
            .OrderBy(item => item.Distance)
            .Select(item => item.ObjectId)
            .FirstOrDefault();
    }

    private string? FindExactPoint(Point2 world) =>
        FindPointNear(world, GeometryAlgorithms.Epsilon * 10);

    private static string SelectorNearest(IReadOnlyList<Point2> points, Point2 world)
    {
        if (points.Count < 2) return "first";
        var index = Enumerable.Range(0, points.Count).MinBy(i => points[i].DistanceTo(world));
        return index == 0 ? "first" : "second";
    }

    private static string FormatNumber(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string UserDisplayName(GeometryObjectDefinition item) =>
        string.IsNullOrWhiteSpace(item.Name) ? item.Label?.Text ?? "" : item.Name;

    private void NewDocument()
    {
        ResetConstructionPlaybackSession(restoreBase: false);
        _document = new ProjectDocument();
        _currentPath = null;
        _evaluation = null;
        _selectedId = null;
        _definitionDirty = false;
        _undo.Clear();
        _redo.Clear();
        SetDrawingTool(DrawingTools.Select);
        LoadSolverSettingsFromDocument();
        Recalculate();
        ExportStatus = "已新建空白 v.3.02 项目";
    }

    public void BeginEdit()
    {
        if (_editing || _document is null) return;
        _undo.Push(ProjectSerializer.Clone(_document));
        _redo.Clear();
        _editing = true;
        _playbackDragRefreshGate.Reset();
        RefreshCommands();
    }

    public void EndEdit()
    {
        if (!_editing) return;
        _editing = false;
        Recalculate();
    }

    public void MoveLabel(string objectId, double offsetX, double offsetY)
    {
        var label = _document?.Objects.FirstOrDefault(o => o.Id == objectId)?.Label;
        if (label is null) return;
        label.OffsetX = offsetX;
        label.OffsetY = offsetY;
        Recalculate(false);
        RaiseSelection();
    }

    public void MoveDraggablePoint(
        string objectId,
        Point2 world,
        bool completeDrag = false)
    {
        if (_document?.Objects.FirstOrDefault(o => o.Id == objectId) is not { Draggable: true } item) return;
        if (item.Type == "fixedPoint")
        {
            item.X = FormatNumber(world.X);
            item.Y = FormatNumber(world.Y);
            if (_constructionPlaybackBaseDocument?.Objects.FirstOrDefault(
                    candidate => candidate.Id == item.Id) is { } basePoint)
            {
                basePoint.X = item.X;
                basePoint.Y = item.Y;
            }
            RecalculateDuringPointDrag();
            RefreshPlaybackDuringPointDrag(objectId, completeDrag);
            return;
        }
        if (item.Type == "parametricPoint" &&
            item.ParameterX is { Length: > 0 } parameterX &&
            item.ParameterY is { Length: > 0 } parameterY)
        {
            try
            {
                var values = _evaluation?.Parameters ??
                             _engine.Evaluate(_document).Parameters;
                var xDefinition = _document.Parameters.Single(
                    parameter => parameter.Id == parameterX);
                var yDefinition = _document.Parameters.Single(
                    parameter => parameter.Id == parameterY);
                var x = ClampParameterValue(
                    world.X,
                    xDefinition,
                    values);
                var y = ClampParameterValue(
                    world.Y,
                    yDefinition,
                    values);
                xDefinition.Value = FormatNumber(x);
                yDefinition.Value = FormatNumber(y);
                if (_constructionPlaybackBaseDocument is { } playbackBase)
                {
                    playbackBase.Parameters.Single(
                        parameter => parameter.Id == parameterX).Value =
                        xDefinition.Value;
                    playbackBase.Parameters.Single(
                        parameter => parameter.Id == parameterY).Value =
                        yDefinition.Value;
                }
                RecalculateDuringPointDrag();
            }
            catch (Exception ex)
            {
                ValidationStatus = ex.Message;
            }
            RefreshPlaybackDuringPointDrag(objectId, completeDrag);
            Raise(nameof(ParameterStatus));
            return;
        }
        if (item.Parameter is null) return;
        try
        {
            var value = _engine.ProjectParameter(_document, item, world);
            var formattedValue = FormatNumber(value);
            _document.Parameters.First(p => p.Id == item.Parameter).Value = formattedValue;
            if (_constructionPlaybackBaseDocument?.Parameters.FirstOrDefault(
                    parameter => parameter.Id == item.Parameter) is { } baseParameter)
                baseParameter.Value = formattedValue;
            _evaluation = _evaluation is null
                ? _engine.Evaluate(_document)
                : _engine.EvaluateParameterChange(_document, _evaluation, item.Parameter);
            ValidationStatus = _evaluation.Errors.Count == 0 ? string.Empty : string.Join(" · ", _evaluation.Errors);
            Raise(nameof(Evaluation));
            SceneChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { ValidationStatus = ex.Message; }
        RefreshPlaybackDuringPointDrag(objectId, completeDrag);
        Raise(nameof(ParameterStatus));
    }

    private static double ClampParameterValue(
        double value,
        ParameterDefinition definition,
        IReadOnlyDictionary<string, NumericValue> parameters)
    {
        var min = ExpressionParser.Parse(definition.Min)
            .Evaluate(parameters)
            .Approximate;
        var max = ExpressionParser.Parse(definition.Max)
            .Evaluate(parameters)
            .Approximate;
        if (!double.IsFinite(min) ||
            !double.IsFinite(max) ||
            max < min)
            throw new InvalidOperationException(
                $"运动参数 {definition.Id} 的 min/max 范围无效。");
        return System.Math.Clamp(value, min, max);
    }

    private void RecalculateDuringPointDrag()
    {
        if (_document is null)
            return;
        try
        {
            _evaluation = _engine.Evaluate(_document);
            ValidationStatus = _evaluation.Errors.Count == 0
                ? string.Empty
                : string.Join(" · ", _evaluation.Errors);
            Raise(nameof(Evaluation), nameof(ParameterStatus));
            SceneChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ValidationStatus = ex.Message;
        }
    }

    private void RefreshPlaybackDuringPointDrag(
        string selectedObjectId,
        bool completeDrag)
    {
        if (_constructionPlaybackBaseDocument is null ||
            _constructionPlaybackSourceSolution is null ||
            ConstructionPlaybackStep <= 0 ||
            !_playbackDragRefreshGate.ShouldRun(completeDrag))
            return;
        RebuildPlaybackAtCurrentParameter(
            selectedObjectId,
            lightweight: !completeDrag);
    }

    private void RebuildPlaybackAtCurrentParameter(
        string selectedObjectId,
        bool lightweight)
    {
        if (_constructionPlaybackBaseDocument is null ||
            _constructionPlaybackSourceSolution is null ||
            ConstructionPlaybackStep <= 0)
            return;
        var completedSteps = ConstructionPlaybackStep;
        BuildConstructionPlaybackDocument(completedSteps, lightweight);
        if (FindObject(selectedObjectId) is not null)
            _selectedId = selectedObjectId;
        if (!lightweight)
            RaiseSelection();
    }

    public void NudgeSelectedLabel(double dx, double dy)
    {
        if (SelectedObject?.Label is not { } label) return;
        Apply(() => { label.OffsetX += dx; label.OffsetY += dy; });
    }

    internal bool GetObjectVisible(string id)
    {
        var item = _document?.Objects.FirstOrDefault(o => o.Id == id);
        return item is not null && _document is not null && StyleResolver.Resolve(_document, item).Visible;
    }

    internal double GetObjectOpacity(string id)
    {
        var item = _document?.Objects.FirstOrDefault(o => o.Id == id);
        return item is not null && _document is not null ? StyleResolver.Resolve(_document, item).Opacity : 0;
    }

    internal void SetObjectVisible(string id, bool value)
    {
        var item = _document?.Objects.FirstOrDefault(o => o.Id == id);
        if (item is null) return;
        Apply(() => { item.Style ??= new GeometryStyle(); item.Style.Visible = value; });
    }

    internal void SetObjectOpacity(string id, double value)
    {
        var item = _document?.Objects.FirstOrDefault(o => o.Id == id);
        if (item is null) return;
        Apply(() => { item.Style ??= new GeometryStyle(); item.Style.Opacity = System.Math.Clamp(value, 0, 1); });
    }

    internal string GetObjectDisplayName(string id)
    {
        var item = FindObject(id);
        return item is null || string.IsNullOrWhiteSpace(UserDisplayName(item)) ? "未命名" : UserDisplayName(item);
    }

    private void PickColor()
    {
        var color = _dialogs.PickColor(SelectedColor);
        if (color is not null) SelectedColor = color;
    }

    private void ApplySelectedDefinition()
    {
        if (_document is null || SelectedObject is not { } current) return;
        try
        {
            var replacement = JsonSerializer.Deserialize<GeometryObjectDefinition>(SelectedDefinitionJson, ProjectSerializer.Options)
                              ?? throw new InvalidOperationException("对象定义不能为空。");
            if (string.IsNullOrWhiteSpace(replacement.Id)) throw new InvalidOperationException("对象 ID 不能为空。");
            if (_document.Objects.Any(o => !ReferenceEquals(o, current) && o.Id == replacement.Id))
                throw new InvalidOperationException($"对象 ID“{replacement.Id}”已经存在。");

            var index = _document.Objects.IndexOf(current);
            BeginEdit();
            if (replacement.Label is not null)
            {
                replacement.Label.DefaultOffsetX = current.Label?.DefaultOffsetX ?? replacement.Label.OffsetX;
                replacement.Label.DefaultOffsetY = current.Label?.DefaultOffsetY ?? replacement.Label.OffsetY;
            }
            _document.Objects[index] = replacement;
            _selectedId = replacement.Id;
            _definitionDirty = false;
            EndEdit();
            if (_evaluation is not null) ValidationStatus = $"对象 {replacement.Id} 定义已应用";
        }
        catch (Exception ex)
        {
            ValidationStatus = $"对象定义修改失败：{ex.Message}";
        }
    }

    private void AddObject()
    {
        if (_document is null) return;
        var dialog = new AddObjectDialog(_document)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
        Apply(() =>
        {
            _document.Objects.Add(result);
            _selectedId = result.Id;
        });
        ExportStatus = $"已添加对象：{result.Id}";
    }

    private void Open() { var path = _dialogs.OpenJson(); if (path is not null) Load(path); }
    private void Reload() { if (_currentPath is not null) Load(_currentPath); }

    private void Load(string path)
    {
        try
        {
            ResetConstructionPlaybackSession(restoreBase: false);
            _document = ProjectSerializer.Load(path);
            _currentPath = path;
            _undo.Clear(); _redo.Clear(); _selectedId = null; _definitionDirty = false;
            SetDrawingTool(DrawingTools.Select);
            LoadSolverSettingsFromDocument();
            Recalculate();
            ExportStatus = "";
        }
        catch (Exception ex) { ValidationStatus = ex.Message; _dialogs.ShowError("JSON 加载失败", ex.Message); }
    }

    private void Save()
    {
        if (_document is null) return;
        if (_currentPath is null) { SaveAs(); return; }
        try
        {
            StoreSolverSettingsInDocument();
            ProjectSerializer.Save(_document, _currentPath);
            ExportStatus = $"已保存：{Path.GetFileName(_currentPath)}";
        }
        catch (Exception ex) { _dialogs.ShowError("保存失败", ex.Message); }
    }

    private void SaveAs()
    {
        if (_document is null) return;
        var path = _dialogs.SaveJson(_currentPath);
        if (path is null) return;
        _currentPath = path; Save(); RefreshCommands();
    }

    private void Undo()
    {
        if (_document is null || _undo.Count == 0) return;
        ResetConstructionPlaybackSession(restoreBase: false);
        _redo.Push(ProjectSerializer.Clone(_document));
        _document = _undo.Pop();
        ResetDrawingProgressAfterHistoryChange();
        Recalculate();
    }

    private void Redo()
    {
        if (_document is null || _redo.Count == 0) return;
        ResetConstructionPlaybackSession(restoreBase: false);
        _undo.Push(ProjectSerializer.Clone(_document));
        _document = _redo.Pop();
        ResetDrawingProgressAfterHistoryChange();
        Recalculate();
    }

    private void ResetDrawingProgressAfterHistoryChange()
    {
        _pointPicks.Clear();
        _toolLineId = null;
        _toolGridLine = null;
        Preview = null;
        _selectedId = null;
        _selectedObjectEntry = null;
        ToolInstruction = DefaultToolInstruction(ActiveTool);
        Raise(nameof(RelationNeedsLine), nameof(RelationNeedsPoint),
            nameof(PendingRelationLineId), nameof(PendingRelationGridLine));
    }

    private void DeleteSelected()
    {
        if (_document is null || SelectedObject is not { } selected) return;
        var graph = new DependencyGraph(_document.Objects);
        var removedIds = graph.AffectedBy(selected.Id).ToHashSet(StringComparer.Ordinal);
        var removedParameterIds = _document.Objects
            .Where(item => removedIds.Contains(item.Id))
            .SelectMany(item => new[]
            {
                item.Parameter,
                item.ParameterX,
                item.ParameterY
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        Apply(() =>
        {
            _document.Objects.RemoveAll(item => removedIds.Contains(item.Id));
            _document.Parameters.RemoveAll(parameter =>
                removedParameterIds.Contains(parameter.Id) && !IsParameterReferenced(parameter.Id));
            _selectedId = null;
            _selectedObjectEntry = null;
        });
        ExportStatus = removedIds.Count == 1
            ? $"已删除对象 {selected.Id}"
            : $"已删除对象 {selected.Id} 及 {removedIds.Count - 1} 个依赖对象";
    }

    private bool IsParameterReferenced(string parameterId)
    {
        if (_document is null) return false;
        if (_document.Objects.Any(item =>
                item.Parameter == parameterId ||
                item.ParameterX == parameterId ||
                item.ParameterY == parameterId))
            return true;
        foreach (var expression in _document.Objects.SelectMany(item => new[]
                 {
                     item.X, item.Y, item.Radius, item.Ratio, item.StartAngle, item.EndAngle, item.GridValue
                 }).Where(value => !string.IsNullOrWhiteSpace(value)))
            if (ExpressionParser.Parse(expression!).Dependencies.Contains(parameterId)) return true;
        foreach (var expression in _document.Parameters.SelectMany(parameter => new[]
                 {
                     parameter.Value, parameter.Min, parameter.Max
                 }))
            if (ExpressionParser.Parse(expression).Dependencies.Contains(parameterId)) return true;
        return false;
    }

    private void ApplyCanvasSize()
    {
        if (_document is null) return;
        if (!double.TryParse(CanvasWidthInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(CanvasHeightInput, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            ValidationStatus = "画布宽度和高度必须是大于 0 的数字。";
            return;
        }
        Apply(() =>
        {
            _document.Canvas.MaxX = _document.Canvas.MinX + width;
            _document.Canvas.MaxY = _document.Canvas.MinY + height;
            foreach (var point in _document.Objects.Where(item => item.Type == "pointOnGridLine" && item.Parameter is not null))
            {
                var parameter = _document.Parameters.FirstOrDefault(item => item.Id == point.Parameter);
                if (parameter is null) continue;
                parameter.Min = FormatNumber(point.GridAxis == "x" ? _document.Canvas.MinY : _document.Canvas.MinX);
                parameter.Max = FormatNumber(point.GridAxis == "x" ? _document.Canvas.MaxY : _document.Canvas.MaxX);
            }
        });
        ExportStatus = $"画布已调整为 {width:G6} × {height:G6}，向右、向上扩展";
        Raise(nameof(CanvasOriginText));
    }

    private void RestoreLayout()
    {
        if (_document is null) return;
        Apply(() =>
        {
            foreach (var label in _document.Objects.Where(o => o.Label is not null).Select(o => o.Label!))
            {
                label.OffsetX = label.DefaultOffsetX; label.OffsetY = label.DefaultOffsetY;
            }
        });
    }

    private void RestoreSelectedLabel()
    {
        if (SelectedObject?.Label is not { } label) return;
        Apply(() => { label.OffsetX = label.DefaultOffsetX; label.OffsetY = label.DefaultOffsetY; });
    }

    private void LoadSolverSettingsFromDocument()
    {
        var settings = _document?.Solver ?? new SolverSettings();
        ConstructionTargetsInput = string.Join(
            "\r\n",
            settings.Targets.Select(target => target.Constraint is null
                ? $"{target.Name} = {target.X}, {target.Y}"
                : $"{target.Name} = @constraint"));
        ConstructionMinStepsInput = settings.MinSteps.ToString(CultureInfo.InvariantCulture);
        ConstructionMaxStepsInput = settings.MaxSteps.ToString(CultureInfo.InvariantCulture);
        ConstructionMaxSolutionsInput = settings.MaxSolutionsPerTarget.ToString(CultureInfo.InvariantCulture);
        ConstructionMaxStatesInput = settings.MaxStates.ToString(CultureInfo.InvariantCulture);
        ConstructionParameterSamplesInput = settings.ParameterSampleCount.ToString(CultureInfo.InvariantCulture);
        ConstructionCpuThreadsInput = (
            settings.CpuThreads > 0
                ? settings.CpuThreads
                : Environment.ProcessorCount)
            .ToString(CultureInfo.InvariantCulture);
        _solverUseStraightedge = settings.UseStraightedge;
        _solverUseCompass = settings.UseCompass;
        _solverUseGrid = settings.UseGrid;
        _solverValidateParameters = settings.ValidateParameters;
        _solverSearchStrategy = System.Math.Clamp(settings.SearchStrategy, 0, 3);
        ConstructionSolutions.Clear();
        SelectedConstructionSolution = null;
        ConstructionSearchStatus = settings.Targets.Count == 0
            ? "JSON 中尚未设置求解目标；请输入目标坐标后开始搜索。"
            : $"已从 JSON 载入 {settings.Targets.Count} 个求解目标。";
        Raise(
            nameof(ConstructionTargetsInput),
            nameof(ConstructionMinStepsInput),
            nameof(ConstructionMaxStepsInput),
            nameof(ConstructionMaxSolutionsInput),
            nameof(ConstructionMaxStatesInput),
            nameof(ConstructionParameterSamplesInput),
            nameof(ConstructionCpuThreadsInput),
            nameof(SolverUseStraightedge),
            nameof(SolverUseCompass),
            nameof(SolverUseGrid),
            nameof(SolverValidateParameters),
            nameof(SolverSearchStrategy),
            nameof(SolverSearchStrategyName),
            nameof(SolverSearchStrategyDescription));
    }

    private void StoreSolverSettingsInDocument()
    {
        if (_document is null) return;
        if (!int.TryParse(ConstructionMinStepsInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minSteps)
            || minSteps is < 0 or > 8)
            throw new InvalidOperationException("起始作图笔数必须是 0–8 的整数。");
        if (!int.TryParse(ConstructionMaxStepsInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSteps)
            || maxSteps is < 1 or > 8)
            throw new InvalidOperationException("最大作图笔数必须是 1–8 的整数。");
        if (minSteps > maxSteps)
            throw new InvalidOperationException("起始作图笔数不能大于最大作图笔数。");
        if (!int.TryParse(ConstructionMaxSolutionsInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSolutions)
            || maxSolutions is < 1 or > 20)
            throw new InvalidOperationException("保留联合解数必须是 1–20 的整数。");
        if (!long.TryParse(ConstructionMaxStatesInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxStates)
            || maxStates < 100)
            throw new InvalidOperationException("状态上限必须是不小于 100 的整数。");
        if (!int.TryParse(ConstructionParameterSamplesInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parameterSamples)
            || parameterSamples is < 3 or > 11)
            throw new InvalidOperationException("含参联合验解的位置数必须是 3–11 的整数。");
        if (!int.TryParse(ConstructionCpuThreadsInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cpuThreads)
            || cpuThreads is < 1 or > 256)
            throw new InvalidOperationException("CPU 并行线程数必须是 1–256 的整数。");
        if (!SolverUseStraightedge && !SolverUseCompass)
            throw new InvalidOperationException("请至少启用直尺或圆规中的一种。");

        _document.Solver = new SolverSettings
        {
            Targets = ParseConstructionTargetDefinitions(false),
            MinSteps = minSteps,
            MaxSteps = maxSteps,
            MaxSolutionsPerTarget = maxSolutions,
            MaxStates = maxStates,
            UseStraightedge = SolverUseStraightedge,
            UseCompass = SolverUseCompass,
            UseGrid = SolverUseGrid,
            SearchStrategy = _solverSearchStrategy,
            ValidateParameters = SolverValidateParameters,
            ParameterSampleCount = parameterSamples,
            CpuThreads = cpuThreads
        };
    }

    private async Task StartConstructionSearchAsync()
    {
        if (_evaluation is null || _document is null || IsConstructionSearching) return;
        try
        {
            var targets = ParseConstructionTargets();
            if (!int.TryParse(ConstructionMinStepsInput, out var minSteps) || minSteps is < 0 or > 8)
                throw new InvalidOperationException("起始作图笔数必须是 0–8 的整数。");
            if (!int.TryParse(ConstructionMaxStepsInput, out var maxSteps) || maxSteps is < 1 or > 8)
                throw new InvalidOperationException("最大作图笔数必须是 1–8 的整数。");
            if (minSteps > maxSteps)
                throw new InvalidOperationException("起始作图笔数不能大于最大作图笔数。");
            if (!int.TryParse(ConstructionMaxSolutionsInput, out var maxSolutions) || maxSolutions is < 1 or > 20)
                throw new InvalidOperationException("保留联合解数必须是 1–20 的整数。");
            if (!long.TryParse(ConstructionMaxStatesInput, out var maxStates) || maxStates < 100)
                throw new InvalidOperationException("状态上限必须是不小于 100 的整数。");
            if (!int.TryParse(ConstructionParameterSamplesInput, out var parameterSamples)
                || parameterSamples is < 3 or > 11)
                throw new InvalidOperationException("含参联合验解的位置数必须是 3–11 的整数。");
            if (!int.TryParse(ConstructionCpuThreadsInput, out var cpuThreads)
                || cpuThreads is < 1 or > 256)
                throw new InvalidOperationException("CPU 并行线程数必须是 1–256 的整数。");
            if (!SolverUseStraightedge && !SolverUseCompass)
                throw new InvalidOperationException("请至少启用直尺或圆规中的一种。");
            StoreSolverSettingsInDocument();

            _constructionSearchCancellation?.Cancel();
            _constructionPlaybackCancellation?.Cancel();
            _constructionSearchCancellation = new CancellationTokenSource();
            var cancellationToken = _constructionSearchCancellation.Token;
            var (seedPoints, seedElements) = BuildConstructionSearchSeed();
            var validationScenarios = SolverValidateParameters
                ? BuildParameterizedValidationScenarios(parameterSamples)
                : [];
            var searchBounds = _evaluation.Scene.Canvas;
            IConstructionCandidateAccelerator? candidateAccelerator = null;
            var accelerationStatus = "CPU 精确搜索";
            if (AppPreferences.Current.ExperimentalGpuAcceleration)
            {
                if (GpuAccelerationManager.TryGet(
                        out var gpuAccelerator,
                        out var gpuStatus))
                {
                    candidateAccelerator = gpuAccelerator;
                    accelerationStatus =
                        $"GPU 候选批处理：{gpuAccelerator!.DeviceName}";
                }
                else
                {
                    accelerationStatus =
                        $"GPU 不可用，已回退 CPU（{gpuStatus}）";
                }
            }
            var gpuPairsAtSearchStart =
                candidateAccelerator?.TotalPairsProcessed ?? 0;
            var options = new ConstructionSearchOptions(
                MinSteps: minSteps,
                MaxSteps: maxSteps,
                MaxSolutions: maxSolutions,
                MaxStates: maxStates,
                MaxPointsPerState: SolverUseGrid ? 240 : 120,
                UseStraightedge: SolverUseStraightedge,
                UseCompass: SolverUseCompass,
                MinX: searchBounds.MinX,
                MaxX: searchBounds.MaxX,
                MinY: searchBounds.MinY,
                MaxY: searchBounds.MaxY,
                UseCollinearityClosure: _solverSearchStrategy is 0 or 1,
                UseGuidedSearch: _solverSearchStrategy is 0 or 2,
                UseExhaustiveSearch: _solverSearchStrategy is 0 or 3,
                ValidationScenarios: validationScenarios,
                CandidateAccelerator: candidateAccelerator,
                MaxDegreeOfParallelism: cpuThreads);
            ConstructionSolutions.Clear();
            SelectedConstructionSolution = null;
            IsConstructionSearching = true;
            ConstructionSearchStatus =
                validationScenarios.Count == 0
                    ? $"已知点 {seedPoints.Count} 个、已知对象 {seedElements.Count} 个；笔数范围 {minSteps}–{maxSteps}；CPU {cpuThreads} 线程；{accelerationStatus}；准备搜索…"
                    : $"含参联合验解已启用：当前图命中后再检查 {validationScenarios.Count} 个运动位置；笔数范围 {minSteps}–{maxSteps}；CPU {cpuThreads} 线程；{accelerationStatus}；准备搜索…";

            var searchTimer = Stopwatch.StartNew();
            var progress = new Progress<ConstructionSearchProgress>(value =>
            {
                var statesPerSecond =
                    value.ExploredStates /
                    Math.Max(searchTimer.Elapsed.TotalSeconds, 1e-9);
                ConstructionSearchStatus =
                    $"{value.Message} · 当前笔数 {value.CurrentDepth}"
                    + $" · 状态 {value.ExploredStates:N0}"
                    + $"（{FormatPairRate(statesPerSecond)}/秒）"
                    + $" · 解 {value.SolutionsFound}"
                    + FormatAccelerationProgress(candidateAccelerator);
            });
            var results = await Task.Run(() =>
            {
                var engine = new ConstructionSearchEngine();
                var report = engine.Search(
                    seedPoints,
                    seedElements,
                    targets,
                    options,
                    progress,
                    cancellationToken);
                return report with
                {
                    Solutions = report.Solutions
                        .Select(solution => ConstructionSolutionOptimizer.RemoveRedundantCarrierSteps(
                            solution,
                            seedElements))
                        .ToList()
                };
            }, cancellationToken);

            foreach (var solution in results.Solutions)
                ConstructionSolutions.Add(new ConstructionSolutionEntryViewModel(
                    solution,
                    results.OptimalityProven,
                    results.Strategy));
            SelectedConstructionSolution = ConstructionSolutions.FirstOrDefault();
            ConstructionSearchStatus = ConstructionSolutions.Count == 0
                ? results.StateLimitReached
                    ? "未在当前状态上限内找到解；可提高每层状态上限或最大作图笔数后继续。"
                    : "在指定最大作图笔数内没有找到联合解。"
                : results.OptimalityProven
                    ? $"联合搜索完成：找到 {ConstructionSolutions.Count} 个最少笔数解，"
                      + $"同一套构造同时完成 {targets.Count} 个目标。"
                    : results.Strategy == "parameterized-two-level-collinearity"
                        ? $"不定图联合验解完成：找到 {ConstructionSolutions[0].Solution.StepCount} 笔通用解；"
                          + $"同一套构造已在 {validationScenarios.Count} 个运动位置逐步重放并全部命中。"
                    : results.Strategy == "collinearity-closure"
                        ? $"共线闭包完成：找到 {ConstructionSolutions[0].Solution.StepCount} 笔联合解；"
                          + "同一条经证明的载线可同时完成多个目标。"
                    : $"目标反推搜索完成：已快速找到 {ConstructionSolutions.Count} 个联合候选解；"
                      + "尚未穷尽所有更浅分支，因此暂不标注为最少笔数。";
            ConstructionSearchStatus += FormatAccelerationSummary(
                candidateAccelerator,
                accelerationStatus,
                gpuPairsAtSearchStart);
            ConstructionSearchStatus +=
                $" · 完整状态 {results.ExploredStates:N0}，"
                + $"{FormatPairRate(results.ExploredStates / Math.Max(searchTimer.Elapsed.TotalSeconds, 1e-9))}/秒";
        }
        catch (OperationCanceledException)
        {
            ConstructionSearchStatus = "搜索已取消。";
        }
        catch (Exception ex)
        {
            ConstructionSearchStatus = $"搜索设置无效：{ex.Message}";
            _dialogs.ShowError("构造搜索", ex.Message);
        }
        finally
        {
            IsConstructionSearching = false;
        }
    }

    private void CancelConstructionSearch()
    {
        _constructionSearchCancellation?.Cancel();
        ConstructionSearchStatus = "正在取消搜索…";
    }

    private void ExportConstructionSteps()
    {
        if (SelectedConstructionSolution is not { } selected)
            return;
        try
        {
            var path = _dialogs.SaveConstructionSteps(_currentPath);
            if (path is null)
                return;
            ConstructionSolutionSerializer.Save(
                new ConstructionSolutionPackage
                {
                    SourceProject = _currentPath,
                    Strategy = selected.Strategy,
                    OptimalityProven = selected.OptimalityProven,
                    Solutions = [selected.Solution]
                },
                path);
            ConstructionSearchStatus =
                $"已导出 {selected.Solution.StepCount} 笔作图步骤：{Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("导出作图步骤失败", ex.Message);
        }
    }

    private void ImportConstructionSteps()
    {
        if (_evaluation is null || _document is null)
            return;
        try
        {
            var path = _dialogs.OpenConstructionSteps();
            if (path is null)
                return;
            var package = ConstructionSolutionSerializer.Load(path);
            var targets = ParseConstructionTargets();
            var (points, elements) = BuildConstructionSearchSeed();
            var scenarios = SolverValidateParameters
                ? BuildParameterizedValidationScenarios(
                    int.TryParse(
                        ConstructionParameterSamplesInput,
                        out var sampleCount)
                        ? sampleCount
                        : 5)
                : [];
            var bounds = _evaluation.Scene.Canvas;
            var options = new ConstructionSearchOptions(
                MinSteps: 0,
                MaxSteps: 8,
                MaxPointsPerState: SolverUseGrid ? 300 : 160,
                UseStraightedge: SolverUseStraightedge,
                UseCompass: SolverUseCompass,
                MinX: bounds.MinX,
                MaxX: bounds.MaxX,
                MinY: bounds.MinY,
                MaxY: bounds.MaxY,
                ValidationScenarios: scenarios);
            var baseScenario = new ConstructionSearchScenario(
                points,
                elements,
                targets,
                "当前图");
            var imported = new List<ConstructionSolution>();
            foreach (var source in package.Solutions)
            {
                var replayed = ConstructionSearchEngine.ReplaySolution(
                    source,
                    baseScenario,
                    options);
                if (replayed is null ||
                    !scenarios.All(scenario =>
                        ConstructionSearchEngine.ReplaysSolution(
                            source,
                            scenario,
                            options)))
                    continue;
                imported.Add(replayed);
            }

            if (imported.Count == 0)
                throw new InvalidOperationException(
                    "步骤与当前题目不匹配，或不能在当前不定图检查位置中重放。");
            foreach (var solution in imported)
                ConstructionSolutions.Add(
                    new ConstructionSolutionEntryViewModel(
                        solution,
                        package.OptimalityProven,
                        string.IsNullOrWhiteSpace(package.Strategy)
                            ? "imported"
                            : package.Strategy));
            SelectedConstructionSolution =
                ConstructionSolutions[
                    ConstructionSolutions.Count - imported.Count];
            ConstructionSearchStatus =
                $"已导入并验证 {imported.Count} 个作图解，可直接播放或导出 GIF / MP4。";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("导入作图步骤失败", ex.Message);
        }
    }

    private static string FormatAccelerationProgress(
        IConstructionCandidateAccelerator? accelerator)
    {
        if (accelerator is null || accelerator.TotalPairsProcessed == 0)
            return string.Empty;
        return $" · GPU {FormatPairRate(accelerator.LastPairsPerSecond)} 对/秒";
    }

    private static string FormatAccelerationSummary(
        IConstructionCandidateAccelerator? accelerator,
        string accelerationStatus,
        long pairsAtSearchStart)
    {
        if (accelerator is null)
            return $" · {accelerationStatus}";
        var processedPairs =
            accelerator.TotalPairsProcessed - pairsAtSearchStart;
        if (processedPairs == 0)
            return " · GPU 已启用，本题候选规模未达到批处理阈值";
        return $" · GPU 共处理 {processedPairs:N0} 个候选点对，"
               + $"末批 {FormatPairRate(accelerator.LastPairsPerSecond)} 对/秒";
    }

    private static string FormatPairRate(double value) =>
        value >= 10_000
            ? $"{value / 10_000:N1} 万"
            : $"{value:N0}";

    private List<ConstructionSearchTarget> ParseConstructionTargets()
    {
        if (_evaluation is null) throw new InvalidOperationException("当前图形尚未完成计算。");
        var result = new List<ConstructionSearchTarget>();
        foreach (var target in ParseConstructionTargetDefinitions())
            result.Add(ConstructionTargetResolver.Resolve(target, _evaluation));
        return result;
    }

    private List<SolverTargetDefinition> ParseConstructionTargetDefinitions(bool requireAtLeastOne = true)
    {
        var normalized = ConstructionTargetsInput
            .Replace("\u200b", "")
            .Replace("\ufeff", "")
            .Replace('，', ',')
            .Replace('（', '(')
            .Replace('）', ')')
            .Replace('；', '\n')
            .Replace(';', '\n');
        var result = new List<SolverTargetDefinition>();
        var fallbackIndex = 1;
        foreach (var rawLine in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            string name;
            string coordinateText;
            var equalsIndex = line.IndexOf('=');
            if (equalsIndex >= 0)
            {
                name = line[..equalsIndex].Trim();
                coordinateText = line[(equalsIndex + 1)..].Trim();
            }
            else
            {
                var open = line.IndexOf('(');
                var close = line.LastIndexOf(')');
                if (open > 0 && close > open)
                {
                    name = line[..open].Trim();
                    coordinateText = line[(open + 1)..close];
                }
                else
                {
                    name = $"目标{fallbackIndex++}";
                    coordinateText = line;
                }
            }
            if (string.Equals(
                    coordinateText,
                    "@constraint",
                    StringComparison.OrdinalIgnoreCase))
            {
                var existing = _document?.Solver.Targets.FirstOrDefault(target =>
                    string.Equals(
                        target.Name,
                        name,
                        StringComparison.Ordinal) &&
                    target.Constraint is not null);
                if (existing is null)
                    throw new InvalidOperationException(
                        $"目标“{name}”没有可复用的 JSON constraint 定义。");
                result.Add(existing);
                continue;
            }
            if (!TrySplitCoordinatePair(coordinateText, out var xExpression, out var yExpression)
                || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"无法识别目标“{rawLine}”，请使用 P = 7/8, 5/2。");
            result.Add(new SolverTargetDefinition
            {
                Name = name,
                X = xExpression,
                Y = yExpression
            });
        }
        if (requireAtLeastOne && result.Count == 0)
            throw new InvalidOperationException("请至少输入一个目标坐标。");
        return result;
    }

    private static bool TrySplitCoordinatePair(
        string coordinateText,
        out string xExpression,
        out string yExpression)
    {
        var text = coordinateText.Trim();
        if (IsSingleOuterWrapper(text, '(', ')') || IsSingleOuterWrapper(text, '[', ']'))
            text = text[1..^1].Trim();

        var depth = 0;
        var separatorIndex = -1;
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '(':
                case '[':
                    depth++;
                    break;
                case ')':
                case ']':
                    depth--;
                    if (depth < 0)
                    {
                        xExpression = "";
                        yExpression = "";
                        return false;
                    }
                    break;
                case ',' when depth == 0:
                    if (separatorIndex >= 0)
                    {
                        xExpression = "";
                        yExpression = "";
                        return false;
                    }
                    separatorIndex = index;
                    break;
            }
        }

        if (depth != 0 || separatorIndex <= 0 || separatorIndex >= text.Length - 1)
        {
            xExpression = "";
            yExpression = "";
            return false;
        }

        xExpression = text[..separatorIndex].Trim();
        yExpression = text[(separatorIndex + 1)..].Trim();
        return xExpression.Length > 0 && yExpression.Length > 0;
    }

    private static bool IsSingleOuterWrapper(string text, char open, char close)
    {
        if (text.Length < 2 || text[0] != open || text[^1] != close)
            return false;

        var depth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == open)
                depth++;
            else if (text[index] == close)
                depth--;

            if (depth == 0)
                return index == text.Length - 1;
            if (depth < 0)
                return false;
        }
        return false;
    }

    private (List<ConstructionSearchPoint> Points, List<ConstructionSearchElement> Elements)
        BuildConstructionSearchSeed()
    {
        if (_evaluation is null)
            throw new InvalidOperationException("当前图形尚未完成计算。");
        return BuildConstructionSearchSeed(_evaluation);
    }

    private List<ConstructionSearchScenario> BuildParameterizedValidationScenarios(
        int sampleCount)
    {
        if (_document is null || _evaluation is null)
            throw new InvalidOperationException("当前图形尚未完成计算。");

        var movingParameterIds = _document.Objects
            .Where(item => item.Draggable)
            .SelectMany(item => new[]
            {
                item.Parameter,
                item.ParameterX,
                item.ParameterY
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (movingParameterIds.Count == 0)
            throw new InvalidOperationException(
                "已启用含参联合验解，但当前 JSON 中没有由 draggable 对象引用的运动参数。");

        var parameterRanges = movingParameterIds
            .Select(id =>
            {
                var definition = _document.Parameters.Single(parameter => parameter.Id == id);
                var min = ExpressionParser.Parse(definition.Min)
                    .Evaluate(_evaluation.Parameters)
                    .Approximate;
                var max = ExpressionParser.Parse(definition.Max)
                    .Evaluate(_evaluation.Parameters)
                    .Approximate;
                var current = ExpressionParser.Parse(definition.Value)
                    .Evaluate(_evaluation.Parameters)
                    .Approximate;
                if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
                    throw new InvalidOperationException($"运动参数 {id} 的 min/max 范围无效。");
                if (!double.IsFinite(current))
                    throw new InvalidOperationException($"运动参数 {id} 的当前值无效。");
                return (Id: id, Min: min, Max: max, Current: current);
            })
            .ToList();
        var targetDefinitions = _document.Solver.Targets.ToList();
        var scenarios = new List<ConstructionSearchScenario>();
        // sampleCount 表示“总检查位置数”，当前画面已经是第 1 个位置。
        // 这里只生成其余位置；普通搜索会先在当前画面命中完整候选，
        // 然后才按本列表逐一回放，并在第一处失败时立即短路。
        var additionalSampleCount = System.Math.Max(0, sampleCount - 1);
        var samplePlans = Enumerable.Range(0, additionalSampleCount)
            .Select(sampleIndex =>
            {
                var values = parameterRanges
                    .Select((range, parameterIndex) =>
                    {
                        var fraction = Halton(
                            sampleIndex + 1 + parameterIndex * 11,
                            HaltonPrimes[parameterIndex % HaltonPrimes.Length]);
                        var value = range.Min + (range.Max - range.Min) * fraction;
                        var normalizedDistance =
                            System.Math.Abs(value - range.Current) /
                            (range.Max - range.Min);
                        return (range.Id, Value: value, NormalizedDistance: normalizedDistance);
                    })
                    .ToList();
                // 优先回放离当前画面更远的位置，让定图巧合尽早失败。
                var rejectionScore = values.Sum(item => item.NormalizedDistance);
                return (SampleIndex: sampleIndex, Values: values, RejectionScore: rejectionScore);
            })
            .OrderByDescending(plan => plan.RejectionScore)
            .ThenBy(plan => plan.SampleIndex)
            .ToList();

        foreach (var samplePlan in samplePlans)
        {
            var document = ProjectSerializer.Clone(_document);
            var labels = new List<string>();
            foreach (var sampleValue in samplePlan.Values)
            {
                document.Parameters.Single(parameter => parameter.Id == sampleValue.Id).Value =
                    FormatNumber(sampleValue.Value);
                labels.Add($"{sampleValue.Id}={sampleValue.Value:G6}");
            }

            var evaluation = _engine.Evaluate(document);
            if (evaluation.Errors.Count > 0)
                throw new InvalidOperationException(
                    $"运动位置 {string.Join(", ", labels)} 的图形无效：{evaluation.Errors[0]}");
            var (points, elements) = BuildConstructionSearchSeed(evaluation);
            List<ConstructionSearchTarget> targets;
            try
            {
                targets = targetDefinitions
                    .Select(target =>
                        ConstructionTargetResolver.Resolve(target, evaluation))
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"运动位置 {string.Join(", ", labels)} 的目标约束无解：{ex.Message}",
                    ex);
            }
            scenarios.Add(new ConstructionSearchScenario(
                points,
                elements,
                targets,
                string.Join(", ", labels)));
        }
        return scenarios;
    }

    private static readonly int[] HaltonPrimes =
        [2, 3, 5, 7, 11, 13, 17, 19];

    private static double Halton(int index, int basis)
    {
        var result = 0d;
        var fraction = 1d;
        while (index > 0)
        {
            fraction /= basis;
            result += fraction * (index % basis);
            index /= basis;
        }
        return result;
    }

    private (List<ConstructionSearchPoint> Points, List<ConstructionSearchElement> Elements)
        BuildConstructionSearchSeed(SceneEvaluation evaluation)
    {
        var points = new List<ConstructionSearchPoint>();
        var elements = new List<ConstructionSearchElement>();
        var bounds = evaluation.Scene.Canvas;
        foreach (var item in evaluation.Scene.Items.Where(item => item.Style.Visible))
        {
            if (item.Geometry.Point is { } point)
                points.Add(new ConstructionSearchPoint($"known-point:{item.Id}", point, item.Id));
            if (item.Geometry.Line is not null || item.Geometry.Circle is not null || item.Geometry.Arc is not null)
                elements.Add(new ConstructionSearchElement($"known:{item.Id}", item.Geometry));
        }

        if (SolverUseGrid && evaluation.Scene.Grid is { Visible: true, Step: > 0 } grid)
        {
            var verticals = new List<GridLineReference>();
            var horizontals = new List<GridLineReference>();
            for (var x = bounds.MinX; x <= bounds.MaxX + 1e-9 && verticals.Count < 50; x += grid.Step)
                verticals.Add(new GridLineReference("x", x));
            for (var y = bounds.MinY; y <= bounds.MaxY + 1e-9 && horizontals.Count < 50; y += grid.Step)
                horizontals.Add(new GridLineReference("y", y));
            foreach (var line in verticals.Concat(horizontals))
                elements.Add(new ConstructionSearchElement(
                    GridSearchKey(line),
                    GridGeometry(line)));
            foreach (var vertical in verticals)
                foreach (var horizontal in horizontals)
                    points.Add(new ConstructionSearchPoint(
                        $"grid-point:{FormatNumber(vertical.Value)}:{FormatNumber(horizontal.Value)}",
                        new Point2(vertical.Value, horizontal.Value)));
        }
        var completedPoints = ConstructionSearchEngine.CompleteKnownIntersections(
            points,
            elements,
            includePoint: point =>
                point.X >= bounds.MinX - 1e-7 &&
                point.X <= bounds.MaxX + 1e-7 &&
                point.Y >= bounds.MinY - 1e-7 &&
                point.Y <= bounds.MaxY + 1e-7);
        return (completedPoints.ToList(), elements);
    }

    private async Task PlayConstructionSolutionAsync()
    {
        if (_document is null || SelectedConstructionSolution?.Solution is not { } solution) return;
        if (IsConstructionPlaybackPlaying)
        {
            _constructionPlaybackCancellation?.Cancel();
            IsConstructionPlaybackPlaying = false;
            SetPlaybackVisual(null);
            ConstructionSearchStatus =
                $"动画已暂停在第 {ConstructionPlaybackStep} / {solution.StepCount} 步。";
            return;
        }

        PrepareConstructionPlaybackSession(solution);
        _constructionPlaybackCancellation?.Cancel();
        _constructionPlaybackCancellation = new CancellationTokenSource();
        var cancellationToken = _constructionPlaybackCancellation.Token;
        IsConstructionPlaybackPlaying = true;
        try
        {
            while (ConstructionPlaybackStep < solution.StepCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AnimateConstructionStepAsync(ConstructionPlaybackStep, cancellationToken);
            }
            var completedNames = string.Join("、", solution.CompletedTargets.Select(target => target.Name));
            ConstructionSearchStatus =
                $"动画完成：已用 {solution.StepCount} 笔同时构造 {completedNames}。可用“上一步”逐笔复看。";
        }
        catch (OperationCanceledException)
        {
            ConstructionSearchStatus =
                $"动画已暂停在第 {ConstructionPlaybackStep} / {solution.StepCount} 步。";
        }
        catch (Exception ex)
        {
            ConstructionSearchStatus = $"动画执行失败：{ex.Message}";
            _dialogs.ShowError("构造动画", ex.Message);
        }
        finally
        {
            SetPlaybackVisual(null);
            IsConstructionPlaybackPlaying = false;
            IsConstructionPlaybackAnimating = false;
        }
    }

    private async Task NextConstructionStepAsync()
    {
        if (_document is null || SelectedConstructionSolution?.Solution is not { } solution) return;
        PrepareConstructionPlaybackSession(solution);
        if (ConstructionPlaybackStep >= solution.StepCount) return;

        _constructionPlaybackCancellation?.Cancel();
        _constructionPlaybackCancellation = new CancellationTokenSource();
        try
        {
            await AnimateConstructionStepAsync(
                ConstructionPlaybackStep,
                _constructionPlaybackCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetPlaybackVisual(null);
        }
        finally
        {
            IsConstructionPlaybackAnimating = false;
        }
    }

    private void PreviousConstructionStep()
    {
        if (SelectedConstructionSolution?.Solution is not { } solution) return;
        PrepareConstructionPlaybackSession(solution);
        _constructionPlaybackCancellation?.Cancel();
        IsConstructionPlaybackPlaying = false;
        IsConstructionPlaybackAnimating = false;
        SetPlaybackVisual(null);
        if (ConstructionPlaybackStep <= 0) return;
        BuildConstructionPlaybackDocument(ConstructionPlaybackStep - 1);
        ConstructionSearchStatus =
            $"已回到第 {ConstructionPlaybackStep} / {solution.StepCount} 步。";
    }

    private async Task AnimateConstructionStepAsync(int index, CancellationToken cancellationToken)
    {
        RefreshConstructionPlaybackGeometry();
        if (_constructionPlaybackSolution is null ||
            index < 0 ||
            index >= _constructionPlaybackPlans.Count)
            return;

        CaptureConstructionPlaybackHistory();
        IsConstructionPlaybackAnimating = true;
        var plan = _constructionPlaybackPlans[index];
        var color = PlaybackColor(index);
        ConstructionSearchStatus =
            $"动画 {index + 1}/{_constructionPlaybackSolution.StepCount}：{plan.Step.Description}";

        foreach (var point in new[] { plan.Step.PointA.Position, plan.Step.PointB.Position })
            await AnimatePhaseAsync(
                350,
                progress => SetPlaybackVisual(new ConstructionPlaybackVisual(
                    plan.DisplayKind, plan.Start, plan.Guide, plan.End, color, 0,
                    .35 + .65 * System.Math.Sin(progress * System.Math.PI), [point], [])),
                cancellationToken);

        await AnimatePhaseAsync(
            1650,
            progress =>
            {
                var eased = progress * progress * (3 - 2 * progress);
                SetPlaybackVisual(new ConstructionPlaybackVisual(
                    plan.DisplayKind,
                    plan.Start,
                    plan.Guide,
                    plan.End,
                    color,
                    eased,
                    .25,
                    [plan.Step.PointA.Position, plan.Step.PointB.Position],
                    []));
            },
            cancellationToken);

        await AnimatePhaseAsync(
            650,
            progress => SetPlaybackVisual(new ConstructionPlaybackVisual(
                plan.DisplayKind,
                plan.Start,
                plan.Guide,
                plan.End,
                color,
                1,
                1 - progress,
                [],
                plan.RevealedPoints)),
            cancellationToken);

        SetPlaybackVisual(null);
        BuildConstructionPlaybackDocument(index + 1);
        IsConstructionPlaybackAnimating = false;
    }

    private static async Task AnimatePhaseAsync(
        int durationMilliseconds,
        Action<double> update,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = System.Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds,
                0,
                1);
            update(progress);
            if (progress >= 1) break;
            await Task.Delay(16, cancellationToken);
        }
    }

    private void PrepareConstructionPlaybackSession(ConstructionSolution solution)
    {
        if (ReferenceEquals(_constructionPlaybackSourceSolution, solution) &&
            _constructionPlaybackBaseDocument is not null)
        {
            RefreshConstructionPlaybackGeometry();
            return;
        }

        ResetConstructionPlaybackSession(restoreBase: true);
        if (_document is null) return;
        SetDrawingTool(DrawingTools.Select);
        _constructionPlaybackBaseDocument = ProjectSerializer.Clone(_document);
        _constructionPlaybackSourceSolution = solution;
        _constructionPlaybackSolution = solution;
        RefreshConstructionPlaybackGeometry();
        _constructionPlaybackHistoryCaptured = false;
        ConstructionPlaybackStep = 0;
        RaisePlaybackState();
    }

    private void CaptureConstructionPlaybackHistory()
    {
        if (_constructionPlaybackHistoryCaptured ||
            _constructionPlaybackBaseDocument is null)
            return;
        _undo.Push(ProjectSerializer.Clone(_constructionPlaybackBaseDocument));
        _redo.Clear();
        _constructionPlaybackHistoryCaptured = true;
        RefreshCommands();
    }

    private void BuildConstructionPlaybackDocument(
        int completedSteps,
        bool lightweight = false)
    {
        if (_constructionPlaybackBaseDocument is null ||
            _constructionPlaybackSolution is null)
            return;

        completedSteps = System.Math.Clamp(
            completedSteps,
            0,
            _constructionPlaybackSolution.StepCount);
        RefreshConstructionPlaybackGeometry();
        _document = ClonePlaybackBaseWithCurrentState();
        _selectedId = null;
        var elementIds = _document.Objects.ToDictionary(
            item => $"known:{item.Id}",
            item => item.Id,
            StringComparer.Ordinal);
        var pointIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var point in _constructionPlaybackSolution.Steps
                     .SelectMany(step => new[] { step.PointA, step.PointB })
                     .Concat(_constructionPlaybackSolution.CompletedTargets.Select(target => target.Point))
                     .Where(point => point.ExistingPointId is not null))
            pointIds[point.Key] = point.ExistingPointId!;

        for (var index = 0; index < completedSteps; index++)
            AddPlaybackStepCore(
                _constructionPlaybackPlans[index],
                index,
                pointIds,
                elementIds);

        if (completedSteps == _constructionPlaybackSolution.StepCount)
        {
            var completedTargets = _constructionPlaybackSolution.CompletedTargets
                .Select(target => (
                    Target: target,
                    Id: EnsurePlaybackPointCore(target.Point, pointIds, elementIds)))
                .ToList();
            foreach (var completed in completedTargets)
            {
                var target = FindObject(completed.Id)!;
                target.Name = completed.Target.Name;
                target.Label ??= NewLabel(completed.Target.Name);
                target.Label.Text = completed.Target.Name;
                target.Label.Visible = true;
                target.Style ??= new GeometryStyle();
                target.Style.Color = "#DC2626";
                target.Style.PointRadius = 4;
                target.Style.ZIndex = 40;
            }
            _selectedId = completedTargets.LastOrDefault().Id;
        }

        ConstructionPlaybackStep = completedSteps;
        if (lightweight)
            RecalculateDuringPointDrag();
        else
            Recalculate();
    }

    private ProjectDocument ClonePlaybackBaseWithCurrentState()
    {
        if (_constructionPlaybackBaseDocument is null)
            throw new InvalidOperationException("构造演示尚未准备。");
        var document = ProjectSerializer.Clone(_constructionPlaybackBaseDocument);
        if (_document is null) return document;

        var currentParameters = _document.Parameters.ToDictionary(
            parameter => parameter.Id,
            parameter => parameter.Value,
            StringComparer.Ordinal);
        foreach (var parameter in document.Parameters)
            if (currentParameters.TryGetValue(parameter.Id, out var value))
                parameter.Value = value;

        var currentObjects = _document.Objects.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        foreach (var item in document.Objects.Where(item =>
                     item.Type == "fixedPoint" && item.Draggable))
            if (currentObjects.TryGetValue(item.Id, out var current))
            {
                item.X = current.X;
                item.Y = current.Y;
            }
        return document;
    }

    private void RefreshConstructionPlaybackGeometry()
    {
        if (_constructionPlaybackBaseDocument is null ||
            _constructionPlaybackSourceSolution is null)
            return;
        try
        {
            var document = ClonePlaybackBaseWithCurrentState();
            var evaluation = _engine.Evaluate(document);
            if (evaluation.Errors.Count > 0) return;
            var (points, elements) = BuildConstructionSearchSeed(evaluation);
            var targets = document.Solver.Targets
                .Select(target =>
                    ConstructionTargetResolver.Resolve(target, evaluation))
                .ToList();
            if (targets.Count == 0) return;
            var bounds = evaluation.Scene.Canvas;
            var replayed = ConstructionSearchEngine.ReplaySolution(
                _constructionPlaybackSourceSolution,
                new ConstructionSearchScenario(
                    points,
                    elements,
                    targets,
                    "当前拖动位置"),
                new ConstructionSearchOptions(
                    MinSteps: 0,
                    MaxSteps: _constructionPlaybackSourceSolution.StepCount,
                    MaxPointsPerState: SolverUseGrid ? 300 : 160,
                    UseStraightedge: SolverUseStraightedge,
                    UseCompass: SolverUseCompass,
                    MinX: bounds.MinX,
                    MaxX: bounds.MaxX,
                    MinY: bounds.MinY,
                    MaxY: bounds.MaxY));
            if (replayed is null) return;
            _constructionPlaybackSolution = replayed;
            _constructionPlaybackPlans = ConstructionPlaybackPlanner.Create(replayed);
        }
        catch
        {
            // Keep the last valid animation geometry while the pointer passes
            // through a temporary degenerate position.
        }
    }

    private void AddPlaybackStepCore(
        ConstructionPlaybackStepPlan plan,
        int index,
        IDictionary<string, string> pointIds,
        IDictionary<string, string> elementIds)
    {
        var pointA = EnsurePlaybackPointCore(plan.Step.PointA, pointIds, elementIds);
        var pointB = EnsurePlaybackPointCore(plan.Step.PointB, pointIds, elementIds);
        GeometryObjectDefinition item;
        if (plan.DisplayKind == ConstructionDisplayKind.Circle)
        {
            item = CreateObject("circleByCenterPoint", "solverCircle", false);
            item.Center = pointA;
            item.PointA = pointB;
        }
        else
        {
            item = CreateObject(
                "lineThroughPoints",
                plan.DisplayKind switch
                {
                    ConstructionDisplayKind.Segment => "solverSegment",
                    ConstructionDisplayKind.Ray => "solverRay",
                    _ => "solverLine"
                },
                false);
            ConfigurePlaybackStraightedgeDefinition(item, plan, pointA, pointB);
        }
        item.Style = new GeometryStyle
        {
            Color = PlaybackColor(index),
            StrokeWidth = 3.5,
            Opacity = .94,
            ZIndex = 22
        };
        _document!.Objects.Add(item);
        elementIds[plan.Step.Key] = item.Id;
        _selectedId = item.Id;
    }

    internal static void ConfigurePlaybackStraightedgeDefinition(
        GeometryObjectDefinition item,
        ConstructionPlaybackStepPlan plan,
        string pointA,
        string pointB)
    {
        // A straightedge stroke is mathematically an infinite carrier line.
        // DisplayMin/DisplayMax only crop that carrier for the polished
        // segment/ray presentation.  Derived intersections must never use the
        // cropped presentation geometry, otherwise a valid construction can
        // become invalid during playback.
        item.Type = "lineThroughPoints";
        var reverseRay =
            plan.DisplayKind == ConstructionDisplayKind.Ray &&
            plan.Start.DistanceTo(plan.Step.PointB.Position) <= 1e-7;
        item.PointA = reverseRay ? pointB : pointA;
        item.PointB = reverseRay ? pointA : pointB;
        item.DisplayMinParameter = plan.DisplayMinParameter;
        item.DisplayMaxParameter = plan.DisplayMaxParameter;
    }

    private string EnsurePlaybackPointCore(
        ConstructionSearchPoint point,
        IDictionary<string, string> pointIds,
        IDictionary<string, string> elementIds)
    {
        if (pointIds.TryGetValue(point.Key, out var knownId) && FindObject(knownId) is not null)
            return knownId;

        GeometryObjectDefinition item;
        if (point.ParentElementA is not null && point.ParentElementB is not null)
        {
            var gridParent = IsGridSearchKey(point.ParentElementA)
                ? point.ParentElementA
                : IsGridSearchKey(point.ParentElementB) ? point.ParentElementB : null;
            if (gridParent is not null)
            {
                var objectParent = gridParent == point.ParentElementA
                    ? point.ParentElementB
                    : point.ParentElementA;
                if (!elementIds.TryGetValue(objectParent, out var objectId))
                    throw new InvalidOperationException("动画缺少格线交点的几何父对象。");
                var grid = ParseGridSearchKey(gridParent);
                item = CreateObject("gridIntersectionPoint", "solverPoint", true);
                item.ObjectA = objectId;
                item.GridAxis = grid.Axis;
                item.GridValue = FormatNumber(grid.Value);
                item.Selector = point.Selector ?? "first";
            }
            else
            {
                if (!elementIds.TryGetValue(point.ParentElementA, out var objectA) ||
                    !elementIds.TryGetValue(point.ParentElementB, out var objectB))
                    throw new InvalidOperationException("动画缺少交点的几何父对象。");
                item = CreateObject("intersectionPoint", "solverPoint", true);
                item.ObjectA = objectA;
                item.ObjectB = objectB;
                item.Selector = point.Selector ?? "first";
            }
            item.Draggable = false;
        }
        else
        {
            item = CreateFixedPoint(point.Position, draggable: false);
        }
        item.Style = new GeometryStyle
        {
            Color = "#2563EB",
            PointRadius = 2.6,
            ZIndex = 35
        };
        _document!.Objects.Add(item);
        pointIds[point.Key] = item.Id;
        return item.Id;
    }

    private void ResetConstructionPlaybackSession(bool restoreBase)
    {
        _constructionPlaybackCancellation?.Cancel();
        if (restoreBase && _constructionPlaybackBaseDocument is not null)
        {
            _document = ClonePlaybackBaseWithCurrentState();
            _selectedId = null;
            Recalculate();
        }
        _constructionPlaybackBaseDocument = null;
        _constructionPlaybackSourceSolution = null;
        _constructionPlaybackSolution = null;
        _constructionPlaybackPlans = [];
        _constructionPlaybackHistoryCaptured = false;
        _constructionPlaybackStep = 0;
        _isConstructionPlaybackPlaying = false;
        _isConstructionPlaybackAnimating = false;
        SetPlaybackVisual(null);
        RaisePlaybackState();
    }

    private void SetPlaybackVisual(ConstructionPlaybackVisual? visual)
    {
        PlaybackVisual = visual;
        Raise(nameof(PlaybackVisual));
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaisePlaybackState()
    {
        Raise(nameof(ConstructionPlaybackStep),
            nameof(ConstructionPlaybackStepCount),
            nameof(ConstructionPlaybackProgressText),
            nameof(IsConstructionPlaybackPlaying),
            nameof(IsConstructionPlaybackAnimating),
            nameof(ConstructionPlaybackPlayText));
        RaisePlaybackCommands();
    }

    private void RaisePlaybackCommands()
    {
        PlayConstructionSolutionCommand.Refresh();
        PreviousConstructionStepCommand.Refresh();
        NextConstructionStepCommand.Refresh();
        ExportGifCommand.Refresh();
        ExportVideoCommand.Refresh();
    }

    private static string PlaybackColor(int index)
    {
        string[] colors = ["#2563EB", "#7C3AED", "#0EA5E9", "#059669", "#D97706", "#DB2777"];
        return colors[index % colors.Length];
    }

    private static string GridSearchKey(GridLineReference line) =>
        $"grid:{line.Axis}:{FormatNumber(line.Value)}";
    private static bool IsGridSearchKey(string key) =>
        key.StartsWith("grid:", StringComparison.Ordinal);
    private static GridLineReference ParseGridSearchKey(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 3 ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"无效格线引用：{key}");
        return new GridLineReference(parts[1], value);
    }

    private void ExportPng() => Export("png", "PNG 图像 (*.png)|*.png", path =>
        RasterExporter.ExportPng(_evaluation!.Scene, path, _document!.Exports.Width, _document.Exports.Height, _document.Exports.Dpi, _document.Exports.TransparentPng));
    private void ExportJpg() => Export("jpg", "JPEG 图像 (*.jpg)|*.jpg", path =>
        RasterExporter.ExportJpeg(_evaluation!.Scene, path, _document!.Exports.Width, _document.Exports.Height, _document.Exports.Dpi, _document.Exports.JpegQuality, _document.Exports.Background));
    private void ExportEmf() => Export("emf", "增强型图元文件 (*.emf)|*.emf", path =>
        EmfExporter.Export(_evaluation!.Scene, path, _document!.Exports.Width, _document.Exports.Height, _document.Exports.EmfTextMode));

    private void ExportConstructionGif()
    {
        if (_document is null ||
            SelectedConstructionSolution?.Solution is not { } solution)
            return;
        Export("gif", "GIF 构造动画 (*.gif)|*.gif", path =>
        {
            ExportStatus = "正在生成构造动画…";
            var sourceWidth = _document.Exports.Width;
            var sourceHeight = _document.Exports.Height;
            var scale = System.Math.Min(
                1,
                720d / System.Math.Max(sourceWidth, sourceHeight));
            var width = System.Math.Max(1, (int)System.Math.Round(sourceWidth * scale));
            var height = System.Math.Max(1, (int)System.Math.Round(sourceHeight * scale));
            var frames = CreateConstructionAnimationFrames(solution);
            AnimatedGifExporter.Export(frames, path, width, height, 96);
        });
    }

    private async Task ExportConstructionVideoAsync()
    {
        if (_document is null || SelectedConstructionSolution?.Solution is not { } solution || IsAnimationExporting)
            return;
        var dialog = new VideoExportWindow { Owner = System.Windows.Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;
        var path = _dialogs.SaveExport("mp4", "高清视频 (*.mp4)|*.mp4");
        if (path is null) return;
        _animationExportCancellation = new CancellationTokenSource();
        var token = _animationExportCancellation.Token;
        IsAnimationExporting = true;
        try
        {
            ExportStatus = "正在准备高清视频…";
            var sourceWidth = _document.Exports.Width;
            var sourceHeight = _document.Exports.Height;
            var contentAspectRatio = sourceWidth / (double)sourceHeight;
            var (width, height) = AnimatedVideoExporter.GetCompatibleFrameSize(dialog.LongEdge, contentAspectRatio);
            var fps = dialog.FramesPerSecond;
            var frames = CreateConstructionAnimationFrames(solution, fps, dialog.Speed);
            // WPF rendering needs its own STA. Only scene snapshots cross the thread boundary;
            // progress is marshalled back, keeping the window responsive and cancellation usable.
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                if (IsAnimationExporting)
                    ExportStatus = $"正在导出 MP4 {width}×{height} · {fps} 帧/秒 · {p.Done * 100 / p.Total}%";
            });
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                try
                {
                    AnimatedVideoExporter.Export(frames, path, width, height, fps,
                        (done, total) => ((IProgress<(int, int)>)progress).Report((done, total)), token, contentAspectRatio);
                    completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
            }) { IsBackground = true, Name = "GeometryGrid MP4 export" };
            worker.SetApartmentState(ApartmentState.STA); worker.Start();
            await completion.Task;
            ExportStatus = $"已导出高清视频：{Path.GetFileName(path)}（{width}×{height}，{fps} 帧/秒）";
        }
        catch (OperationCanceledException) { ExportStatus = "视频导出已取消，原文件未被覆盖。"; }
        catch (Exception ex) { ExportStatus = "视频导出失败"; _dialogs.ShowError("视频导出失败", ex.Message); }
        finally
        {
            IsAnimationExporting = false;
            _animationExportCancellation?.Dispose(); _animationExportCancellation = null;
        }
    }

    internal IReadOnlyList<AnimatedGifFrame> CreateConstructionAnimationFrames(
        ConstructionSolution solution, int frameRate = 12, double speed = 1)
    {
        if (_document is null)
            throw new InvalidOperationException("当前项目尚未加载。");
        var originalDocument = ProjectSerializer.Clone(_document);
        var originalSelectedId = _selectedId;
        var originalPlaybackStep = ConstructionPlaybackStep;
        var originalPlaybackVisual = PlaybackVisual;
        var originalBase = _constructionPlaybackBaseDocument;
        var originalSource = _constructionPlaybackSourceSolution;
        var originalSolution = _constructionPlaybackSolution;
        var originalPlans = _constructionPlaybackPlans;
        var originalHistory = _constructionPlaybackHistoryCaptured;
        var originalTool = _activeTool;
        var originalInstruction = _toolInstruction;
        var originalPicks = _pointPicks.ToArray();
        var originalLine = _toolLineId;
        var originalGrid = _toolGridLine;
        var originalPreview = Preview;
        var frames = new List<AnimatedGifFrame>();
        void Phase(double seconds, Func<double, RenderScene> scene)
        {
            var count = Math.Max(1, (int)Math.Ceiling(seconds * frameRate / speed));
            var total = (int)Math.Round(seconds * 100 / speed);
            for (var i = 1; i <= count; i++)
            {
                var delay = (int)Math.Round(i * total / (double)count) -
                            (int)Math.Round((i - 1) * total / (double)count);
                frames.Add(new AnimatedGifFrame(scene(i / (double)count), Math.Max(1, delay), seconds / speed / count));
            }
        }

        try
        {
            PrepareConstructionPlaybackSession(solution);
            var baseIds = (_constructionPlaybackBaseDocument?.Objects ?? [])
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var stepStyles = originalDocument.Objects
                .Where(item =>
                    !baseIds.Contains(item.Id) &&
                    IsPlaybackCarrier(item))
                .Take(solution.StepCount)
                .Select(item => StyleResolver.Resolve(originalDocument, item))
                .ToList();

            BuildConstructionPlaybackDocument(0);
            ApplyConstructionAnimationArtwork(
                originalDocument,
                includeMissingObjects: false);
            if (_evaluation is null)
                throw new InvalidOperationException("无法生成构造动画的初始画面。");
            frames.Add(new AnimatedGifFrame(WithAnimationCaption(_evaluation.Scene, "已知图 · 先观察题目中的点、线与网格"),
                (int)Math.Round(100 / speed), 1 / speed));

            for (var index = 0; index < _constructionPlaybackPlans.Count; index++)
            {
                BuildConstructionPlaybackDocument(index);
                ApplyConstructionAnimationArtwork(
                    originalDocument,
                    includeMissingObjects: false);
                if (_evaluation is null) continue;
                var plan = _constructionPlaybackPlans[index];
                var stepStyle = index < stepStyles.Count
                    ? stepStyles[index]
                    : null;
                var color = stepStyle?.Color ?? PlaybackColor(index);

                var before = _evaluation.Scene;
                var title = $"第 {index + 1}/{_constructionPlaybackPlans.Count} 步 · {plan.Step.Description}";
                for (var focus = 0; focus < 2; focus++)
                {
                    var selectedFocus = focus;
                    Phase(.35, t => WithAnimationCaption(CreateAnimationScene(before, plan, color, stepStyle,
                        0, .35 + .65 * Math.Sin(t * Math.PI), false, selectedFocus),
                        title + (selectedFocus == 0 ? " · 选择第一个点" : " · 选择第二个点")));
                }
                Phase(1.65, t => WithAnimationCaption(CreateAnimationScene(before, plan, color, stepStyle,
                    t * t * (3 - 2 * t), .18, false), title + " · 绘制"));

                BuildConstructionPlaybackDocument(index + 1);
                ApplyConstructionAnimationArtwork(
                    originalDocument,
                    includeMissingObjects:
                        index + 1 == _constructionPlaybackPlans.Count);
                if (_evaluation is null) continue;
                var after = _evaluation.Scene;
                Phase(.65, t => WithAnimationCaption(CreateAnimationScene(after, plan, color, stepStyle,
                    1, 1 - t, true), title + (plan.RevealedPoints.Count > 0 ? " · 确认新交点" : " · 本步完成")));
                frames.Add(new AnimatedGifFrame(WithAnimationCaption(after, title + " · 本步完成"),
                    (int)Math.Round(40 / speed), .4 / speed));
            }

            if (_evaluation is not null)
                frames.Add(new AnimatedGifFrame(WithAnimationCaption(_evaluation.Scene,
                    $"构造完成 · 共 {solution.StepCount} 笔 · {solution.TargetName}"), (int)Math.Round(240 / speed), 2.4 / speed));
            return frames;
        }
        finally
        {
            _document = originalDocument;
            _selectedId = originalSelectedId;
            _constructionPlaybackBaseDocument = originalBase;
            _constructionPlaybackSourceSolution = originalSource;
            _constructionPlaybackSolution = originalSolution;
            _constructionPlaybackPlans = originalPlans;
            _constructionPlaybackHistoryCaptured = originalHistory;
            _activeTool = originalTool; _toolInstruction = originalInstruction;
            _pointPicks.Clear(); _pointPicks.AddRange(originalPicks);
            _toolLineId = originalLine; _toolGridLine = originalGrid; Preview = originalPreview;
            Raise(nameof(ActiveTool), nameof(ToolInstruction), nameof(Preview));
            ConstructionPlaybackStep = originalPlaybackStep;
            SetPlaybackVisual(originalPlaybackVisual);
            Recalculate();
        }
    }

    private static RenderScene WithAnimationCaption(RenderScene scene, string caption) => new()
    {
        Canvas = scene.Canvas, Grid = scene.Grid, Items = scene.Items, AnimationCaption = caption
    };

    private void ApplyConstructionAnimationArtwork(
        ProjectDocument artwork,
        bool includeMissingObjects)
    {
        if (_document is null)
            return;
        ConstructionAnimationFormatting.Apply(
            _document,
            artwork,
            includeMissingObjects);
        Recalculate();
    }

    private static bool IsPlaybackCarrier(GeometryObjectDefinition item) =>
        (item.Id.StartsWith("solverSegment", StringComparison.Ordinal) ||
         item.Id.StartsWith("solverRay", StringComparison.Ordinal) ||
         item.Id.StartsWith("solverLine", StringComparison.Ordinal) ||
         item.Id.StartsWith("solverCircle", StringComparison.Ordinal)) &&
        item.Type is "segment" or "ray" or "infiniteLine" or
            "lineThroughPoints" or "circleByCenterPoint";

    internal static RenderScene CreateAnimationScene(
        RenderScene scene,
        ConstructionPlaybackStepPlan plan,
        string color,
        ResolvedStyle? stepStyle,
        double progress,
        double pulse,
        bool showRevealed,
        int? focusPointIndex = null)
    {
        var items = scene.Items.ToList();
        var overlayIndex = items.Count;
        if (!showRevealed && progress > 1e-6 && (stepStyle?.Visible ?? true))
        {
            GeometryResult geometry;
            if (plan.DisplayKind == ConstructionDisplayKind.Circle)
            {
                geometry = new GeometryResult
                {
                    ObjectId = $"animation-overlay-{overlayIndex}",
                    Type = "arc",
                    IsValid = true,
                    Arc = progress < 1 ? new ArcGeometry(
                        plan.Start,
                        plan.Start.DistanceTo(plan.Guide), 0, 2 * Math.PI * progress) : null,
                    Circle = progress >= 1 ? new CircleGeometry(plan.Start, plan.Start.DistanceTo(plan.Guide)) : null
                };
            }
            else
            {
                var end = plan.Start + (plan.End - plan.Start) * progress;
                geometry = new GeometryResult
                {
                    ObjectId = $"animation-overlay-{overlayIndex}",
                    Type = "segment",
                    IsValid = true,
                    Line = new LineGeometry(
                        plan.Start,
                        end - plan.Start,
                        0,
                        1)
                };
            }
            items.Add(new RenderItem
            {
                Id = geometry.ObjectId,
                Type = geometry.Type,
                Geometry = geometry,
                Style = new ResolvedStyle(
                    true,
                    stepStyle?.StrokeWidth ?? 4.6,
                    stepStyle?.StrokeStyle ?? "solid",
                    stepStyle?.Color ?? color,
                    stepStyle?.Opacity ?? .98,
                    0,
                    stepStyle?.ZIndex ?? 90)
            });
        }

        if (pulse > 0 && progress > 0 && progress < 1 && (stepStyle?.Visible ?? true))
        {
            var nib = plan.DisplayKind == ConstructionDisplayKind.Circle
                ? plan.Start + new Vector2(Math.Cos(progress * Math.Tau), Math.Sin(progress * Math.Tau)) * plan.Start.DistanceTo(plan.Guide)
                : plan.Start + (plan.End - plan.Start) * progress;
            foreach (var (radius, opacity) in new[] { (8d, .16), (3.2, .95) })
                items.Add(new RenderItem
                {
                    Id = $"animation-nib-{overlayIndex++}", Type = "point",
                    Geometry = new GeometryResult { ObjectId = "animation-nib", Type = "point", IsValid = true, Point = nib },
                    Style = new ResolvedStyle(true, 0, "solid", color, opacity, radius, 101)
                });
        }

        if (pulse > 0)
        {
            var focusPoints = showRevealed
                ? plan.RevealedPoints
                : focusPointIndex is { } focus
                    ? new[] { focus == 0 ? plan.Step.PointA.Position : plan.Step.PointB.Position }
                    : new[] { plan.Step.PointA.Position, plan.Step.PointB.Position };
            foreach (var point in focusPoints)
            {
                var geometry = new GeometryResult
                {
                    ObjectId = $"animation-point-{overlayIndex++}",
                    Type = "point",
                    IsValid = true,
                    Point = point
                };
                items.Add(new RenderItem
                {
                    Id = geometry.ObjectId,
                    Type = geometry.Type,
                    Geometry = geometry,
                    Style = new ResolvedStyle(
                        true,
                        0,
                        "solid",
                        showRevealed ? "#F59E0B" : color,
                        .12 + .15 * pulse,
                        9 + 7 * (1 - pulse),
                        100)
                });
                foreach (var (dotColor, radius) in new[] { ("#FFFFFF", 4.2), (showRevealed ? "#DC2626" : color, 2.8) })
                    items.Add(new RenderItem
                    {
                        Id = $"animation-focus-{overlayIndex++}", Type = "point", Geometry = geometry,
                        Style = new ResolvedStyle(true, 0, "solid", dotColor, 1, radius, 101)
                    });
            }
        }

        return new RenderScene
        {
            Canvas = scene.Canvas,
            Grid = scene.Grid,
            AnimationCaption = scene.AnimationCaption,
            Items = items
        };
    }

    private void Export(string extension, string filter, Action<string> action)
    {
        var path = _dialogs.SaveExport(extension, filter);
        if (path is null) return;
        try { action(path); ExportStatus = $"已导出：{Path.GetFileName(path)}"; }
        catch (Exception ex) { ExportStatus = "导出失败"; _dialogs.ShowError("导出失败", ex.Message); }
    }

    private void SetStyle(Action<GeometryStyle> mutation)
    {
        if (SelectedObject is null) return;
        Apply(() => { SelectedObject.Style ??= new GeometryStyle(); mutation(SelectedObject.Style); });
    }

    private void SetLabel(Action<LabelDefinition> mutation)
    {
        if (SelectedObject is null) return;
        Apply(() =>
        {
            SelectedObject.Label ??= NewLabel(UserDisplayName(SelectedObject));
            mutation(SelectedObject.Label);
        });
    }

    private void Apply(Action mutation) { BeginEdit(); mutation(); EndEdit(); }

    private void Recalculate(bool refreshCommands = true)
    {
        if (_document is null) return;
        try
        {
            _evaluation = _engine.Evaluate(_document);
            ValidationStatus = _evaluation.Errors.Count == 0 ? string.Empty : string.Join(" · ", _evaluation.Errors);
        }
        catch (Exception ex) { _evaluation = null; ValidationStatus = ex.Message; }
        Raise(nameof(Document), nameof(Evaluation), nameof(ParameterStatus));
        if (_document is not null)
        {
            CanvasWidthInput = FormatNumber(_document.Canvas.MaxX - _document.Canvas.MinX);
            CanvasHeightInput = FormatNumber(_document.Canvas.MaxY - _document.Canvas.MinY);
            Raise(nameof(CanvasWidthInput), nameof(CanvasHeightInput), nameof(CanvasOriginText));
        }
        RefreshObjectEntries();
        RaiseSelection();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        if (refreshCommands) RefreshCommands();
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectedObject), nameof(HasSelection), nameof(NoSelectionVisibility), nameof(SelectionVisibility), nameof(SelectionStatus),
            nameof(SelectedDisplayName),
            nameof(SelectedVisible), nameof(SelectedStrokeWidth), nameof(SelectedStrokeStyle), nameof(SelectedColor),
            nameof(SelectedOpacity), nameof(SelectedPointRadius), nameof(SelectedZIndex), nameof(SelectedLabelVisible),
            nameof(SelectedFontSize), nameof(SelectedOffsetX), nameof(SelectedOffsetY));
        RestoreSelectedLabelCommand.Refresh();
        DeleteSelectedCommand.Refresh();
        PickColorCommand.Refresh();
        SetCommonColorCommand.Refresh();
        ApplyDefinitionCommand.Refresh();
    }

    private void RefreshObjectEntries()
    {
        var ids = _document?.Objects.Select(o => o.Id).ToList() ?? [];
        if (!ObjectEntries.Select(entry => entry.Id).SequenceEqual(ids))
        {
            ObjectEntries.Clear();
            foreach (var id in ids) ObjectEntries.Add(new ObjectEntryViewModel(this, id));
        }
        foreach (var entry in ObjectEntries) entry.Refresh();
        _selectedObjectEntry = ObjectEntries.FirstOrDefault(entry => entry.Id == _selectedId);
        Raise(nameof(SelectedObjectEntry));
        if (!_definitionDirty) UpdateDefinitionEditor(false);
    }

    private void UpdateDefinitionEditor(bool force)
    {
        if (!force && _definitionDirty) return;
        _selectedDefinitionJson = SelectedObject is null
            ? ""
            : JsonSerializer.Serialize(SelectedObject, ProjectSerializer.Options);
        _definitionDirty = false;
        Raise(nameof(SelectedDefinitionJson));
    }

    private void RefreshCommands()
    {
        foreach (var command in new[] { ReloadCommand, SaveCommand, SaveAsCommand, UndoCommand, RedoCommand, DeleteSelectedCommand,
                     RestoreLayoutCommand, ApplyCanvasSizeCommand, ExportPngCommand, ExportJpgCommand, ExportEmfCommand,
                     ExportGifCommand, ExportVideoCommand, CancelAnimationExportCommand,
                     StartConstructionSearchCommand, PlayConstructionSolutionCommand,
                     PreviousConstructionStepCommand, NextConstructionStepCommand,
                     ImportConstructionStepsCommand, ExportConstructionStepsCommand,
                     AddObjectCommand })
            command.Refresh();
    }

    private readonly record struct PointPick(string? ExistingId, Point2 World);
    private readonly record struct SmartIntersection(
        Point2 Point,
        string ObjectA,
        string? ObjectB,
        GridLineReference? Grid,
        string Selector);
}
