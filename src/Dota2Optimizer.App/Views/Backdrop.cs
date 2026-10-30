using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Dota2Optimizer.App.Views;

/// <summary>
/// Windows 11 «Mica» behind a window: the blur is composed by the system (DWM), so it costs the app nothing per frame.
/// Used only where it reads well (dialogs); skipped on Windows 10 and in performance mode, where the window stays opaque.
/// </summary>
public static class Backdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropMica = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    /// <summary>Call from SourceInitialized. <paramref name="translucent"/> replaces the window background when Mica is on.</summary>
    public static void TryMica(Window window, Brush translucent)
    {
        if (Motion.Performance || Environment.OSVersion.Version.Build < 22621)
        {
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var mica = BackdropMica;
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref mica, sizeof(int)) != 0)
            {
                return;
            }

            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            window.Background = translucent;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older systems keep the opaque background.
        }
    }
}
