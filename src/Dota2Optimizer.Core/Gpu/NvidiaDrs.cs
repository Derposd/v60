using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Gpu;

/// <summary>NVIDIA driver settings (the global profile of «Панель управления NVIDIA → Управление параметрами 3D»).</summary>
public interface INvidiaDrs
{
    bool Available { get; }

    /// <summary>Name the driver gives a setting id, or null if the driver doesn't know it. Used to check every id before use.</summary>
    string? NameOf(uint id);

    /// <summary>Value set in the global profile, or null when the setting is at the driver default (not stored).</summary>
    uint? Get(uint id);

    void Set(uint id, uint value);

    /// <summary>Removes the setting from the profile: the driver default applies again.</summary>
    void Reset(uint id);
}

/// <summary>
/// NVAPI driver settings (DRS) through nvapi64.dll, the same interface NVIDIA Profile Inspector uses. The setting structure is
/// handled as a raw buffer with the documented layout (NVDRS_SETTING_V1, 12320 bytes) to avoid marshalling surprises.
/// Writing needs administrator rights, so changes run in the elevated helper.
/// </summary>
public sealed class NvidiaDrs : INvidiaDrs
{
    private const int SettingSize = 0x3020;
    private const uint SettingVersion = SettingSize | (1 << 16);
    private const int OffId = 4 + 4096;
    private const int OffType = OffId + 4;
    private const int OffCurrent = OffType + 4 + 4 + 4 + 4 + 4100;
    private const int Ok = 0;
    private const int SettingNotFound = -160;

    private static readonly Lazy<Api?> Instance = new(Api.Load);

    public bool Available => Instance.Value is not null;

    public string? NameOf(uint id)
    {
        var api = Instance.Value;
        if (api?.GetSettingNameFromId is null)
        {
            return null;
        }

        var buf = Marshal.AllocHGlobal(4096);
        try
        {
            Marshal.Copy(new byte[4096], 0, buf, 4096);
            return api.GetSettingNameFromId(id, buf) == Ok ? Marshal.PtrToStringUni(buf) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public uint? Get(uint id) => WithBase((api, session, profile) =>
    {
        var buf = NewSetting();
        try
        {
            var status = api.GetSetting(session, profile, id, buf);
            if (status == SettingNotFound)
            {
                return (uint?)null;
            }

            Check(status, "GetSetting");
            return (uint)Marshal.ReadInt32(buf, OffCurrent);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    });

    public void Set(uint id, uint value) => WithBase((api, session, profile) =>
    {
        var buf = NewSetting();
        try
        {
            Marshal.WriteInt32(buf, OffId, unchecked((int)id));
            Marshal.WriteInt32(buf, OffType, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(buf, OffCurrent, unchecked((int)value));
            Check(api.SetSetting(session, profile, buf), "SetSetting");
            Check(api.SaveSettings(session), "SaveSettings");
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    });

    public void Reset(uint id) => WithBase((api, session, profile) =>
    {
        var status = api.DeleteProfileSetting(session, profile, id);
        if (status != SettingNotFound)
        {
            Check(status, "DeleteProfileSetting");
        }

        Check(api.SaveSettings(session), "SaveSettings");
        return true;
    });

    private static IntPtr NewSetting()
    {
        var buf = Marshal.AllocHGlobal(SettingSize);
        Marshal.Copy(new byte[SettingSize], 0, buf, SettingSize);
        Marshal.WriteInt32(buf, 0, unchecked((int)SettingVersion));
        return buf;
    }

    private static T WithBase<T>(Func<Api, IntPtr, IntPtr, T> body)
    {
        var api = Instance.Value ?? throw new InvalidOperationException("Драйвер NVIDIA (nvapi64.dll) не найден");
        Check(api.CreateSession(out var session), "CreateSession");
        try
        {
            Check(api.LoadSettings(session), "LoadSettings");
            Check(api.GetBaseProfile(session, out var profile), "GetBaseProfile");
            return body(api, session, profile);
        }
        finally
        {
            api.DestroySession(session);
        }
    }

    private static void Check(int status, string what)
    {
        if (status != Ok)
        {
            throw new InvalidOperationException(status == -137
                ? "NVAPI: нужны права администратора"
                : $"NVAPI {what}: код {status}");
        }
    }

    private sealed class Api
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int InitializeFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SessionOutFn(out IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SessionFn(IntPtr session);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int BaseProfileFn(IntPtr session, out IntPtr profile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int DeleteSettingFn(IntPtr session, IntPtr profile, uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int NameFn(uint id, IntPtr name);

        public required SessionOutFn CreateSession { get; init; }
        public required SessionFn DestroySession { get; init; }
        public required SessionFn LoadSettings { get; init; }
        public required SessionFn SaveSettings { get; init; }
        public required BaseProfileFn GetBaseProfile { get; init; }
        public required GetSettingFn GetSetting { get; init; }
        public required SetSettingFn SetSetting { get; init; }
        public required DeleteSettingFn DeleteProfileSetting { get; init; }
        public NameFn? GetSettingNameFromId { get; init; }

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        public static Api? Load()
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            {
                return null;
            }

            try
            {
                var init = Fn<InitializeFn>(0x0150E828);
                if (init is null || init() != Ok)
                {
                    return null;
                }

                var create = Fn<SessionOutFn>(0x0694D52E);
                var destroy = Fn<SessionFn>(0xDAD9CFF8);
                var load = Fn<SessionFn>(0x375DBD6B);
                var save = Fn<SessionFn>(0xFCBC7E14);
                var baseProfile = Fn<BaseProfileFn>(0xDA8466A0);
                var get = Fn<GetSettingFn>(0x73BF8338);
                var set = Fn<SetSettingFn>(0x577DD202);
                var del = Fn<DeleteSettingFn>(0xE4A26362);
                if (create is null || destroy is null || load is null || save is null || baseProfile is null || get is null || set is null || del is null)
                {
                    return null;
                }

                return new Api
                {
                    CreateSession = create,
                    DestroySession = destroy,
                    LoadSettings = load,
                    SaveSettings = save,
                    GetBaseProfile = baseProfile,
                    GetSetting = get,
                    SetSetting = set,
                    DeleteProfileSetting = del,
                    GetSettingNameFromId = Fn<NameFn>(0xD61CBE6E),
                };
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Log.Info($"NVAPI недоступен: {ex.Message}");
                return null;
            }
        }

        private static T? Fn<T>(uint id)
            where T : Delegate
        {
            var p = QueryInterface(id);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
        }
    }
}
