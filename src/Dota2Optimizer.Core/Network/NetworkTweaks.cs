using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Core.Network;

/// <summary>A physical network adapter as the driver describes it in the registry.</summary>
public sealed record NetAdapter(string ClassKey, string Description, bool IsWifi)
{
    /// <summary>Advanced properties the driver declares (Ndi\Params) — only these can be offered.</summary>
    public IReadOnlySet<string> Keywords { get; init; } = new HashSet<string>();
}

/// <summary>Reads adapters from the network class key. Virtual adapters (VPN, Hyper-V, loopback) are skipped.</summary>
public static class NetAdapters
{
    public const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const int NcfPhysical = 0x4;

    public static IReadOnlyList<NetAdapter> Physical(IRegistry reg)
    {
        var list = new List<NetAdapter>();
        foreach (var sub in reg.GetSubKeyNames(RegHive.LocalMachine, ClassKey).Where(k => k.Length == 4 && k.All(char.IsDigit)))
        {
            var key = $@"{ClassKey}\{sub}";
            var characteristics = reg.GetValue(RegHive.LocalMachine, key, "Characteristics")?.AsInt() ?? 0;
            var desc = reg.GetValue(RegHive.LocalMachine, key, "DriverDesc")?.Data;
            if ((characteristics & NcfPhysical) == 0 || string.IsNullOrWhiteSpace(desc))
            {
                continue;
            }

            var ifType = reg.GetValue(RegHive.LocalMachine, key, "*IfType")?.AsInt();
            var keywords = reg.GetSubKeyNames(RegHive.LocalMachine, $@"{key}\Ndi\Params").ToHashSet(StringComparer.OrdinalIgnoreCase);
            list.Add(new NetAdapter(key, desc, ifType == 71) { Keywords = keywords });
        }

        return list;
    }

    /// <summary>Current value of an advanced property: the stored one, or the driver's default.</summary>
    public static string? Current(IRegistry reg, NetAdapter a, string keyword) =>
        reg.GetValue(RegHive.LocalMachine, a.ClassKey, keyword)?.Data ??
        reg.GetValue(RegHive.LocalMachine, $@"{a.ClassKey}\Ndi\Params\{keyword}", "default")?.Data;

    public static string? Max(IRegistry reg, NetAdapter a, string keyword) =>
        reg.GetValue(RegHive.LocalMachine, $@"{a.ClassKey}\Ndi\Params\{keyword}", "max")?.Data;

    /// <summary>Values the driver accepts for an enum property (Ndi\Params\keyword\enum).</summary>
    public static IReadOnlyList<string> Allowed(IRegistry reg, NetAdapter a, string keyword) =>
        reg.GetValueNames(RegHive.LocalMachine, $@"{a.ClassKey}\Ndi\Params\{keyword}\enum");

    /// <summary>The adapter is restarted for the driver to read new values: PowerShell, matched by its description.</summary>
    public static string RestartArgs(NetAdapter a) =>
        $"-NoProfile -NonInteractive -Command \"Restart-NetAdapter -InterfaceDescription '{a.Description.Replace("'", "''").Replace("\"", string.Empty)}' -Confirm:$false\"";
}

/// <summary>What one adapter property tweak sets.</summary>
public sealed record AdapterSetting(
    string Id,
    string Title,
    string[] Keywords,
    string Explanation,
    Effectiveness Effect,
    bool Disputed,
    string AppliesTo,
    bool WifiOnly = false,
    bool UseMax = false,
    string Value = "0");

/// <summary>One advanced adapter property (or a family of vendor variants of it). Shown only when the driver declares it.</summary>
public sealed class AdapterPropertyTweak : Tweak
{
    public static readonly AdapterSetting[] Settings =
    {
        new("irqmod", "Отключить Interrupt Moderation", new[] { "*InterruptModeration" },
            "Карта перестаёт копить пакеты перед прерыванием и отдаёт каждый сразу: задержка обработки падает на доли миллисекунды, нагрузка на процессор растёт. " +
            "На пинг до сервера не влияет — только на время внутри ПК.", Effectiveness.Low, Disputed: true,
            "Эффект минимальный; имеет смысл только для соревновательных шутеров на кабеле"),
        new("eee", "Отключить Energy Efficient Ethernet (энергосбережение линка)", new[] { "*EEE", "EEELinkAdvertisement", "EnableGreenEthernet", "AdvancedEEE", "GigaLite", "PowerSavingMode" },
            "Энергосбережение кабельного подключения: в паузах линк «засыпает», и первый пакет после паузы может задержаться. Иногда из-за EEE бывают редкие разрывы. " +
            "Выключение тратит чуть больше энергии.", Effectiveness.Low, Disputed: false,
            "Кабельное подключение; помогает при редких скачках пинга и разрывах, на средний пинг не влияет"),
        new("flow", "Отключить Flow Control", new[] { "*FlowControl" },
            "Механизм «притормозить отправку», когда буфер роутера полон. Выключение убирает паузы, но при перегруженной домашней сети пакеты будут теряться, а не ждать.", Effectiveness.Low, Disputed: true,
            "Эффект минимальный; включайте, только если замер показал выигрыш"),
        new("rxbuf", "Буферы приёма на максимум", new[] { "*ReceiveBuffers" },
            "Больше памяти под входящие пакеты: меньше потерь при всплесках трафика (загрузка + игра + стрим одновременно). На задержку не влияет.", Effectiveness.Low, Disputed: false,
            "Полезно, если в игре бывают потери пакетов при параллельных загрузках", UseMax: true),
        new("offload", "Отключить объединение пакетов (RSC) и LSO", new[] { "*RscIPv4", "*RscIPv6", "*LsoV2IPv4", "*LsoV2IPv6" },
            "Offloading склеивает TCP-пакеты в карте, чтобы разгрузить процессор. На TCP это может добавлять задержку, но большинство игр работает по UDP — на них эффекта нет.",
            Effectiveness.Low, Disputed: true, "Эффект минимальный: действует только на TCP, а большинство игр используют UDP"),
        new("roam", "Wi-Fi: минимальная агрессивность роуминга", new[] { "RoamAggressiveness" },
            "Карта реже сканирует эфир в поисках «лучшей» точки доступа. Сканирование даёт скачки пинга раз в несколько десятков секунд; если точка одна — оно не нужно.",
            Effectiveness.HardwareDependent, Disputed: false, "Wi-Fi с одним роутером: убирает периодические скачки пинга. С mesh-системой оставьте как есть", WifiOnly: true, Value: "0"),
    };

    public AdapterPropertyTweak(NetAdapter adapter, AdapterSetting setting)
    {
        Adapter = adapter;
        Setting = setting;
    }

    public NetAdapter Adapter { get; }
    public AdapterSetting Setting { get; }

    public override string Id => $"net.adapter.{Setting.Id}:{Adapter.Description.ToLowerInvariant()}";
    public override string Title => $"{Setting.Title} — {Adapter.Description}";
    public override TweakCategory Category => TweakCategory.Network;
    public override Effectiveness Effectiveness => Setting.Effect;
    public override bool Recommended => false;
    public override bool Disputed => Setting.Disputed;
    public override string Explanation => Setting.Explanation;
    public override string AppliesTo => Setting.AppliesTo;
    public override string Risk => "Низкий, откатывается. Сеть пропадёт на несколько секунд, пока карта перезапускается. Нужны права администратора";

    /// <summary>The tweaks that this adapter's driver supports.</summary>
    public static IEnumerable<AdapterPropertyTweak> For(NetAdapter a) =>
        Settings.Where(s => (!s.WifiOnly || a.IsWifi) && (s.Id != "eee" || !a.IsWifi) && s.Keywords.Any(a.Keywords.Contains))
            .Select(s => new AdapterPropertyTweak(a, s));

    public override TweakPlan Plan(TweakContext ctx)
    {
        var actions = new List<PlannedAction>();
        foreach (var kw in Setting.Keywords.Where(Adapter.Keywords.Contains))
        {
            var wanted = Setting.UseMax ? NetAdapters.Max(ctx.Registry, Adapter, kw) : Setting.Value;
            if (wanted is null)
            {
                continue;
            }

            // Enum properties accept only listed values; skip a variant whose driver doesn't list "0".
            var allowed = NetAdapters.Allowed(ctx.Registry, Adapter, kw);
            if (!Setting.UseMax && allowed.Count > 0 && !allowed.Contains(wanted))
            {
                continue;
            }

            if (NetAdapters.Current(ctx.Registry, Adapter, kw) != wanted)
            {
                actions.Add(PlannedAction.SetReg(RegHive.LocalMachine, Adapter.ClassKey, kw, RegValue.Str(wanted), $"{kw} = {wanted}"));
            }
        }

        if (actions.Count == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже так настроено в драйвере.", recommendedHere: false);
        }

        var restart = NetAdapters.RestartArgs(Adapter);
        // First step: nothing on apply, a restart on undo (undo runs backwards, so it restarts after the values are back).
        actions.Insert(0, PlannedAction.Run("cmd.exe", "/c exit 0", "powershell.exe", restart, "Перезапустить сетевую карту после отката", requiresAdmin: true));
        actions.Add(PlannedAction.Run("powershell.exe", restart, "cmd.exe", "/c exit 0", $"Перезапустить «{Adapter.Description}», чтобы драйвер применил настройку", requiresAdmin: true));
        return Result(TweakStatus.CanApply, "Сейчас по умолчанию драйвера.", actions, recommendedHere: false);
    }
}

/// <summary>Restores TCP receive window auto-tuning if a "booster" switched it off; switched off it only slows downloads.</summary>
public sealed partial class AutotuningTweak : Tweak
{
    public override string Id => "net.autotuning";
    public override string Title => "Автонастройка окна TCP: стандартный режим Windows";
    public override TweakCategory Category => TweakCategory.Network;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override string AppliesTo => "Загрузки Steam и лаунчеров; на пинг в играх (UDP) не влияет";
    public override string Risk => "Минимальный, откатывается. Нужны права администратора";

    public override string Explanation =>
        "Сборники «сетевых твиков» часто выключают автонастройку окна приёма TCP. Пинг в играх от этого не меняется, а загрузки на быстром интернете становятся медленнее. " +
        "Пункт возвращает стандартное значение Windows — normal.";

    [GeneratedRegex(@"(?im)^[^\r\n]*(auto-?tuning|автонастрой)[^\r\n]*:\s*(\w+)")]
    private static partial Regex Line();

    public static string? Parse(string netshGlobal)
    {
        var m = Line().Match(netshGlobal);
        return m.Success ? m.Groups[2].Value.ToLowerInvariant() : null;
    }

    public override TweakPlan Plan(TweakContext ctx)
    {
        var level = Parse(ctx.Commands.Run("netsh", "int tcp show global").StdOut);
        if (level is null)
        {
            return Result(TweakStatus.NotApplicable, "Не удалось прочитать настройки TCP.", recommendedHere: false);
        }

        if (level == "normal")
        {
            return Result(TweakStatus.AlreadyOptimal, "Стандартный режим (normal).");
        }

        return Result(TweakStatus.CanApply, $"Сейчас: {level} — кто-то изменил стандартное значение.", new[]
        {
            PlannedAction.Run("netsh", "int tcp set global autotuninglevel=normal", "netsh", $"int tcp set global autotuninglevel={level}", "Автонастройка окна TCP: normal", requiresAdmin: true),
        });
    }
}

/// <summary>BBR2 congestion control for TCP (Windows 11 22H2+). TCP only: downloads, not game traffic.</summary>
public sealed partial class CongestionTweak : Tweak
{
    private static readonly string[] Providers = { "bbr2", "cubic", "ctcp", "dctcp", "newreno", "ledbat" };

    public override string Id => "net.congestion";
    public override string Title => "Алгоритм перегрузки TCP: BBR2";
    public override TweakCategory Category => TweakCategory.Network;
    public override Effectiveness Effectiveness => Effectiveness.Low;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override TweakRisk RiskLevel => TweakRisk.Medium;
    public override string AppliesTo => "Эффект минимальный для игр: действует только на TCP (загрузки, лаунчеры), а игровой трафик почти всегда UDP";
    public override string Risk => "Средний: в отдельных сборках Windows 11 с BBR2 замечали замедление локальных подключений. Откат возвращает прежний алгоритм. Нужны права администратора";

    public override string Explanation =>
        "BBR2 держит очередь в роутере короче, чем стандартный CUBIC: при загрузке в фоне пинг растёт меньше. На саму игру (UDP) не действует.";

    [GeneratedRegex(@"(?i)\b(bbr2|cubic|ctcp|dctcp|newreno|ledbat)\b")]
    private static partial Regex Provider();

    public static string? Parse(string supplemental)
    {
        var m = Provider().Match(supplemental);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (ctx.Snapshot.Windows is not { Build: >= 22621 })
        {
            return Result(TweakStatus.NotApplicable, "BBR2 есть в Windows 11 22H2 и новее.", recommendedHere: false);
        }

        var current = Parse(ctx.Commands.Run("netsh", "int tcp show supplemental template=internet").StdOut);
        if (current is null || !Providers.Contains(current))
        {
            return Result(TweakStatus.NotApplicable, "Не удалось прочитать алгоритм TCP.", recommendedHere: false);
        }

        if (current == "bbr2")
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже BBR2.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, $"Сейчас: {current}.", new[]
        {
            PlannedAction.Run("netsh", "int tcp set supplemental template=internet congestionprovider=bbr2", "netsh", $"int tcp set supplemental template=internet congestionprovider={current}",
                "Алгоритм перегрузки TCP: BBR2", requiresAdmin: true),
        }, recommendedHere: false);
    }
}

/// <summary>
/// Policy-based QoS: marks a game's packets with DSCP 46 ("expedited"). Helps only if the router prioritises by DSCP
/// (WMM on Wi-Fi, or QoS by DSCP on the router); otherwise nothing changes. The policy is a registry key, so it is undone exactly.
/// </summary>
public sealed class DscpTweak : Tweak
{
    public const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
    private const string NlaKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\QoS";

    public DscpTweak(string exeName, string gameName)
    {
        ExeName = exeName;
        GameName = gameName;
    }

    public string ExeName { get; }
    public string GameName { get; }

    private string Key => $@"{PolicyRoot}\FrameLab {ExeName}";

    public override string Id => "net.dscp:" + ExeName.ToLowerInvariant();
    public override string Title => $"Приоритет пакетов DSCP 46 для {GameName}";
    public override TweakCategory Category => TweakCategory.Network;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override string AppliesTo => "Только если роутер учитывает DSCP (WMM на Wi-Fi или QoS по DSCP). Помогает, когда кто-то дома качает или смотрит видео; на пинг до сервера в интернете не влияет";
    public override string Risk => "Низкий, откатывается. Нужны права администратора";

    public override string Explanation =>
        $"Windows помечает пакеты {ExeName} как срочные (DSCP 46) — штатная групповая политика QoS. Роутер с поддержкой приоритизации пропускает их первыми, " +
        "если канал занят другими устройствами. Провайдер эту метку обычно сбрасывает, так что за пределами дома эффекта нет.";

    private IEnumerable<(string Name, string Value)> Values() => new[]
    {
        ("Version", "1.0"), ("Application Name", ExeName), ("Protocol", "*"), ("Local Port", "*"), ("Local IP", "*"), ("Local IP Prefix Length", "*"),
        ("Remote Port", "*"), ("Remote IP", "*"), ("Remote IP Prefix Length", "*"), ("DSCP Value", "46"), ("Throttle Rate", "-1"),
    };

    public override TweakPlan Plan(TweakContext ctx)
    {
        var actions = Values()
            .Where(v => ctx.Registry.GetValue(RegHive.LocalMachine, Key, v.Name)?.Data != v.Value)
            .Select(v => PlannedAction.SetReg(RegHive.LocalMachine, Key, v.Name, RegValue.Str(v.Value), $"QoS {ExeName}: {v.Name} = {v.Value}"))
            .ToList();
        if (actions.Count == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Политика уже есть.", recommendedHere: false);
        }

        // Outside a domain Windows applies QoS policies only with this switch.
        if (ctx.Registry.GetValue(RegHive.LocalMachine, NlaKey, "Do not use NLA")?.Data != "1")
        {
            actions.Add(PlannedAction.SetReg(RegHive.LocalMachine, NlaKey, "Do not use NLA", RegValue.Str("1"), "Применять политики QoS без домена"));
        }

        actions.Add(PlannedAction.Run("gpupdate.exe", "/target:computer /force", "gpupdate.exe", "/target:computer /force", "Обновить групповые политики", requiresAdmin: true));
        return Result(TweakStatus.CanApply, "Политики QoS для игры нет.", actions, recommendedHere: false);
    }
}

/// <summary>All network tweaks for this PC: TCP settings, adapter properties the drivers support, DSCP for the given games.</summary>
public static class NetworkTweakCatalog
{
    public static IReadOnlyList<Tweak> For(IRegistry reg, IEnumerable<(string Exe, string Name)> games)
    {
        var list = new List<Tweak> { new AutotuningTweak(), new CongestionTweak() };
        foreach (var a in NetAdapters.Physical(reg))
        {
            list.AddRange(AdapterPropertyTweak.For(a));
        }

        list.AddRange(games.DistinctBy(g => g.Exe, StringComparer.OrdinalIgnoreCase).Select(g => new DscpTweak(g.Exe, g.Name)));
        return list;
    }
}
