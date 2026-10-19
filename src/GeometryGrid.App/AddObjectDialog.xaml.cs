using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using GeometryGrid.Core.Models;
using GeometryGrid.Core.Serialization;

namespace GeometryGrid.App;

public partial class AddObjectDialog : Window
{
    private readonly ProjectDocument _document;

    public AddObjectDialog(ProjectDocument document)
    {
        InitializeComponent();
        LiquidGlassWindow.Apply(this, transient: true);
        _document = document;
        IdBox.Text = NextId(document, "object");
        TypeBox.SelectedIndex = 0;
    }

    public GeometryObjectDefinition? Result { get; private set; }

    private void GenerateJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            JsonBox.Text = JsonSerializer.Serialize(BuildFromForm(), ProjectSerializer.Options);
            EditorTabs.SelectedIndex = 1;
            ErrorText.Text = "";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = EditorTabs.SelectedIndex == 1
                ? JsonSerializer.Deserialize<GeometryObjectDefinition>(
                    JsonBox.Text,
                    ProjectSerializer.Options)
                  ?? throw new InvalidOperationException("JSON 对象不能为空。")
                : BuildFromForm();
            if (string.IsNullOrWhiteSpace(result.Id))
                throw new InvalidOperationException("对象 ID 不能为空。");
            if (string.IsNullOrWhiteSpace(result.Type))
                throw new InvalidOperationException("对象类型不能为空。");
            if (_document.Objects.Any(item =>
                    string.Equals(item.Id, result.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"对象 ID“{result.Id}”已经存在。");
            Result = result;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private GeometryObjectDefinition BuildFromForm()
    {
        var type = TypeBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString()
            : TypeBox.Text;
        if (!double.TryParse(
                StrokeWidthBox.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var strokeWidth) ||
            !double.IsFinite(strokeWidth) ||
            strokeWidth <= 0)
            throw new InvalidOperationException("线宽必须是大于 0 的数字。");
        var name = EmptyToNull(NameBox.Text);
        return new GeometryObjectDefinition
        {
            Id = IdBox.Text.Trim(),
            Name = name,
            Type = type?.Trim() ?? "",
            X = EmptyToNull(XBox.Text),
            Y = EmptyToNull(YBox.Text),
            PointA = EmptyToNull(PointABox.Text),
            PointB = EmptyToNull(PointBBox.Text),
            PointC = EmptyToNull(PointCBox.Text),
            Center = EmptyToNull(CenterBox.Text),
            ObjectA = EmptyToNull(ObjectABox.Text),
            ObjectB = EmptyToNull(ObjectBBox.Text),
            Radius = EmptyToNull(RadiusBox.Text),
            Selector = EmptyToNull(SelectorBox.Text),
            Draggable = DraggableCheck.IsChecked == true,
            Style = new GeometryStyle
            {
                Visible = VisibleCheck.IsChecked == true,
                Color = string.IsNullOrWhiteSpace(ColorBox.Text) ? "#000000" : ColorBox.Text.Trim(),
                StrokeWidth = strokeWidth
            },
            Label = name is null
                ? null
                : new LabelDefinition
                {
                    Text = name,
                    Visible = LabelCheck.IsChecked == true,
                    FontSize = 45
                }
        };
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NextId(ProjectDocument document, string prefix)
    {
        for (var index = 1; ; index++)
        {
            var id = $"{prefix}{index}";
            if (document.Objects.All(item =>
                    !string.Equals(item.Id, id, StringComparison.Ordinal)))
                return id;
        }
    }
}
