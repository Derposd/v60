using System.Globalization;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.GtaRp;

/// <summary>Writes one of the <see cref="GtaModes"/> into GTA V's settings.xml. Only existing keys change; undo restores the original file.</summary>
public sealed class GtaGraphicsModeTweak : Tweak
{
    private readonly GtaInfo _gta;

    public GtaGraphicsModeTweak(GtaInfo gta, GtaMode mode)
    {
        _gta = gta;
        Mode = mode;
    }

    public GtaMode Mode { get; }

    public override string Id => $"gta.mode.{Mode.Id}";
    public override string Title => $"Режим «{Mode.Title}»";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Mode.Effect;
    public override bool Recommended => false;
    public override string Explanation => Mode.Explanation;

    public override string Risk => "Минимальный: меняются только значения в settings.xml, исходный файл сохраняется и возвращается откатом";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (!_gta.SettingsExists)
        {
            return Result(TweakStatus.NotApplicable,
                "Файл settings.xml не найден (Документы\\Rockstar Games\\GTA V). Запустите GTA V один раз и закройте — игра создаст его.", recommendedHere: false);
        }

        if (_gta.AnyRunning)
        {
            return Result(TweakStatus.Blocked, "Закройте GTA V и RAGE MP: при выходе игра перезапишет settings.xml и отменит изменения.", recommendedHere: false);
        }

        GtaSettingsFile file;
        try
        {
            file = GtaSettingsFile.Parse(ctx.FileSystem.ReadAllText(_gta.SettingsPath));
        }
        catch (InvalidDataException ex)
        {
            return Result(TweakStatus.Blocked, $"{ex.Message} Запустите GTA V и измените любую настройку графики — игра пересоздаст файл.", recommendedHere: false);
        }

        var gpu = ctx.Snapshot.PrimaryGpu;
        var changed = 0;
        foreach (var (key, value) in GtaModes.Resolve(Mode, gpu))
        {
            if (file.TrySet(key, value))
            {
                changed++;
            }
        }

        var notes = new List<string>();
        if (Mode.TexturesFollowVram && gpu is not null && GtaModes.TextureCap(gpu) < int.Parse(Mode.Values["TextureQuality"], CultureInfo.InvariantCulture))
        {
            notes.Add("Качество текстур ограничено объёмом видеопамяти, иначе игра будет подвисать.");
        }

        if (Mode.LowerResolution)
        {
            var (width, height) = CurrentResolution(file);
            if (width > GtaModes.LowResWidth && IsSixteenByNine(ctx.Snapshot.Display, width, height))
            {
                changed += file.TrySet("ScreenWidth", GtaModes.LowResWidth.ToString(CultureInfo.InvariantCulture)) ? 1 : 0;
                changed += file.TrySet("ScreenHeight", GtaModes.LowResHeight.ToString(CultureInfo.InvariantCulture)) ? 1 : 0;
                notes.Add($"Разрешение будет снижено до {GtaModes.LowResWidth}×{GtaModes.LowResHeight}. Если игра его не применила, выберите в ней режим экрана «Полный экран».");
            }
            else if (width > GtaModes.LowResWidth)
            {
                notes.Add("Разрешение не меняется: монитор не 16:9, картинка получилась бы растянутой. Снизьте его вручную в настройках игры, если нужен ещё больший прирост.");
            }
        }

        var note = notes.Count == 0 ? string.Empty : " " + string.Join(" ", notes);
        if (changed == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Этот режим уже включён." + note, recommendedHere: false);
        }

        var recommended = GtaModes.Recommend(ctx.Snapshot).Mode.Id == Mode.Id;
        return Result(TweakStatus.CanApply, $"Изменится параметров: {changed}.{note}", new[]
        {
            PlannedAction.WriteFile(_gta.SettingsPath, file.Text, $"Графика GTA V: режим «{Mode.Title}»"),
        }, recommendedHere: recommended);
    }

    private static (int Width, int Height) CurrentResolution(GtaSettingsFile file) =>
        (int.TryParse(file.Get("ScreenWidth"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 0,
         int.TryParse(file.Get("ScreenHeight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 0);

    private static bool IsSixteenByNine(DisplayInfo? display, int width, int height)
    {
        var (w, h) = display is { Width: > 0, Height: > 0 } ? (display.Width, display.Height) : (width, height);
        return h > 0 && Math.Abs((double)w / h - 16d / 9d) < 0.03;
    }
}

/// <summary>
/// RAGE MP keeps everything it downloads from a server in client_resources. A damaged cache is a common reason for white textures, endless
/// "loading resources" and crashes on connect. The folder is renamed, not deleted, so nothing is lost and undo puts it back.
/// </summary>
public sealed class RageMpCacheTweak : Tweak
{
    public const string BackupName = "client_resources.bak";
    private readonly GtaInfo _gta;

    public RageMpCacheTweak(GtaInfo gta)
    {
        _gta = gta;
    }

    public override string Id => "gta.ragemp.cache";
    public override string Title => "Сбросить кэш RAGE MP (если глючат текстуры или не грузится сервер)";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override string Risk => "Минимальный. Клиент заново скачает ресурсы сервера — первый вход будет дольше";

    public override string Explanation =>
        "FPS это не добавляет: сброс лечит глюки — белые и «ломаные» текстуры, зависание на загрузке ресурсов, вылеты при входе на сервер. " +
        $"Папка client_resources переименовывается в {BackupName}; RAGE MP создаст её заново. Откат возвращает старую папку. " +
        "Старую копию можно удалить вручную, когда убедитесь, что всё работает: она занимает место на диске.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var cache = _gta.RageMpCacheDir;
        if (cache is null)
        {
            return Result(TweakStatus.NotApplicable, "Клиент RAGE MP (папка RAGEMP) не найден.", recommendedHere: false);
        }

        if (_gta.AnyRunning)
        {
            return Result(TweakStatus.Blocked, "Закройте GTA V и RAGE MP.", recommendedHere: false);
        }

        if (!ctx.FileSystem.DirectoryExists(cache))
        {
            return Result(TweakStatus.AlreadyOptimal, "Кэша нет: RAGE MP создаст его при следующем входе на сервер.", recommendedHere: false);
        }

        var backup = Path.Combine(_gta.RageMpDir!, BackupName);
        if (ctx.FileSystem.DirectoryExists(backup))
        {
            return Result(TweakStatus.ManualOnly,
                $"Рядом уже лежит {BackupName} от прошлого сброса. Удалите её вручную ({backup}) и повторите — приложение не стирает папки само.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Кэш найден. Применяйте только при проблемах с загрузкой или текстурами.", new[]
        {
            PlannedAction.Run("cmd", $"/c ren \"{cache}\" \"{BackupName}\"", "cmd", $"/c ren \"{backup}\" \"client_resources\"",
                "Переименовать client_resources RAGE MP (клиент создаст кэш заново)", requiresAdmin: false),
        }, recommendedHere: false);
    }
}

/// <summary>Hybrid-graphics laptops: makes Windows run GTA5.exe and the RAGE MP client on the discrete card.</summary>
public sealed class GtaGpuPreferenceTweak : Tweak
{
    private const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private readonly GtaInfo _gta;

    public GtaGpuPreferenceTweak(GtaInfo gta)
    {
        _gta = gta;
    }

    public override string Id => "gta.win.gpupreference";
    public override string Title => "GTA V и RAGE MP на мощной видеокарте";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.High;

    public override string Explanation =>
        "На ноутбуках с двумя видеокартами Windows может запустить игру на встроенной графике — FPS падает в разы. " +
        "Это та же настройка, что в «Параметры → Дисплей → Графика»: для GTA5.exe и клиента RAGE MP выбирается «Высокая производительность».";

    public override TweakPlan Plan(TweakContext ctx)
    {
        var exes = new[] { _gta.GameExe, _gta.RageMpExe }
            .Where(p => p is not null && ctx.FileSystem.FileExists(p))
            .Select(p => p!)
            .ToList();
        if (exes.Count == 0)
        {
            return Result(TweakStatus.NotApplicable, "GTA V не найдена.", recommendedHere: false);
        }

        if (ctx.Snapshot.RealGpus.Count() < 2)
        {
            return Result(TweakStatus.NotApplicable, "В системе одна видеокарта — настройка не нужна.", recommendedHere: false);
        }

        var actions = new List<PlannedAction>();
        foreach (var exe in exes)
        {
            var current = ctx.Registry.GetValue(RegHive.CurrentUser, Key, exe)?.Data;
            if (RegistryStrings.GetSemicolonPair(current, "GpuPreference") == "2")
            {
                continue;
            }

            actions.Add(PlannedAction.SetReg(RegHive.CurrentUser, Key, exe,
                RegValue.Str(RegistryStrings.SetSemicolonPair(current, "GpuPreference", "2")),
                $"Предпочтение GPU для {Path.GetFileName(exe)}: высокая производительность"));
        }

        return actions.Count == 0
            ? Result(TweakStatus.AlreadyOptimal, "Для GTA V и RAGE MP уже выбрана «Высокая производительность».")
            : Result(TweakStatus.CanApply, "Несколько видеокарт, предпочтение для игры не задано.", actions, recommendedHere: ctx.Snapshot.IsHybridGraphics);
    }
}
