using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GeometryGrid.App.Services;
using GeometryGrid.App.ViewModels;

namespace GeometryGrid.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _circleLongPressTimer;
    private readonly LiquidGlassWebHost _liquidGlassWebHost;
    private bool _circleLongPressTriggered;

    public MainWindow()
    {
        InitializeComponent();
        LiquidGlassWindow.Apply(this);
        _liquidGlassWebHost = new LiquidGlassWebHost(LiquidGlassWebView);
        _liquidGlassWebHost.Ready += (_, _) =>
        {
            if (!AppPreferences.Current.LiquidGlassEnabled)
                return;
            LiquidGlassWebLayer.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            LiquidGlassWebLayer.BeginAnimation(OpacityProperty, fadeIn);
        };
        _circleLongPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _circleLongPressTimer.Tick += (_, _) =>
        {
            _circleLongPressTimer.Stop();
            _circleLongPressTriggered = true;
            CircleToolsPopup.IsOpen = true;
        };
        var viewModel = new MainViewModel(new FileDialogService(this));
        DataContext = viewModel;
        SourceInitialized += async (_, _) => await ApplyLiquidGlassPreferenceAsync();
        Loaded += (_, _) => viewModel.OpenDefaultExample();
        Closed += async (_, _) => await _liquidGlassWebHost.DisposeAsync();
    }

    private void CircleToolButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _circleLongPressTriggered = false;
        _circleLongPressTimer.Stop();
        _circleLongPressTimer.Start();
    }

    private void CircleToolButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _circleLongPressTimer.Stop();
    }

    private void CircleToolButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _circleLongPressTimer.Stop();
    }

    private void CircleToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (_circleLongPressTriggered)
        {
            _circleLongPressTriggered = false;
            return;
        }
        if (DataContext is MainViewModel viewModel)
            viewModel.SetToolCommand.Execute(DrawingTools.CircleRadiusSegment);
    }

    private void CircleChoice_Click(object sender, RoutedEventArgs e) => CircleToolsPopup.IsOpen = false;

    private void OpenChangelog_Click(object sender, RoutedEventArgs e) =>
        new ChangelogWindow { Owner = this }.ShowDialog();

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (new SettingsWindow { Owner = this }.ShowDialog() == true)
            await ApplyLiquidGlassPreferenceAsync();
    }

    private async Task ApplyLiquidGlassPreferenceAsync()
    {
        var enabled = AppPreferences.Current.LiquidGlassEnabled;
        if (!enabled)
        {
            LiquidGlassWebLayer.BeginAnimation(OpacityProperty, null);
            LiquidGlassWebLayer.Opacity = 0;
            LiquidGlassWebLayer.Visibility = Visibility.Collapsed;
        }
        else
        {
            LiquidGlassWebLayer.Visibility = Visibility.Visible;
            LiquidGlassWebLayer.Opacity = 0;
        }
        await _liquidGlassWebHost.SetEnabledAsync(enabled);
    }
}
