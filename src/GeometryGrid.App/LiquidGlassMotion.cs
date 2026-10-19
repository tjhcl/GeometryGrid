using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace GeometryGrid.App;

/// <summary>
/// Lightweight spring-like feedback for buttons. Animations run only while a
/// pointer is interacting with a control, mirroring the needs-redraw principle
/// used by the WebGL reference instead of keeping a permanent render loop alive.
/// </summary>
internal static class LiquidGlassMotion
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
            return;

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(WpfButtonBase),
            Mouse.MouseEnterEvent,
            new System.Windows.Input.MouseEventHandler(OnMouseEnter));
        EventManager.RegisterClassHandler(
            typeof(WpfButtonBase),
            Mouse.MouseLeaveEvent,
            new System.Windows.Input.MouseEventHandler(OnMouseLeave));
        EventManager.RegisterClassHandler(
            typeof(WpfButtonBase),
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnMouseDown));
        EventManager.RegisterClassHandler(
            typeof(WpfButtonBase),
            Mouse.PreviewMouseUpEvent,
            new MouseButtonEventHandler(OnMouseUp));
        EventManager.RegisterClassHandler(
            typeof(WpfCheckBox),
            ToggleButton.CheckedEvent,
            new RoutedEventHandler(OnCheckChanged));
        EventManager.RegisterClassHandler(
            typeof(WpfCheckBox),
            ToggleButton.UncheckedEvent,
            new RoutedEventHandler(OnCheckChanged));
    }

    private static void OnMouseEnter(object sender, WpfMouseEventArgs e)
    {
        if (sender is WpfButtonBase { IsEnabled: true } button)
            Animate(button, 1.025, 145, EasingMode.EaseOut);
    }

    private static void OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (sender is WpfButtonBase button)
            Animate(button, 1.0, 190, EasingMode.EaseOut);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            sender is WpfButtonBase { IsEnabled: true } button)
            Animate(button, 0.955, 85, EasingMode.EaseIn);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            sender is WpfButtonBase { IsEnabled: true } button)
            Animate(button, button.IsMouseOver ? 1.025 : 1.0, 210, EasingMode.EaseOut);
    }

    private static void OnCheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfCheckBox { IsLoaded: true } checkBox)
            return;

        var template = checkBox.Template;
        if (template is null ||
            template.FindName("SwitchThumb", checkBox) is not FrameworkElement thumb)
            return;

        var isChecked = checkBox.IsChecked == true;
        var target = isChecked
            ? new Thickness(19.5, 1.5, 1.5, 1.5)
            : new Thickness(1.5);
        thumb.BeginAnimation(
            FrameworkElement.MarginProperty,
            new ThicknessAnimation(target, TimeSpan.FromMilliseconds(isChecked ? 205 : 180))
            {
                EasingFunction = isChecked
                    ? new BackEase { Amplitude = .16, EasingMode = EasingMode.EaseOut }
                    : new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void Animate(
        WpfButtonBase button,
        double target,
        int milliseconds,
        EasingMode easingMode)
    {
        button.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        var scale = button.RenderTransform as ScaleTransform;
        if (scale is null ||
            scale.IsFrozen ||
            button.ReadLocalValue(UIElement.RenderTransformProperty) ==
            DependencyProperty.UnsetValue)
        {
            scale = scale?.CloneCurrentValue() ?? new ScaleTransform(1, 1);
            button.RenderTransform = scale;
        }

        var easing = new BackEase
        {
            Amplitude = target < 1 ? .08 : .18,
            EasingMode = easingMode
        };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }
}
