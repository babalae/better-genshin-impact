using System;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

internal static class EPTensorRt
{
    private const int CudaSuccess = 0;

    public static string TrtCacheDirectoryGet(int deviceId)
    {
        var CUDAMajor = CUDADeviceAttributeGet(CudaDeviceAttribute.ComputeCapabilityMajor, deviceId);
        var CUDAMinor = CUDADeviceAttributeGet(CudaDeviceAttribute.ComputeCapabilityMinor, deviceId);

        return $"onnx-{OrtEnv.Instance().GetVersionString()}_trt-{TensorRTVersionGet()}_cuda-sm{CUDAMajor}{CUDAMinor}";
    }

    private static int CUDADeviceAttributeGet(CudaDeviceAttribute attribute, int deviceId)
    {
        var result = CUDADeviceAttributeGet(out var value, attribute, deviceId);
        if (result != CudaSuccess)
        {
            throw new InvalidOperationException($"[ONNX] [TensorRT] 无法读取 CUDA 设备 {deviceId} 属性 {attribute} ，错误代码 {result}");
        }

        return value;
    }

    [DllImport("nvinfer_10.dll", EntryPoint = "getInferLibVersion", CallingConvention = CallingConvention.Cdecl)]
    private static extern int TensorRTVersionGet();

    [DllImport("cudart64_12.dll", EntryPoint = "cudaDeviceGetAttribute", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CUDADeviceAttributeGet(out int value, CudaDeviceAttribute attribute, int deviceId);

    private enum CudaDeviceAttribute
    {
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }
}
