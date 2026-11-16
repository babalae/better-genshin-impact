using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO 设备枚举：通过 openvino_c.dll 的 C API 直接查询（加载即用即卸，不污染进程状态）。
///     从旧 OpenVINO 执行提供程序的设备探测移植，运行库目录改为独立后端的 Plugins/OpenVINO。
/// </summary>
internal static class OpenVinoDeviceDiscovery
{
    public static IReadOnlyList<OpenVinoDeviceInfo> GetDevices(string directory)
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
            var devices = new List<OpenVinoDeviceInfo>();
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
                devices.Add(new(id, display));
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

    private static T Get<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [StructLayout(LayoutKind.Sequential)] private struct OpenVinoDevices { public IntPtr Devices; public nuint Size; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvCreate(out IntPtr core);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvDevices(IntPtr core, ref OpenVinoDevices devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OvDevicesFree(ref OpenVinoDevices devices);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OvProperty(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string device, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OvFree(IntPtr value);
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
}
