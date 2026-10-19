using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GeometryGrid.App;

/// <summary>Small, native, offline export configuration dialog.</summary>
public sealed class VideoExportWindow : Window
{
    private readonly ComboBox _quality = new();
    private readonly ComboBox _fps = new();
    private readonly ComboBox _speed = new();
    public int LongEdge => _quality.SelectedIndex == 1 ? 3840 : 1920;
    public int FramesPerSecond => _fps.SelectedIndex == 1 ? 60 : 30;
    public double Speed => new[] { .75, 1d, 1.25, 1.5 }[_speed.SelectedIndex];

    public VideoExportWindow()
    {
        Title = "导出高清视频";
        Width = 460; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 253));
        FontFamily = TryFindResource("AppFontFamily") as FontFamily ?? new FontFamily("Segoe UI");
        FontSize = 14;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        UseLayoutRounding = true;
        var stack = new StackPanel { Margin = new Thickness(28) };
        stack.Children.Add(new TextBlock { Text = "清晰地讲完每一步", FontSize = 23, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 42, 68)) });
        stack.Children.Add(new TextBlock { Text = "MP4 · H.264 · 离屏渲染，不录入界面或鼠标。\n标准横/竖屏画幅，原画布居中等比放入；必要时留白，不拉伸、不裁切。保留当前颜色、虚线和标签。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18) });
        AddChoice(stack, "画质（横屏尺寸；竖屏自动交换宽高）", _quality, ["高清 · 1920 × 1080", "4K · 3840 × 2160"]);
        AddChoice(stack, "流畅度", _fps, ["30 帧 / 秒（推荐）", "60 帧 / 秒"]);
        AddChoice(stack, "播放速度", _speed, ["0.75× · 更慢", "1× · 舒缓讲解（推荐）", "1.25×", "1.5×"]);
        _speed.SelectedIndex = 1;
        stack.Children.Add(new TextBlock { Text = "每笔约 3 秒，包含取点、绘线和交点确认。\n视频无音轨；导出时可取消，失败不会覆盖已有文件。",
            FontSize = 12, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 12) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true };
        var export = new Button { Content = "导出 MP4", IsDefault = true };
        export.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(export); stack.Children.Add(buttons);
        Content = stack;
    }

    private static void AddChoice(Panel parent, string label, ComboBox choice, string[] items)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 5) });
        choice.ItemsSource = items; choice.SelectedIndex = 0; choice.MinHeight = 34;
        parent.Children.Add(choice);
    }
}
