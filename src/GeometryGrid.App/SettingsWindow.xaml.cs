using System.Windows;
using GeometryGrid.App.Services;

namespace GeometryGrid.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LiquidGlassWindow.Apply(this, transient: true);
        GpuAccelerationCheckBox.IsChecked =
            AppPreferences.Current.ExperimentalGpuAcceleration;
        LiquidGlassCheckBox.IsChecked =
            AppPreferences.Current.LiquidGlassEnabled;
        Loaded += async (_, _) => await ProbeGpuAsync(runBenchmark: false);
    }

    private async void BenchmarkButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ProbeGpuAsync(runBenchmark: true);

    private async Task ProbeGpuAsync(bool runBenchmark)
    {
        BenchmarkButton.IsEnabled = false;
        GpuStatusText.Text = "正在检测 DirectX 12 计算设备…";
        GpuBenchmarkText.Text = string.Empty;
        try
        {
            var result = await Task.Run(() =>
            {
                var available = GpuAccelerationManager.TryGet(
                    out var accelerator,
                    out var status);
                var pairsPerSecond =
                    available && runBenchmark && accelerator is not null
                        ? accelerator.RunBenchmark()
                        : 0;
                return (available, accelerator, status, pairsPerSecond);
            });

            GpuStatusText.Text = result.status;
            if (result.available && result.accelerator is not null)
            {
                GpuStatusText.Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(21, 128, 61));
                if (runBenchmark)
                    GpuBenchmarkText.Text =
                        $"本机批处理：{FormatRate(result.pairsPerSecond)} 候选点对/秒";
            }
            else
            {
                GpuStatusText.Foreground =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(180, 83, 9));
            }
        }
        catch (Exception ex)
        {
            GpuStatusText.Text = $"检测失败：{ex.Message}";
            GpuStatusText.Foreground =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(185, 28, 28));
        }
        finally
        {
            BenchmarkButton.IsEnabled = true;
        }
    }

    private static string FormatRate(double value) =>
        value >= 10_000
            ? $"{value / 10_000:N1} 万"
            : $"{value:N0}";

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        AppPreferences.Save(
            GpuAccelerationCheckBox.IsChecked == true,
            LiquidGlassCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
