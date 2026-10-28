using Dota2Optimizer.App.Views;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>«Настройки»: motion and visual effects of the app itself.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Action _reapplyTheme;
    private bool _restartNote;

    public SettingsViewModel(Action reapplyTheme)
    {
        _reapplyTheme = reapplyTheme;
    }

    public bool ReduceMotion
    {
        get => Motion.Settings.ReduceMotion;
        set => Change(s => s.ReduceMotion = value);
    }

    public bool PerformanceMode
    {
        get => Motion.Settings.PerformanceMode;
        set => Change(s => s.PerformanceMode = value);
    }

    public bool SystemAnimationsOff => !Motion.SystemAllowsAnimation;

    public string SystemText => SystemAnimationsOff
        ? "В Windows выключено «Показывать анимацию» — FrameLab тоже не анимирует интерфейс."
        : "В Windows анимации включены. Если выключить их там (Параметры → Специальные возможности → Визуальные эффекты), FrameLab выключит свои.";

    public bool RestartNote
    {
        get => _restartNote;
        private set => Set(ref _restartNote, value);
    }

    private void Change(Action<AppSettings> edit)
    {
        var s = new AppSettings { ReduceMotion = Motion.Settings.ReduceMotion, PerformanceMode = Motion.Settings.PerformanceMode };
        edit(s);
        Motion.Save(s);
        _reapplyTheme();
        RestartNote = true;
        OnPropertyChanged(nameof(ReduceMotion));
        OnPropertyChanged(nameof(PerformanceMode));
    }
}
