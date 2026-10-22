using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn_shim.dll 的动态加载封装（NativeLibrary，dll 路径运行时可配，绕开 DllImport 常量约束）。
///     shim 导出纯 C 接口：shim_create / shim_infer / shim_destroy / shim_free_buffer，
///     内部静态链接 ncnn（Vulkan）。实验性 ncnn 后端专用。
/// </summary>
public sealed class NcnnShim : IDisposable
{
    private readonly nint _library;
    private readonly CreateDelegate _create;
    private readonly DestroyDelegate _destroy;
    private readonly InferDelegate _infer;
    private readonly FreeBufferDelegate _freeBuffer;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string paramPath,
        [MarshalAs(UnmanagedType.LPStr)] string modelPath,
        int gpuIndex, int numThreads);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InferDelegate(
        nint handle,
        [MarshalAs(UnmanagedType.LPStr)] string inputName,
        [MarshalAs(UnmanagedType.LPStr)] string outputName,
        float[] data, int w, int h, int c,
        out nint outBuffer, out int outCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeBufferDelegate(nint buffer);

    public NcnnShim(string dllPath)
    {
        _library = NativeLibrary.Load(dllPath);
        _create = GetExport<CreateDelegate>(_library, "shim_create");
        _destroy = GetExport<DestroyDelegate>(_library, "shim_destroy");
        _infer = GetExport<InferDelegate>(_library, "shim_infer");
        _freeBuffer = GetExport<FreeBufferDelegate>(_library, "shim_free_buffer");
    }

    public nint Create(string paramPath, string modelPath, int gpuIndex = 0, int numThreads = 4)
        => _create(paramPath, modelPath, gpuIndex, numThreads);

    public void Destroy(nint handle) => _destroy(handle);

    public int Infer(nint handle, string inputName, string outputName,
        float[] data, int w, int h, int c, out nint outBuffer, out int outCount)
        => _infer(handle, inputName, outputName, data, w, h, c, out outBuffer, out outCount);

    public void FreeBuffer(nint buffer) => _freeBuffer(buffer);

    private static T GetExport<T>(nint library, string name) where T : class
    {
        if (!NativeLibrary.TryGetExport(library, name, out var address))
            throw new EntryPointNotFoundException($"ncnn_shim 导出未找到: {name}");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    public void Dispose() => NativeLibrary.Free(_library);
}
