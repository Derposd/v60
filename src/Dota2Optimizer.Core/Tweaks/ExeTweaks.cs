using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>«Отключить оптимизацию во весь экран» for any game exe the user added (the same checkbox as in the exe's Compatibility tab).</summary>
public sealed class FsoExeTweak : Tweak
{
    private const string Key = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string Flag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    public FsoExeTweak(string exePath)
    {
        ExePath = exePath;
    }

    public string ExePath { get; }

    public override string Id => "fso:" + ExePath.ToLowerInvariant();
    public override string Title => $"Отключить оптимизацию во весь экран для {PathEx.FileName(ExePath)}";
    public override TweakCategory Category => TweakCategory.Game;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override bool Recommended => false;
    public override bool Disputed => true;
    public override string AppliesTo => "Только эксклюзивный полноэкранный режим DirectX 9–11; на DX12, Vulkan и «окно без рамки» не влияет";

    public override string Explanation =>
        "Та же галочка, что в «Свойства → Совместимость». На части систем убирает микрофризы и задержку при Alt+Tab в полноэкранном режиме, " +
        "на других ничего не меняет или ломает оверлеи и HDR. Включайте, только если замер показал выигрыш.";

    public override TweakPlan Plan(TweakContext ctx)
    {
        if (!ctx.FileSystem.FileExists(ExePath))
        {
            return Result(TweakStatus.NotApplicable, "Файл игры не найден.", recommendedHere: false);
        }

        var current = ctx.Registry.GetValue(RegHive.CurrentUser, Key, ExePath)?.Data;
        if (RegistryStrings.HasLayerFlag(current, Flag))
        {
            return Result(TweakStatus.AlreadyOptimal, "Отключена.", recommendedHere: false);
        }

        return Result(TweakStatus.CanApply, "Не отключена (по умолчанию Windows).", new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, Key, ExePath, RegValue.Str(RegistryStrings.AddLayerFlag(current, Flag)), $"Оптимизация во весь экран для {PathEx.FileName(ExePath)}: выкл."),
        }, recommendedHere: false);
    }
}
