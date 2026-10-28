using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Dota2Optimizer.App.ViewModels;

namespace Dota2Optimizer.App;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        SourceInitialized += (_, _) => UseRoundCorners();
        Loaded += async (_, _) => await vm.Home.LoadAsync();
        Closed += (_, _) => vm.Shutdown();
        StateChanged += (_, _) => OnStateChanged();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Windows 11 rounds the corners of the custom-chrome window like any other window.</summary>
    private void UseRoundCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var round = DwmCornerRound;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older systems keep square corners.
        }
    }

    /// <summary>A maximized custom-chrome window overhangs the screen by the resize border; pad it back in.</summary>
    private void OnStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + SystemParameters.FixedFrameVerticalBorderWidth) : new Thickness(0);
        Root.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        MaxButton.Tag = maximized ? "M 2.5,0.5 L 9.5,0.5 L 9.5,7.5 M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z" : "M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z";
        MaxButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
