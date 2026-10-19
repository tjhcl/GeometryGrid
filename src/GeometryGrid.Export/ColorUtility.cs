namespace GeometryGrid.Export;

internal static class ColorUtility
{
    public static System.Windows.Media.Color Wpf(string color, double opacity = 1)
    {
        var parsed = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        parsed.A = (byte)System.Math.Clamp(System.Math.Round(parsed.A * opacity), 0, 255);
        return parsed;
    }

    public static System.Drawing.Color Gdi(string color, double opacity = 1)
    {
        var wpf = Wpf(color, opacity);
        return System.Drawing.Color.FromArgb(wpf.A, wpf.R, wpf.G, wpf.B);
    }
}
