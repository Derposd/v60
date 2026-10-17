using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Dota2Optimizer.App.ViewModels;

namespace Dota2Optimizer.App;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmCaptionColor = 35;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark native title bar (Windows 10 20H1+ and 11) and, on Windows 11, a caption colour matching the sidebar.</summary>
    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
            }

            var caption = 0x0029211E; // COLORREF 0x00BBGGRR for #1E2129
            DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older systems simply keep the default title bar.
        }
    }
}
