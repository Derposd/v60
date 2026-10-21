using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App;

/// <summary>
/// Windows reads mouse acceleration and the accessibility shortcuts from the registry only at sign-in.
/// After the app changes or restores those values, they are pushed to the running session here
/// (without SPIF_UPDATEINIFILE: the registry already holds the right values).
/// </summary>
internal static class UserInputSettings
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpiGetFilterKeys = 0x0032;
    private const uint SpiSetFilterKeys = 0x0033;
    private const uint SpiGetToggleKeys = 0x0034;
    private const uint SpiSetToggleKeys = 0x0035;
    private const uint SpiGetStickyKeys = 0x003A;
    private const uint SpiSetStickyKeys = 0x003B;
    private const uint SpifSendChange = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlagsOnly
    {
        public uint CbSize;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterKeys
    {
        public uint CbSize;
        public uint Flags;
        public uint WaitMSec;
        public uint DelayMSec;
        public uint RepeatMSec;
        public uint BounceMSec;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, int[] value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoFlags(uint action, uint param, ref FlagsOnly value, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoFilter(uint action, uint param, ref FilterKeys value, uint winIni);

    public static void Reload(IRegistry reg)
    {
        try
        {
            ReloadMouse(reg);
            ReloadShortcut(reg, 0, SpiGetStickyKeys, SpiSetStickyKeys);
            ReloadShortcut(reg, 2, SpiGetToggleKeys, SpiSetToggleKeys);
            ReloadFilterKeys(reg);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn($"Не удалось обновить настройки мыши и клавиатуры без перезахода: {ex.Message}");
        }
    }

    private static int? Read(IRegistry reg, string name) =>
        int.TryParse(reg.GetValue(RegHive.CurrentUser, MouseAccelerationTweak.Key, name)?.Data, out var v) ? v : null;

    private static void ReloadMouse(IRegistry reg)
    {
        // Only values that are actually stored are pushed; a missing value leaves the session as it is.
        if (Read(reg, "MouseThreshold1") is not { } t1 || Read(reg, "MouseThreshold2") is not { } t2 || Read(reg, "MouseSpeed") is not { } speed)
        {
            return;
        }

        var current = new int[3];
        if (!SystemParametersInfo(SpiGetMouse, 0, current, 0))
        {
            return;
        }

        var wanted = new[] { t1, t2, speed };
        if (!current.SequenceEqual(wanted))
        {
            SystemParametersInfo(SpiSetMouse, 0, wanted, SpifSendChange);
        }
    }

    private static uint? HotkeyBit(IRegistry reg, int feature)
    {
        var (key, _, _) = AccessibilityHotkeysTweak.Features[feature];
        return reg.GetValue(RegHive.CurrentUser, key, "Flags") is null
            ? null
            : (uint)(AccessibilityHotkeysTweak.ReadFlags(reg, key, 0) & AccessibilityHotkeysTweak.HotkeyActive);
    }

    private static void ReloadShortcut(IRegistry reg, int feature, uint get, uint set)
    {
        if (HotkeyBit(reg, feature) is not { } bit)
        {
            return;
        }

        var s = new FlagsOnly { CbSize = (uint)Marshal.SizeOf<FlagsOnly>() };
        if (!SystemParametersInfoFlags(get, s.CbSize, ref s, 0))
        {
            return;
        }

        var flags = (s.Flags & ~(uint)AccessibilityHotkeysTweak.HotkeyActive) | bit;
        if (flags != s.Flags)
        {
            s.Flags = flags;
            SystemParametersInfoFlags(set, s.CbSize, ref s, SpifSendChange);
        }
    }

    private static void ReloadFilterKeys(IRegistry reg)
    {
        if (HotkeyBit(reg, 1) is not { } bit)
        {
            return;
        }

        var s = new FilterKeys { CbSize = (uint)Marshal.SizeOf<FilterKeys>() };
        if (!SystemParametersInfoFilter(SpiGetFilterKeys, s.CbSize, ref s, 0))
        {
            return;
        }

        var flags = (s.Flags & ~(uint)AccessibilityHotkeysTweak.HotkeyActive) | bit;
        if (flags != s.Flags)
        {
            s.Flags = flags;
            SystemParametersInfoFilter(SpiSetFilterKeys, s.CbSize, ref s, SpifSendChange);
        }
    }
}
