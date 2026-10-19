using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace GeometryGrid.App.Services;

public interface IFileDialogService
{
    string? OpenJson();
    string? SaveJson(string? currentPath);
    string? OpenConstructionSteps() => null;
    string? SaveConstructionSteps(string? currentProjectPath) => null;
    string? SaveExport(string extension, string filter);
    string? PickColor(string current);
    string? PromptText(string title, string prompt, string initialValue);
    void ShowError(string title, string message);
}

public sealed class FileDialogService(Window owner) : IFileDialogService
{
    public string? OpenJson()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "打开 GeometryGrid JSON", Filter = "GeometryGrid JSON (*.json)|*.json|所有文件 (*.*)|*.*" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveJson(string? currentPath)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "保存 GeometryGrid JSON",
            Filter = "GeometryGrid JSON (*.json)|*.json",
            FileName = currentPath is null ? "geometry.json" : Path.GetFileName(currentPath),
            InitialDirectory = currentPath is null ? null : Path.GetDirectoryName(currentPath)
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? OpenConstructionSteps()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 GeometryGrid 作图步骤",
            Filter = "GeometryGrid 作图步骤 (*.ggsteps.json)|*.ggsteps.json|JSON 文件 (*.json)|*.json"
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveConstructionSteps(string? currentProjectPath)
    {
        var baseName = currentProjectPath is null
            ? "geometry"
            : Path.GetFileNameWithoutExtension(currentProjectPath);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 GeometryGrid 作图步骤",
            DefaultExt = "json",
            Filter = "GeometryGrid 作图步骤 (*.ggsteps.json)|*.ggsteps.json",
            FileName = $"{baseName}.ggsteps.json",
            InitialDirectory = currentProjectPath is null
                ? null
                : Path.GetDirectoryName(currentProjectPath)
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveExport(string extension, string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = $"导出 {extension.ToUpperInvariant()}", DefaultExt = extension, Filter = filter, FileName = $"geometry.{extension}" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickColor(string current)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(current)
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
        return $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    public string? PromptText(string title, string prompt, string initialValue)
    {
        var input = new System.Windows.Controls.TextBox
        {
            Text = initialValue,
            Margin = new Thickness(0, 8, 0, 12),
            MinWidth = 320
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = "确定",
            IsDefault = true,
            MinWidth = 82,
            Margin = new Thickness(4, 0, 0, 0)
        };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "取消",
            IsCancel = true,
            MinWidth = 82,
            Margin = new Thickness(4, 0, 0, 0)
        };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    public void ShowError(string title, string message) => System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
