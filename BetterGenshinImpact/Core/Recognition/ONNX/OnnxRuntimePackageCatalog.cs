using System;
using System.Collections.Generic;
using System.IO;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public sealed record RuntimeInstallation(InferenceInstallationState State, string Directory, string Description);
public sealed record RuntimeDownloadPackage(string Name, Uri Url, long DownloadBytes, long InstalledBytes,
    string? Hash, string HashAlgorithm, string? NugetId = null);
public sealed record RuntimeDownloadProgress(string Message, long Received, long Total)
{
    public double Percentage => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
}

/// <summary>固定的运行库版本与依赖清单，CUDA/TRT 目录、安装状态和搜索路径完全独立。</summary>
public static class OnnxRuntimePackageCatalog
{
    public const string TensorRtUrl = "https://developer.download.nvidia.com/compute/machine-learning/tensorrt/10.8.0/zip/TensorRT-10.8.0.43.Windows.win10.cuda-12.8.zip";

    public static string EngineDirectory(string pluginsDirectory, InferenceDeviceType engine) => Path.Combine(pluginsDirectory, engine.ToString());

    public static string RuntimeDirectory(string pluginsDirectory, InferenceDeviceType engine)
    {
        var root = EngineDirectory(pluginsDirectory, engine);
        var pointer = Path.Combine(root, "current.txt");
        if (File.Exists(pointer) && Guid.TryParseExact(File.ReadAllText(pointer).Trim(), "N", out var revision))
        {
            var directory = Path.Combine(root, "versions", revision.ToString("N"));
            if (Directory.Exists(directory)) return directory;
        }
        return root;
    }

    public static string[] ProviderFiles(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.CUDA => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_cuda.dll"],
        InferenceDeviceType.TensorRT => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_tensorrt.dll"],
        InferenceDeviceType.OpenVINO => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_openvino.dll", "openvino.dll", "openvino_c.dll", "tbb12.dll", "openvino_intel_cpu_plugin.dll"],
        InferenceDeviceType.DirectML => ["onnxruntime.dll", "DirectML.dll"],
        _ => []
    };

    public static string[] DependencyFiles(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.CUDA => ["cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "curand64_10.dll", "cudnn64_9.dll", "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_adv64_9.dll", "cudnn_graph64_9.dll", "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll", "nvrtc64_120_0.dll", "nvrtc-builtins64_128.dll"],
        // TensorRT 10.8 自带计算与构建库，外部依赖只补 CUDA runtime，不安装 CUDA provider、cuDNN 或无关数学库。
        InferenceDeviceType.TensorRT => ["cudart64_12.dll", "nvinfer_10.dll", "nvinfer_plugin_10.dll", "nvonnxparser_10.dll", "nvinfer_builder_resource_10.dll"],
        _ => []
    };

    public static RuntimeInstallation Inspect(string baseDirectory, InferenceDeviceType engine)
    {
        if (engine == InferenceDeviceType.Cpu) return new(InferenceInstallationState.Installed, baseDirectory, "内置，无需下载");
        var plugins = Path.Combine(baseDirectory, "Plugins", "OnnxRuntime");
        var directory = RuntimeDirectory(plugins, engine);
        if (engine == InferenceDeviceType.DirectML && !File.Exists(Path.Combine(directory, "onnxruntime.dll"))) directory = baseDirectory;
        foreach (var file in ProviderFiles(engine))
            if (!File.Exists(Path.Combine(directory, file))) return new(InferenceInstallationState.Missing, directory, $"DLL 未安装：缺少 {file}");
        var missing = Array.FindAll(DependencyFiles(engine), file => !File.Exists(Path.Combine(directory, file)));
        return missing.Length == 0
            ? new(InferenceInstallationState.Installed, directory, "DLL 已安装，需通过模型验证确认可用")
            : new(InferenceInstallationState.MissingDependencies, directory, $"依赖不完整：{string.Join("、", missing)}");
    }
}
