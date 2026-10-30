using System.Diagnostics;
using System.Windows.Input;
using Dota2Optimizer.App.Views;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Boost;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

public sealed class BoostRowVm : ObservableObject
{
    private bool _busy;
    private MarkState _mark;
    private string? _result;

    public BoostRowVm(BoostAction action, Func<BoostRowVm, Task> run)
    {
        Action = action;
        RunCommand = new AsyncCommand(() => run(this), () => !IsBusy);
    }

    public BoostAction Action { get; }
    public string Title => Action.Title;
    public string Description => Action.Description;
    public string Honest => Action.Honest;
    public string Effect => "Эффект: " + Core.Tweaks.EffectivenessText.Ru(Action.Effect).ToLowerInvariant();
    public Core.Tweaks.Effectiveness EffectValue => Action.Effect;
    public bool RequiresAdmin => Action.RequiresAdmin;
    public ICommand RunCommand { get; }

    public bool IsBusy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                if (value)
                {
                    Mark = MarkState.Busy;
                }

                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public MarkState Mark
    {
        get => _mark;
        set => Set(ref _mark, value);
    }

    public string? Result
    {
        get => _result;
        set => Set(ref _result, value);
    }
}

/// <summary>«Ускорения»: one-off clean-ups. Nothing here is undoable, so each one asks first and says honestly how much it helps.</summary>
public sealed class BoostViewModel
{
    private readonly AppServices _s;

    public BoostViewModel(AppServices services)
    {
        _s = services;
        Actions = BoostCatalog.All.Select(a => new BoostRowVm(a, RunAsync)).ToList();
    }

    public IReadOnlyList<BoostRowVm> Actions { get; }

    private async Task RunAsync(BoostRowVm row)
    {
        var a = row.Action;
        row.IsBusy = true;
        // Folder sizes are measured off the UI thread: TEMP and driver caches can hold many thousands of files.
        var extra = await Task.Run(() => a.Id switch
        {
            BoostCatalog.Shaders => $"\n\nБудет очищено: {Ui.Size(ShaderCaches().Sum(c => c.SizeBytes))}.",
            BoostCatalog.Temp => $"\n\nБудет удалено: {TempCleaner.Measure(TempCleaner.UserTemp, DateTime.UtcNow).SizeText}.",
            _ => string.Empty,
        });
        row.IsBusy = false;
        row.Mark = MarkState.None;
        if (!Ui.Confirm($"{a.Title}?\n\n{a.Description}\n\n{a.Honest}{extra}\n\nЭто разовое действие, откатить его нельзя (и не нужно).", a.Title))
        {
            return;
        }

        row.IsBusy = true;
        BoostResult result;
        try
        {
            result = await Task.Run(() => Run(a));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            result = new BoostResult(false, ex.Message);
        }
        finally
        {
            row.IsBusy = false;
        }

        row.Result = result.Message;
        row.Mark = result.Success ? MarkState.Done : MarkState.Error;
        _s.History.Add(new Core.Benchmark.HistoryEntry(DateTime.Now, result.Success ? "Выполнено" : "Ошибка", a.Title, result.Message));
    }

    private IReadOnlyList<CacheLocation> ShaderCaches()
    {
        var vendors = _s.Snapshot?.RealGpus.Select(g => g.Vendor).ToList() ?? new List<Core.SystemInfo.GpuVendor>();
        // Only the driver and DirectX caches: the Steam per-game caches are cleaned from each game's own page.
        return CrashTools.ShaderCaches(Core.Game.Games.Dota2, new Core.SystemInfo.SteamInfo(), vendors).Where(c => c.Exists).ToList();
    }

    private BoostResult Run(BoostAction a)
    {
        switch (a.Id)
        {
            case BoostCatalog.Temp:
                var t = TempCleaner.Clean(TempCleaner.UserTemp, DateTime.UtcNow);
                return new BoostResult(true, $"Удалено файлов: {t.Files}, освобождено {t.SizeText}.");
            case BoostCatalog.Dns:
                var d = _s.Commands.Run("ipconfig", "/flushdns");
                return new BoostResult(d.Success, d.Success ? "Кэш DNS очищен." : $"ipconfig вернул код {d.ExitCode}.");
            case BoostCatalog.Shaders:
                var c = CrashTools.Clean(ShaderCaches());
                return new BoostResult(true, $"Освобождено {Ui.Size(c.FreedBytes)}, удалено файлов: {c.Deleted}" + (c.Skipped > 0 ? $", занятых пропущено: {c.Skipped}." : "."));
            case BoostCatalog.Explorer:
                _s.Commands.Run("taskkill", "/f /im explorer.exe");
                Thread.Sleep(800);
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
                return new BoostResult(true, "Проводник перезапущен.");
            default:
                var r = AppServices.RunElevated(new ElevatedRequest { Mode = "boost", BoostId = a.Id });
                return r is null ? new BoostResult(false, "Запрос прав администратора отклонён.") : new BoostResult(r.Items.FirstOrDefault()?.Success == true, string.Join(" ", r.Messages));
        }
    }
}
