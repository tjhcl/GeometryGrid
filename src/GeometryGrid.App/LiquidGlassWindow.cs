using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GeometryGrid.App;

/// <summary>
/// Adds the native Windows backdrop and rounded non-client corners that sit
/// behind GeometryGrid's lightweight WPF glass layers. Every call is best
/// effort so the application remains compatible with older Windows builds.
/// </summary>
internal static class LiquidGlassWindow
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;

    public static void Apply(Window window, bool transient = false)
    {
        LiquidGlassMotion.EnsureRegistered();
        window.SourceInitialized += (_, _) =>
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                return;

            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                var corner = DwmwcpRound;
                var backdrop = transient ? DwmsbtTransientWindow : DwmsbtMainWindow;
                var caption = ToColorRef(0xF4, 0xF7, 0xFC);
                var border = ToColorRef(0xD4, 0xE2, 0xF2);

                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaWindowCornerPreference,
                    ref corner,
                    sizeof(int));
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaSystemBackdropType,
                    ref backdrop,
                    sizeof(int));
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaCaptionColor,
                    ref caption,
                    sizeof(int));
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmwaBorderColor,
                    ref border,
                    sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // DWM is unavailable; the WPF theme still renders normally.
            }
            catch (EntryPointNotFoundException)
            {
                // Older Windows build.
            }
        };
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
