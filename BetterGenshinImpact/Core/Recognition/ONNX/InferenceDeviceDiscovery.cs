using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SharpDX.DXGI;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public interface IInferenceDeviceDiscovery
{
    IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string runtimeDirectory);
}

/// <summary>只探测硬件 API，不加载、替换其他引擎的 onnxruntime.dll。</summary>
public sealed class InferenceDeviceDiscovery : IInferenceDeviceDiscovery
{
    public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string runtimeDirectory) => engine switch
    {
        InferenceDeviceType.Cpu => [ResolvedInferenceRuntime.CpuDevice],
        InferenceDeviceType.DirectML => GetDirectMlDevices(),
        InferenceDeviceType.CUDA or InferenceDeviceType.TensorRT => GetNvidiaDevices(engine),
        InferenceDeviceType.OpenVINO => GetOpenVinoDevices(runtimeDirectory),
        _ => []
    };

    private static IReadOnlyList<InferenceDeviceInfo> GetDirectMlDevices()
    {
        if (!BetterGenshinImpact.Helpers.OsVersionHelper.IsWindows10_1903_OrGreater) return [];
        var devices = new List<InferenceDeviceInfo>();
        using var factory = new Factory1();
        for (var index = 0; ; index++)
        {
            Adapter1 adapter;
            try { adapter = factory.GetAdapter1(index); }
            catch (SharpDX.SharpDXException) { break; }
            using (adapter)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) != 0) continue;
                var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
                var result = D3D12CreateDevice(adapter.NativePointer, 0xb000, ref iid, out var device);
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (result < 0) continue;
                var id = GetDirectMlIdentity(description);
                devices.Add(new(id, description.Description.Trim(), index, index.ToString()));
            }
        }
        return NormalizeDirectMlDevices(devices);
    }

    public static IReadOnlyList<InferenceDeviceInfo> NormalizeDirectMlDevices(IReadOnlyList<InferenceDeviceInfo> devices)
    {
        // DXGI 可能暴露同一物理显卡的多个适配器入口。保留原始序号，不重新编号。
        var unique = devices.DistinctBy(device => device.Id).ToArray();
        return unique.Select(device => unique.Count(other => other.Name == device.Name) > 1
            ? device with { Name = $"{device.Name}（设备 {device.Index + 1}）" } : device).ToArray();
    }

    private static string GetDirectMlIdentity(AdapterDescription1 description)
    {
        // LUID 只在一次 Windows 启动期间稳定，持久化时改用显卡的 PCI 位置和型号。
        var adapter = new KernelAdapter { Luid = description.Luid };
        if (D3DKMTOpenAdapterFromLuid(ref adapter) >= 0)
        {
            var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AdapterAddress>());
            try
            {
                var query = new AdapterQuery { Handle = adapter.Handle, Type = 6, Data = buffer, Size = (uint)Marshal.SizeOf<AdapterAddress>() };
                if (D3DKMTQueryAdapterInfo(ref query) >= 0)
                {
                    var address = Marshal.PtrToStructure<AdapterAddress>(buffer);
                    return $"DXGI:{description.VendorId:X4}:{description.DeviceId:X4}:{address.Bus}:{address.Device}:{address.Function}";
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                var handle = adapter.Handle;
                D3DKMTCloseAdapter(ref handle);
            }
        }
        // 非 PCI 适配器保留型号与 LUID，避免误合并多张同型号显卡。跨启动标识失效时重新选设备。
        return $"DXGI:{description.VendorId:X4}:{description.DeviceId:X4}:{description.SubsystemId:X8}:{description.Luid:X16}";
    }

    private static IReadOnlyList<InferenceDeviceInfo> GetNvidiaDevices(InferenceDeviceType engine)
    {
        if (!NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvcuda.dll"), out var library)) return [];
        try
        {
            var init = Get<CudaInit>(library, "cuInit");
            var count = Get<CudaCount>(library, "cuDeviceGetCount");
            var get = Get<CudaDevice>(library, "cuDeviceGet");
            var name = Get<CudaName>(library, "cuDeviceGetName");
            var uuid = Get<CudaUuid>(library, "cuDeviceGetUuid");
            var attribute = Get<CudaAttribute>(library, "cuDeviceGetAttribute");
            var driverVersion = Get<CudaCount>(library, "cuDriverGetVersion");
            if (init(0) != 0 || driverVersion(out var version) != 0 || version < 12000 || count(out var size) != 0) return [];
            var devices = new List<InferenceDeviceInfo>();
            var compatible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var ordinal = 0; ordinal < size; ordinal++)
            {
                if (get(out var device, ordinal) != 0) continue;
                var text = new StringBuilder(256);
                if (name(text, text.Capacity, device) != 0 || uuid(out var id, device) != 0) continue;
                if (attribute(out var major, 75, device) != 0 || attribute(out var minor, 76, device) != 0) continue;
                // TensorRT 10.8 的硬件下限为 Turing (SM 7.5)，不把旧显卡作为可选设备。
                var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref id, 1));
                var hex = Convert.ToHexString(bytes).ToLowerInvariant();
                var key = $"GPU-{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
                devices.Add(new(key, text.ToString(), ordinal, ordinal.ToString()));
                if ((engine != InferenceDeviceType.TensorRT && major >= 5) || (engine == InferenceDeviceType.TensorRT && major * 10 + minor >= 75))
                    compatible.Add(key);
            }
            // cuInit 已按 CUDA_VISIBLE_DEVICES 过滤并重排设备，ordinal 就是 ORT 需要的 device_id，不能再次映射。
            return devices.Where(device => compatible.Contains(device.Id)).ToArray();
        }
        finally { NativeLibrary.Free(library); }
    }

    private static IReadOnlyList<InferenceDeviceInfo> GetOpenVinoDevices(string directory)
    {
        var path = Path.Combine(directory, "openvino_c.dll");
        if (!File.Exists(path)) return [];
        var library = LoadLibraryEx(path, IntPtr.Zero, 0x00001100);
        if (library == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenVINO 设备探测库加载失败");
        IntPtr core = IntPtr.Zero;
        var available = new OpenVinoDevices();
        try
        {
            var create = Get<OvCreate>(library, "ov_core_create");
            var get = Get<OvDevices>(library, "ov_core_get_available_devices");
            var property = Get<OvProperty>(library, "ov_core_get_property");
            var free = Get<OvFree>(library, "ov_free");
            if (create(out core) != 0 || get(core, ref available) != 0)
                throw new InvalidOperationException("无法枚举 OpenVINO 设备，请检查设备驱动。");
            var devices = new List<InferenceDeviceInfo>();
            for (var i = 0; i < checked((int)available.Size); i++)
            {
                var id = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(available.Devices, i * IntPtr.Size));
                if (id == null || !(id == "CPU" || id.StartsWith("GPU", StringComparison.Ordinal) || id.StartsWith("NPU", StringComparison.Ordinal))) continue;
                string display = id;
                if (property(core, id, "FULL_DEVICE_NAME", out var value) == 0 && value != IntPtr.Zero)
                {
                    try { display = $"{id} · {Marshal.PtrToStringUTF8(value)}"; }
                    finally { free(value); }
                }
                devices.Add(new(id, display, i, id));
            }
            return devices;
        }
        finally
        {
            if (available.Devices != IntPtr.Zero) Get<OvDevicesFree>(library, "ov_available_devices_free")(ref available);
            if (core != IntPtr.Zero) Get<OvFree>(library, "ov_core_free")(core);
            NativeLibrary.Free(library);
        }
    }

    private static T Get<T>(IntPtr library, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    [StructLayout(LayoutKind.Sequential)] private struct DeviceUuid { public uint A, B, C, D; }
    [StructLayout(LayoutKind.Sequential)] private struct KernelAdapter { public long Luid; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterAddress { public uint Bus, Device, Function; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterQuery { public uint Handle; public int Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)] private struct OpenVinoDevices { public IntPtr Devices; public nuint Size; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CudaInit(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CudaCount(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CudaDevice(out int device, int ordinal);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int CudaName(StringBuilder name, int length, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CudaUuid(out DeviceUuid uuid, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CudaAttribute(out int value, int attribute, int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvCreate(out IntPtr core);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvDevices(IntPtr core, ref OpenVinoDevices devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OvDevicesFree(ref OpenVinoDevices devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvProperty(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string device, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OvFree(IntPtr value);
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int level, ref Guid iid, out IntPtr device);
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref KernelAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref AdapterQuery query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint handle);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
}
