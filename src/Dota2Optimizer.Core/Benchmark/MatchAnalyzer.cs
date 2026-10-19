using System.Globalization;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Benchmark;

/// <summary>One sample of system counters taken during a match. Nothing here is read from the game process itself.</summary>
public sealed record MatchSample(
    double ElapsedSec,
    double DotaWorkingSetMb,
    double DotaPrivateMb,
    double AvailableRamMb,
    double CpuPerformancePct,
    double CpuUtilityPct,
    double GpuUtilPct,
    double DotaVramMb);

public sealed record MinuteFps(int Minute, double AvgFps, double OnePercentLowFps, int Frames);

public sealed record MatchReport(
    string Summary,
    IReadOnlyList<Advice> Findings,
    double? EarlyFps,
    double? LateFps,
    double? DropPercent,
    IReadOnlyList<MinuteFps> Minutes,
    IReadOnlyList<MatchSample> Samples);

/// <summary>
/// Explains "FPS drops toward the end of the match" from what was recorded: CPU clock falling (heat/power limit),
/// VRAM or RAM running out, the game's own memory growing, or the GPU/CPU simply being maxed in late fights.
/// </summary>
public static class MatchAnalyzer
{
    public const int MinMinutesForTrend = 8;

    public static IReadOnlyList<MinuteFps> FpsPerMinute(IReadOnlyList<(double TimeSec, double FrameMs)> frames)
    {
        return frames
            .Where(f => f.FrameMs is > 0 and < 5000)
            .GroupBy(f => (int)(f.TimeSec / 60))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ft = g.Select(f => f.FrameMs).ToList();
                var worst = ft.OrderByDescending(x => x).Take(Math.Max(1, ft.Count / 100)).Average();
                return new MinuteFps(g.Key, 1000.0 * ft.Count / ft.Sum(), 1000.0 / worst, ft.Count);
            })
            .ToList();
    }

    /// <param name="fpsProblem">Why the FPS capture produced nothing (shown to the user), if it did.</param>
    /// <param name="game">The recorded game (texts only); Dota 2 when omitted.</param>
    public static MatchReport Analyze(IReadOnlyList<MatchSample> samples, IReadOnlyList<MinuteFps> minutes, double vramTotalMb, double ramTotalMb, string? fpsProblem = null,
        GameDefinition? game = null)
    {
        game ??= Games.Dota2;
        var name = game.Name;
        var cs2 = game.Id == GameId.Cs2;
        var findings = new List<Advice>();

        double? early = null, late = null, drop = null;
        // Minute 0 is usually loading/strategy time; skip it and very short captures.
        var usable = minutes.Where(m => m.Minute >= 1 && m.Frames > 100).ToList();
        if (usable.Count >= MinMinutesForTrend)
        {
            var n = Math.Max(3, usable.Count / 5);
            early = usable.Take(n).Average(m => m.AvgFps);
            late = usable.Skip(usable.Count - n).Average(m => m.AvgFps);
            drop = (early - late) / early * 100;
        }

        if (samples.Count >= 6)
        {
            var q = Math.Max(2, samples.Count / 4);
            var first = samples.Take(q).ToList();
            var last = samples.Skip(samples.Count - q).ToList();

            var cpuEarly = first.Average(s => s.CpuPerformancePct);
            var cpuLate = last.Average(s => s.CpuPerformancePct);
            if (cpuEarly > 0 && cpuLate / cpuEarly < 0.88)
            {
                findings.Add(new Advice(Severity.Critical, "Процессор сбрасывает частоту к концу матча",
                    $"Производительность CPU упала с {cpuEarly:0}% до {cpuLate:0}% от базовой частоты — это троттлинг из-за нагрева или лимита мощности. " +
                    "Ограничьте FPS (+fps_max под частоту монитора), включите план «Высокая производительность», на ноутбуке играйте от сети и на подставке, почистите охлаждение."));
            }

            var vramLate = last.Average(s => s.DotaVramMb);
            if (vramTotalMb > 0 && vramLate >= 0.9 * vramTotalMb)
            {
                findings.Add(new Advice(Severity.Critical, "Видеопамять почти заполнена",
                    $"Игра использует ~{vramLate:0} МБ из {vramTotalMb:0} МБ. Когда видеопамять кончается, текстуры уходят в ОЗУ и FPS проседает. " +
                    "Снизьте качество текстур (в пресете — mat_picmip), закройте браузер с аппаратным ускорением и другие программы, использующие видеокарту."));
            }

            var ramLate = last.Average(s => s.AvailableRamMb);
            if (ramLate < 1024)
            {
                findings.Add(new Advice(Severity.Critical, "Заканчивается оперативная память",
                    $"Свободно всего ~{ramLate:0} МБ. Система начинает использовать файл подкачки — отсюда рывки. Закройте браузер и лишние программы, включите автоматический файл подкачки" +
                    (ramTotalMb is > 0 and < 12_000 ? "; при 8 ГБ ОЗУ это ожидаемо — 16 ГБ сильно помогут." : ".")));
            }

            var memEarly = first.Average(s => s.DotaPrivateMb);
            var memLate = last.Average(s => s.DotaPrivateMb);
            if (memEarly > 0 && memLate > memEarly * 1.5 && memLate - memEarly > 1500)
            {
                findings.Add(new Advice(Severity.Warning, "Потребление памяти игрой сильно выросло за матч",
                    $"Память {name} выросла с ~{memEarly:0} до ~{memLate:0} МБ. Это похоже на утечку на стороне игры — приложение исправить её не может. " +
                    $"Перезапускайте {name} между матчами; если повторяется после патча — сообщите Valve" + (cs2 ? "." : " (github.com/ValveSoftware/Dota2-Gameplay).")));
            }

            var gpuLate = last.Average(s => s.GpuUtilPct);
            var cpuUtilLate = last.Average(s => s.CpuUtilityPct);
            if (drop is > 10)
            {
                if (gpuLate >= 92)
                {
                    findings.Add(new Advice(Severity.Warning, cs2 ? "Упор в видеокарту" : "Упор в видеокарту в поздних драках",
                        $"Видеокарта загружена на ~{gpuLate:0}%. Больше эффектов на экране — меньше FPS. Помогут пресет «Максимум FPS» или «Баланс», " +
                        (cs2 ? "снижение теней, AO, MSAA и разрешения." : "снижение рендер-масштаба, теней и сглаживания.")));
                }
                else if (gpuLate < 75)
                {
                    findings.Add(new Advice(Severity.Warning, cs2 ? "Упор в процессор" : "Упор в процессор в поздних драках",
                        $"Видеокарта загружена лишь на ~{gpuLate:0}% (CPU ~{cpuUtilLate:0}%): FPS ограничивает процессор — " +
                        (cs2 ? "в CS2 это типично для перестрелок, дымов и гранат. " : "много юнитов, иллюзий и эффектов. ") +
                        "Графика здесь почти не поможет; помогают ограничение FPS (ровнее кадры), закрытие фоновых программ, " +
                        (cs2 ? "двухканальная память и сравнение DX11 с -vulkan." : "сравнение -dx11/-vulkan (Vulkan часто меньше нагружает CPU), а также перезапуск игры между матчами.")));
                }
            }
        }

        string summary;
        if (drop is null)
        {
            summary = minutes.Count == 0
                ? $"FPS не записан: {fpsProblem ?? "PresentMon недоступен"}. Выводы сделаны только по системным счётчикам."
                : $"Запись слишком короткая для оценки тренда FPS (нужно хотя бы {MinMinutesForTrend} минут игры).";
        }
        else if (drop > 10)
        {
            summary = $"FPS упал на {drop:0}% к концу матча: в начале ~{early:0}, в конце ~{late:0}.";
        }
        else
        {
            summary = $"FPS стабилен: в начале ~{early:0}, в конце ~{late:0} (разница {drop:+0;-0;0}%).";
        }

        if (drop is > 10 && findings.Count == 0)
        {
            findings.Add(new Advice(Severity.Info, "Явной системной причины не найдено",
                "Нагрев, память и видеопамять в норме. Скорее всего, падение — это нагрузка самой игры в поздних драках или постепенная деградация без перезапуска. " +
                $"Перезапускайте {name} каждые 2–3 матча и ограничьте FPS, чтобы кадры были ровнее."));
        }

        findings.Add(new Advice(Severity.Info, "Профилактика",
            $"Перезапуск {name} каждые 2–3 матча, ограничение FPS и закрытый браузер — самые надёжные способы избежать падения FPS в длинных играх."));

        return new MatchReport(summary, findings.OrderByDescending(f => f.Severity).ToList(), early, late, drop, minutes, samples);
    }

    public static string FormatMinutes(IEnumerable<MinuteFps> minutes) =>
        string.Join("  ", minutes.Select(m => $"{m.Minute}′:{m.AvgFps.ToString("0", CultureInfo.InvariantCulture)}"));
}
