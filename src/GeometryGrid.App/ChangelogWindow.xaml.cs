using System.Windows;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace GeometryGrid.App;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
        LiquidGlassWindow.Apply(this, transient: true);
    }

    private void OpenLiquidGlassLicense_Click(object sender, RoutedEventArgs e)
    {
        const string resourceName = "GeometryGrid.App.ThirdParty.liquid-glass-webgl.LICENSE";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        using var reader = stream is null ? null : new StreamReader(stream);
        var licenseText = reader?.ReadToEnd() ?? "未找到内置许可证文本。";

        var viewer = new Window
        {
            Title = "liquid-glass-webgl · Apache License 2.0",
            Owner = this,
            Width = 820,
            Height = 680,
            MinWidth = 620,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("AppBackdropBrush"),
            Content = new System.Windows.Controls.TextBox
            {
                Text = licenseText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(18),
                Padding = new Thickness(16),
            },
        };
        LiquidGlassWindow.Apply(viewer, transient: true);
        viewer.ShowDialog();
    }
}
