using System.Globalization;
using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>One registry value a tweak wants. <paramref name="Default"/> is what Windows behaves like when the value is missing.</summary>
public sealed record RegSetting(RegHive Hive, string Key, string Name, RegValue Value, string Text, RegValue? Default = null)
{
    public bool Matches(IRegistry reg) => Same(reg.GetValue(Hive, Key, Name) ?? Default, Value);

    public static bool Same(RegValue? current, RegValue wanted) =>
        current is not null && (wanted.Kind is RegKind.DWord or RegKind.QWord
            ? current.AsInt()?.ToString(CultureInfo.InvariantCulture) == wanted.Data
            : string.Equals(current.Data, wanted.Data, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A tweak that is nothing but a set of registry values. "Applied" when every value already matches; applying writes
/// only the ones that differ, so the backup holds exactly what was changed.
/// </summary>
public sealed class RegistryTweak : Tweak
{
    public required string TweakId { get; init; }
    public required string Name { get; init; }
    public required TweakCategory Group { get; init; }
    public required Effectiveness Effect { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<RegSetting> Settings { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsDisputed { get; init; }
    public bool NeedsRestart { get; init; }
    public TweakRisk Level { get; init; } = TweakRisk.Low;
    public string? RiskText { get; init; }
    public string? Games { get; init; }
    public string? Manual { get; init; }

    /// <summary>Returns a reason when the tweak doesn't apply to this PC (e.g. Windows 10 only).</summary>
    public Func<SystemSnapshot, string?>? NotApplicableWhen { get; init; }

    public override string Id => TweakId;
    public override string Title => Name;
    public override TweakCategory Category => Group;
    public override Effectiveness Effectiveness => Effect;
    public override string Explanation => Description;
    public override bool Recommended => IsRecommended;
    public override bool Disputed => IsDisputed;
    public override bool RequiresRestart => NeedsRestart;
    public override TweakRisk RiskLevel => Level;
    public override string? AppliesTo => Games;
    public override string? ManualSteps => Manual;

    public override string Risk => RiskText ?? (Settings.Any(s => s.Hive == RegHive.LocalMachine)
        ? "Низкий, полностью откатывается. Нужны права администратора"
        : "Минимальный, полностью откатывается");

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (NotApplicableWhen?.Invoke(ctx.Snapshot) is { } reason)
        {
            return Result(TweakStatus.NotApplicable, reason, recommendedHere: false);
        }

        var actions = Settings
            .Where(s => !s.Matches(ctx.Registry))
            .Select(s => PlannedAction.SetReg(s.Hive, s.Key, s.Name, s.Value, s.Text))
            .ToList();
        if (actions.Count == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Уже так настроено.");
        }

        var done = Settings.Count - actions.Count;
        var state = done == 0 ? "Не настроено (по умолчанию Windows)." : $"Настроено частично: {done} из {Settings.Count}.";
        return Result(TweakStatus.CanApply, state + (NeedsRestart ? " Вступит в силу после перезагрузки или повторного входа." : string.Empty), actions);
    }
}

/// <summary>One service a tweak moves to another start type.</summary>
public sealed record ServiceSetting(string Name, string Start, bool Stop, string Text);

/// <summary>
/// Services switched to «Вручную» (never deleted, never disabled unless stated): Windows can still start them when something asks.
/// Services that are not installed are skipped; if none is installed the tweak doesn't apply.
/// </summary>
public sealed class ServiceTweak : Tweak
{
    public required string TweakId { get; init; }
    public required string Name { get; init; }
    public required Effectiveness Effect { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<ServiceSetting> Services { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsDisputed { get; init; }
    public TweakRisk Level { get; init; } = TweakRisk.Low;
    public string? RiskText { get; init; }
    public string? Games { get; init; }

    /// <summary>Extra registry values applied together with the services (e.g. a policy).</summary>
    public IReadOnlyList<RegSetting> Registry { get; init; } = Array.Empty<RegSetting>();

    /// <summary>Returns a reason when the tweak doesn't apply to this PC.</summary>
    public Func<TweakContext, string?>? NotApplicableWhen { get; init; }

    /// <summary>Service state source; the real one goes through sc.exe.</summary>
    public static Func<TweakContext, IServiceControl> ServiceSource { get; set; } = ctx => new ServiceControl(ctx.Registry, ctx.Commands);

    public override string Id => TweakId;
    public override string Title => Name;
    public override TweakCategory Category => TweakCategory.Services;
    public override Effectiveness Effectiveness => Effect;
    public override string Explanation => Description;
    public override bool Recommended => IsRecommended;
    public override bool Disputed => IsDisputed;
    public override TweakRisk RiskLevel => Level;
    public override string? AppliesTo => Games;
    public override string Risk => RiskText ?? "Низкий: службы переводятся в «Вручную», не удаляются; откат возвращает тип запуска и запускает их. Нужны права администратора";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (NotApplicableWhen?.Invoke(ctx) is { } reason)
        {
            return Result(TweakStatus.NotApplicable, reason, recommendedHere: false);
        }

        var sc = ServiceSource(ctx);
        var installed = Services.Select(s => (Setting: s, State: sc.Query(s.Name))).Where(x => x.State is not null).ToList();
        if (installed.Count == 0)
        {
            return Result(TweakStatus.NotApplicable, "Этих служб в системе нет.", recommendedHere: false);
        }

        var actions = new List<PlannedAction>();
        foreach (var (s, state) in installed)
        {
            // Already manual (or disabled by the user): nothing to do. A manual service that Windows started on demand is fine.
            if (state!.Start != s.Start && state.Start != ServiceStart.Disabled)
            {
                actions.Add(PlannedAction.SetService(s.Name, s.Start, s.Stop, s.Text));
            }
        }

        actions.AddRange(Registry.Where(r => !r.Matches(ctx.Registry)).Select(r => PlannedAction.SetReg(r.Hive, r.Key, r.Name, r.Value, r.Text)));
        if (actions.Count == 0)
        {
            return Result(TweakStatus.AlreadyOptimal, "Службы уже в режиме «Вручную».");
        }

        var running = installed.Where(x => x.State!.Running).Select(x => x.Setting.Name).ToList();
        return Result(TweakStatus.CanApply,
            $"Сейчас: {string.Join(", ", installed.Select(x => $"{x.Setting.Name} — {ServiceStart.Ru(x.State!.Start)}"))}." +
            (running.Count > 0 ? $" Работают: {string.Join(", ", running)}." : string.Empty), actions);
    }
}
