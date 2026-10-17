using System.Diagnostics;

namespace Dota2Optimizer.Core.Diagnostics;

public sealed record KnownApp(string ProcessName, string DisplayName, string Category, string Advice);

public sealed record RunningApp(KnownApp App, int Instances, long WorkingSetMb);

/// <summary>
/// Known resource consumers. We only list them; closing is always left to the user.
/// Only names and memory of our own enumeration are read — no process is opened for reading.
/// </summary>
public static class BackgroundApps
{
    public static IReadOnlyList<KnownApp> Known { get; } = new[]
    {
        new KnownApp("chrome", "Google Chrome", "Браузер", "Вкладки с видео и стримами нагружают GPU и сеть. Закройте или включите в браузере экономию памяти."),
        new KnownApp("msedge", "Microsoft Edge", "Браузер", "Закройте лишние вкладки, особенно с видео."),
        new KnownApp("firefox", "Firefox", "Браузер", "Закройте лишние вкладки, особенно с видео."),
        new KnownApp("opera", "Opera", "Браузер", "Закройте лишние вкладки, особенно с видео."),
        new KnownApp("browser", "Яндекс Браузер", "Браузер", "Закройте лишние вкладки, особенно с видео."),
        new KnownApp("EpicGamesLauncher", "Epic Games Launcher", "Лаунчер", "Может качать обновления в фоне. Закройте на время игры."),
        new KnownApp("EADesktop", "EA app", "Лаунчер", "Может качать обновления в фоне."),
        new KnownApp("Battle.net", "Battle.net", "Лаунчер", "Может качать обновления в фоне."),
        new KnownApp("UbisoftConnect", "Ubisoft Connect", "Лаунчер", "Может качать обновления в фоне."),
        new KnownApp("GalaxyClient", "GOG Galaxy", "Лаунчер", "Может качать обновления в фоне."),
        new KnownApp("qbittorrent", "qBittorrent", "Загрузки", "Торрент забивает канал — пинг и потери вырастут. Поставьте на паузу."),
        new KnownApp("utorrent", "uTorrent", "Загрузки", "Торрент забивает канал — пинг и потери вырастут. Поставьте на паузу."),
        new KnownApp("BitTorrent", "BitTorrent", "Загрузки", "Торрент забивает канал — пинг и потери вырастут. Поставьте на паузу."),
        new KnownApp("OneDrive", "OneDrive", "Синхронизация", "Синхронизация файлов нагружает диск и сеть. Можно приостановить на 2 часа."),
        new KnownApp("Dropbox", "Dropbox", "Синхронизация", "Синхронизация нагружает диск и сеть."),
        new KnownApp("GoogleDriveFS", "Google Drive", "Синхронизация", "Синхронизация нагружает диск и сеть."),
        new KnownApp("Discord", "Discord", "Оверлей/голос", "Сам по себе нужен, но оверлей и аппаратное ускорение иногда вызывают фризы. Попробуйте отключить оверлей для Dota 2."),
        new KnownApp("MSIAfterburner", "MSI Afterburner", "Оверлей", "Оверлей RTSS и разгон — частая причина вылетов. При вылетах проверьте без него и сбросьте разгон."),
        new KnownApp("RTSS", "RivaTuner Statistics Server", "Оверлей", "Оверлей может конфликтовать с Vulkan. При вылетах проверьте без него."),
        new KnownApp("NVIDIA Overlay", "NVIDIA Overlay", "Оверлей", "Мгновенный повтор (Instant Replay) пишет видео в фоне. Отключите, если не пользуетесь."),
        new KnownApp("RadeonSoftware", "AMD Radeon Software", "Оверлей", "Проверьте, что запись/Instant Replay выключены, если не нужны."),
        new KnownApp("obs64", "OBS Studio", "Запись", "Запись/стрим заметно нагружают систему. Закройте, если не стримите."),
        new KnownApp("iCUE", "Corsair iCUE", "RGB", "RGB-софт периодически опрашивает устройства, на слабых ПК даёт микрофризы."),
        new KnownApp("LightingService", "ASUS Aura", "RGB", "RGB-софт может давать микрофризы."),
        new KnownApp("ArmouryCrate", "ASUS Armoury Crate", "RGB/утилита", "На ноутбуках ASUS выберите в нём режим «Турбо/Производительность»."),
        new KnownApp("SignalRgb", "SignalRGB", "RGB", "Эффекты синхронизации экрана нагружают CPU/GPU."),
        new KnownApp("Razer Synapse", "Razer Synapse", "RGB", "RGB-софт может давать микрофризы."),
        new KnownApp("Wallpaper64", "Wallpaper Engine", "Обои", "Живые обои тратят GPU. Он сам ставится на паузу в полноэкранных играх — проверьте настройку."),
        new KnownApp("Teams", "Microsoft Teams", "Мессенджер", "Тяжёлое приложение, закройте, если не нужно."),
        new KnownApp("Telegram", "Telegram", "Мессенджер", "Обычно не мешает. Закройте, если идут загрузки файлов."),
        new KnownApp("steamwebhelper", "Steam (веб-интерфейс)", "Steam", "Нормально. Если FPS низкий, отключите в Steam аппаратное ускорение веб-страниц и оверлей."),
    };

    public static IReadOnlyList<RunningApp> FindRunning()
    {
        var byName = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var name = p.ProcessName;
                    var ws = p.WorkingSet64;
                    byName[name] = byName.TryGetValue(name, out var v) ? (v.Count + 1, v.Bytes + ws) : (1, ws);
                }
                catch (InvalidOperationException)
                {
                    // Process exited while enumerating.
                }
            }
        }

        return Known
            .Where(k => byName.ContainsKey(k.ProcessName))
            .Select(k => new RunningApp(k, byName[k.ProcessName].Count, byName[k.ProcessName].Bytes / 1024 / 1024))
            .OrderByDescending(r => r.WorkingSetMb)
            .ToList();
    }
}
