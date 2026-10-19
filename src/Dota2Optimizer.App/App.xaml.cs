using System.Windows;
using System.Windows.Threading;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var args = e.Args;
        var rootIdx = Array.IndexOf(args, "--root");
        if (rootIdx >= 0 && rootIdx + 1 < args.Length)
        {
            // The elevated copy keeps using the data folder of the user who started the app.
            AppPaths.OverrideRoot(args[rootIdx + 1]);
        }

        var elevatedIdx = Array.IndexOf(args, ElevatedHost.Argument);
        if (elevatedIdx >= 0 && elevatedIdx + 1 < args.Length)
        {
            var code = 1;
            try
            {
                Log.Info("Запущен помощник с правами администратора");
                code = ElevatedHost.Run(args[elevatedIdx + 1]);
            }
            catch (Exception ex)
            {
                Log.Error("Ошибка помощника с правами администратора", ex);
            }

            Shutdown(code);
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        Log.Info($"Запуск FrameLab {typeof(App).Assembly.GetName().Version}, Windows {Environment.OSVersion.Version}");
        base.OnStartup(e);
        new MainWindow().Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Необработанная ошибка", e.Exception);
        MessageBox.Show($"Произошла ошибка: {e.Exception.Message}\n\nПодробности записаны в лог: {Log.CurrentFile}", "FrameLab", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
