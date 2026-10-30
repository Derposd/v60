using System.Runtime.InteropServices;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Upscale;

/// <summary>What Direct3D reports for one adapter. FeatureLevel is 0xMm00 (0xB000 = 11_0, 0xC100 = 12_1), 0 when unknown.</summary>
public sealed record GpuCaps(string Name, int FeatureLevel, bool Dx12, int Index = -1, uint VendorId = 0, uint DeviceId = 0)
{
    public string FeatureLevelText => FeatureLevel == 0 ? "не определён" : $"{FeatureLevel >> 12}_{(FeatureLevel >> 8) & 0xF}";
    public bool Fl11 => FeatureLevel >= 0xB000;
}

/// <summary>
/// Creates a throw-away D3D11 device on each adapter (no window, nothing drawn) to read the highest supported feature level,
/// and asks D3D12 whether a device could be created. Read-only; runs once at start of the «Апскейл и кадры» page.
/// </summary>
public static class GpuProbe
{
    private const int DriverTypeUnknown = 0;
    private const int DriverTypeHardware = 1;
    private const uint SdkVersion = 7;
    private const int EInvalidArg = unchecked((int)0x80070057);
    private const int SFalse = 1;

    private static readonly int[] AllLevels = { 0xC200, 0xC100, 0xC000, 0xB100, 0xB000, 0xA100, 0xA000, 0x9300, 0x9200, 0x9100 };

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, int[] levels, uint count, uint sdk,
        out IntPtr device, out int level, out IntPtr context);

    [DllImport("d3d12.dll", ExactSpelling = true)]
    private static extern int D3D12CreateDevice(IntPtr adapter, int minLevel, ref Guid riid, IntPtr device);

    private static Guid _id3d12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    public static IReadOnlyList<GpuCaps> Probe(IReadOnlyList<GpuInfo> gpus)
    {
        var result = new List<GpuCaps>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        try
        {
            var iid = typeof(Native.IDXGIFactory1).GUID;
            if (Native.CreateDXGIFactory1(ref iid, out var factory) != 0)
            {
                return result;
            }

            for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0 && adapter is not null; i++)
            {
                try
                {
                    if (adapter.GetDesc1(out var desc) != 0 || (desc.Flags & Native.DxgiAdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    var ptr = Marshal.GetComInterfaceForObject(adapter, typeof(Native.IDXGIAdapter1));
                    try
                    {
                        result.Add(new GpuCaps(desc.Description.Trim(), FeatureLevel(ptr), Dx12(ptr), (int)i, desc.VendorId, desc.DeviceId));
                    }
                    finally
                    {
                        Marshal.Release(ptr);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }

            Marshal.ReleaseComObject(factory);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            Log.Warn($"Не удалось определить возможности DirectX: {ex.Message}");
        }

        // Adapters DXGI didn't list (rare) keep their WMI name with an unknown level.
        foreach (var g in gpus.Where(g => g.Vendor is not GpuVendor.Microsoft and not GpuVendor.Unknown))
        {
            if (!result.Any(r => r.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new GpuCaps(g.Name, 0, false));
            }
        }

        return result;
    }

    private static int FeatureLevel(IntPtr adapter)
    {
        // Older runtimes reject levels they don't know (12_x) with E_INVALIDARG: retry with a shorter list.
        for (var skip = 0; skip < 3; skip++)
        {
            var levels = AllLevels.Skip(skip).ToArray();
            var hr = D3D11CreateDevice(adapter, adapter == IntPtr.Zero ? DriverTypeHardware : DriverTypeUnknown, IntPtr.Zero, 0, levels, (uint)levels.Length,
                SdkVersion, out var device, out var level, out var context);
            if (hr == EInvalidArg)
            {
                continue;
            }

            if (device != IntPtr.Zero)
            {
                Marshal.Release(device);
            }

            if (context != IntPtr.Zero)
            {
                Marshal.Release(context);
            }

            return hr >= 0 ? level : 0;
        }

        return 0;
    }

    private static bool Dx12(IntPtr adapter)
    {
        try
        {
            // With a null output pointer D3D12 only checks: S_FALSE means "a device could be created".
            var hr = D3D12CreateDevice(adapter, 0xB000, ref _id3d12Device, IntPtr.Zero);
            return hr == SFalse || hr == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
