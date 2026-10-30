using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Redux;
using Dota2Optimizer.Core.Tweaks;
using Dota2Optimizer.Core.Upscale;

namespace Dota2Optimizer.App.ViewModels;

public sealed record CapRowVm(Capability Cap)
{
    public string Name => Cap.Name;
    public string Why => Cap.Why;
    public bool FrameGeneration => Cap.FrameGeneration;

    public string StatusText => Cap.Status switch
    {
        CapStatus.Works => "работает",
        CapStatus.Experimental => "экспериментально",
        _ => "не работает",
    };

    public string KindText => Cap.Kind switch
    {
        CapKind.Overlay => "поверх любой игры",
        CapKind.Driver => "в драйвере",
        _ => "в играх с поддержкой",
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[Cap.Status switch { CapStatus.Works => "Ok", CapStatus.Experimental => "Warn", _ => "Crit" }];
    public Brush StatusSoft => (Brush)Application.Current.Resources[Cap.Status switch { CapStatus.Works => "OkSoft", CapStatus.Experimental => "WarnSoft", _ => "CritSoft" }];
}

public sealed record GpuCapsVm(string Name, string Details, IReadOnlyList<CapRowVm> Rows);

/// <summary>«Апскейл и кадры»: what this card can do, Lossless Scaling and AFMF help, and the OptiScaler helper for single-player games.</summary>
public sealed class UpscaleViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly AppServices _s;
    private bool _loaded;
    private bool _loading;
    private string? _losslessPath;
    private string? _gameExe;
    private string? _optiStatus;
    private double _progress;
    private bool _busy;
    private bool _hasAmd;

    public UpscaleViewModel(AppServices services)
    {
        _s = services;
        Magpie = new MagpieViewModel(services);
        LaunchLosslessCommand = new RelayCommand(() => Core.Crash.CrashTools.OpenUri($"steam://rungameid/{LosslessScaling.AppId}"), () => LosslessFound);
        LosslessStoreCommand = new RelayCommand(() => Ui.OpenUrl(LosslessScaling.StoreUrl));
        PickGameCommand = new AsyncCommand(PickGameAsync, () => !Busy);
        InstallOptiCommand = new AsyncCommand(InstallOptiAsync, () => !Busy && GameExe is not null && !OptiInstalled);
        RemoveOptiCommand = new AsyncCommand(RemoveOptiAsync, () => !Busy && OptiInstalled);
        OpenOptiPageCommand = new RelayCommand(() => Ui.OpenUrl($"https://github.com/{OptiScalerInstaller.Repo}"));
    }

    public ObservableCollection<GpuCapsVm> Gpus { get; } = new();
    public MagpieViewModel Magpie { get; }
    public ObservableCollection<string> LosslessTips { get; } = new();
    public ICommand LaunchLosslessCommand { get; }
    public ICommand LosslessStoreCommand { get; }
    public ICommand PickGameCommand { get; }
    public ICommand InstallOptiCommand { get; }
    public ICommand RemoveOptiCommand { get; }
    public ICommand OpenOptiPageCommand { get; }

    public bool IsLoading
    {
        get => _loading;
        private set => Set(ref _loading, value);
    }

    public bool LosslessFound => _losslessPath is not null;
    public string LosslessText => LosslessFound ? $"Установлена: {_losslessPath}" : "Не найдена в библиотеках Steam. Программа платная (Steam), FrameLab её не скачивает и не обходит покупку.";
    public bool HasAmd => _hasAmd;

    public string? GameExe
    {
        get => _gameExe;
        private set
        {
            Set(ref _gameExe, value);
            OnPropertyChanged(nameof(OptiInstalled));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool OptiInstalled => GameExe is not null && TweakEngine.OwnChanges(OptiScalerInstaller.TweakId(GameExe), _s.Store).Count > 0;

    public string? OptiStatus
    {
        get => _optiStatus;
        private set => Set(ref _optiStatus, value);
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            Set(ref _busy, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task ActivateAsync()
    {
        if (_loaded || IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await _s.EnsureSnapshotAsync();
            var gpus = snapshot.RealGpus.ToList();
            var caps = await Task.Run(() => GpuProbe.Probe(gpus));
            Magpie.SetGpus(caps);
            var build = snapshot.Windows?.Build ?? 0;
            Gpus.Clear();
            foreach (var g in gpus.OrderBy(g => g.IsIntegrated))
            {
                var c = caps.FirstOrDefault(x => x.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase)) ?? new GpuCaps(g.Name, 0, false);
                var details = $"DirectX feature level {c.FeatureLevelText} · DirectX 12: {(c.Dx12 ? "да" : "нет")}" + (g.IsIntegrated ? " · встроенная" : string.Empty);
                if (c.FeatureLevel is > 0 and < 0xB000)
                {
                    details += " · Карта не поддерживает DirectX 11 feature level 11_0: апскейлеры и генерация кадров на ней не работают.";
                }

                Gpus.Add(new GpuCapsVm(g.Name, details, CapabilityMatrix.For(g, c, build).Select(x => new CapRowVm(x)).ToList()));
            }

            _hasAmd = gpus.Any(g => g.Vendor == Core.SystemInfo.GpuVendor.Amd);
            _losslessPath = LosslessScaling.Find(_s.Fs, snapshot.Games.Values.SelectMany(i => i.Libraries).Distinct(StringComparer.OrdinalIgnoreCase));
            LosslessTips.Clear();
            var primary = snapshot.PrimaryGpu;
            if (primary is not null)
            {
                foreach (var t in LosslessScaling.Recommendations(GpuFamily.Of(primary), snapshot.Display?.MaxRefreshHz ?? snapshot.Display?.RefreshHz ?? 60))
                {
                    LosslessTips.Add(t);
                }
            }

            _loaded = true;
            OnPropertyChanged(nameof(LosslessFound));
            OnPropertyChanged(nameof(LosslessText));
            OnPropertyChanged(nameof(HasAmd));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PickGameAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Игра (*.exe)|*.exe", Title = "Выберите exe одиночной игры" };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        GameExe = dlg.FileName;
        OptiStatus = "Проверяю папку игры…";
        Busy = true;
        var dir = PathEx.Parent(dlg.FileName)!;
        IReadOnlyList<string> ac, up;
        try
        {
            // Large games have tens of thousands of files: scanning stays off the UI thread.
            (ac, up) = await Task.Run(() =>
            {
                var root = OptiScalerInstaller.GameRoot(_s.Fs, dir);
                return (AntiCheatScanner.Scan(_s.Fs, root), UpscalerDlls.Find(_s.Fs, root));
            });
        }
        finally
        {
            Busy = false;
        }

        OptiStatus = OptiScalerInstaller.BlockedExes.Contains(PathEx.FileName(dlg.FileName)) || ac.Count > 0
            ? $"⛔ Античит: {(ac.Count > 0 ? string.Join(", ", ac) : "онлайн-игра")}. Установка OptiScaler заблокирована — за внедрение DLL в такие игры банят."
            : up.Count == 0
                ? "В папке игры нет DLSS / FSR 2+ / XeSS — OptiScaler тут не поможет."
                : $"Найдено в игре: {string.Join(", ", up)}. Античит не найден." + (OptiInstalled ? " OptiScaler уже установлен программой." : " Можно установить.");
    }

    private async Task InstallOptiAsync()
    {
        var exe = GameExe!;
        if (!Ui.Confirm(
                "OptiScaler — сторонний мод (лицензия GPLv3), не часть FrameLab. Программа скачает его официальный релиз с GitHub (github.com/" + OptiScalerInstaller.Repo +
                "), покажет список файлов и скопирует их в папку игры, сохранив копии всего, что заменяется. «Удалить» вернёт папку как была.\n\n" +
                "Только для одиночных игр. В онлайн-играх с античитом — бан.\nГенерация кадров OptiFG работает только в DirectX 12 и добавляет задержку ввода.\n\nСкачать и продолжить?",
                "OptiScaler"))
        {
            return;
        }

        Busy = true;
        Progress = 0;
        try
        {
            OptiStatus = "Ищу последний релиз на GitHub…";
            var asset = await GitHubReleases.LatestAsync(Http, OptiScalerInstaller.Repo,
                n => n.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), CancellationToken.None);
            if (asset is null)
            {
                OptiStatus = "В последнем релизе OptiScaler не нашлось архива. Откройте страницу проекта и скачайте вручную.";
                return;
            }

            var downloads = Path.Combine(AppPaths.Root, "downloads");
            var archive = Path.Combine(downloads, asset.Name);
            if (!File.Exists(archive))
            {
                OptiStatus = $"Скачиваю {asset.Name} ({Ui.Size(asset.Size)})…";
                await GitHubReleases.DownloadAsync(Http, asset, archive, new Progress<double>(p => Progress = p), CancellationToken.None);
            }

            OptiStatus = "Распаковываю…";
            // The unpacked release stays in the downloads folder: the copy step reads from it.
            var source = await Task.Run(() => ReduxSources.Open(archive, _s.Commands, _s.Fs, Path.Combine(downloads, "unpacked")));
            var root = source is FolderReduxSource folder ? folder.Root : throw new InvalidOperationException("Ожидался архив .7z");
            var plan = OptiScalerInstaller.Plan(_s.Fs, exe, root);
            if (plan.Blocked is { } why)
            {
                OptiStatus = "⛔ " + why;
                return;
            }

            var needsAdmin = !CanWrite(PathEx.Parent(exe)!);
            var actions = plan.Actions.Select(a => a with { RequiresAdmin = needsAdmin }).ToList();
            var tweak = new OptiScalerTweak(exe, plan with { Actions = actions });
            var lines = await _s.ConfirmAndApplyAsync(new[] { tweak.Plan(_s.Context(_s.Snapshot ?? new Core.SystemInfo.SystemSnapshot())) }, $"OptiScaler {asset.Tag} → {PathEx.FileName(exe)}");
            OptiStatus = lines is null
                ? "Отменено."
                : $"Установлен OptiScaler {asset.Tag} как {plan.ProxyName}. В игре: включите DLSS (или FSR/XeSS) в настройках графики, меню OptiScaler — клавиша Insert. " +
                  "Лицензия и исходники: " + asset.PageUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            OptiStatus = "Не получилось: " + ex.Message;
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(OptiInstalled));
        }
    }

    private async Task RemoveOptiAsync()
    {
        Busy = true;
        try
        {
            var messages = await _s.RevertTweakAsync(OptiScalerInstaller.TweakId(GameExe!), $"OptiScaler для {PathEx.FileName(GameExe!)}");
            if (messages is not null)
            {
                OptiStatus = "OptiScaler удалён, файлы игры возвращены: " + string.Join("; ", messages);
            }
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(OptiInstalled));
        }
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".framelab-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
