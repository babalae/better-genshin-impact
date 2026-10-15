using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public sealed record RuntimeInstallation(InferenceInstallationState State, string Directory, string Description);
public sealed record RuntimeDownloadPackage(string Name, Uri Url, long DownloadBytes, long InstalledBytes,
    string? Hash, string HashAlgorithm, string? NugetId = null);
public sealed record RuntimeDownloadProgress(string Message, long Received, long Total)
{
    public double Percentage => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
}

/// <summary>固定的运行库依赖清单，CUDA/TRT 目录、安装状态和搜索路径完全独立。</summary>
public static class OnnxRuntimePackageCatalog
{
    public const string TensorRtUrl = "https://developer.download.nvidia.com/compute/machine-learning/tensorrt/10.8.0/zip/TensorRT-10.8.0.43.Windows.win10.cuda-12.8.zip";

    public static string EngineDirectory(string pluginsDirectory, InferenceDeviceType engine) => Path.Combine(pluginsDirectory, engine.ToString());

    // 每个引擎只使用一个固定目录，用户手工复制 DLL 后即可直接被识别。
    public static string RuntimeDirectory(string pluginsDirectory, InferenceDeviceType engine)
        => EngineDirectory(pluginsDirectory, engine);

    public static string[] ProviderFiles(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.CUDA => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_cuda.dll"],
        // ORT 的 TensorRT provider 通过 CUDA provider bridge 取得 GPU 基础设施；这里只部署 DLL，不会把 CUDA EP 加入会话。
        InferenceDeviceType.TensorRT => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_cuda.dll", "onnxruntime_providers_tensorrt.dll"],
        InferenceDeviceType.OpenVINO => ["onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime_providers_openvino.dll", "openvino.dll", "openvino_c.dll", "tbb12.dll", "openvino_intel_cpu_plugin.dll"],
        InferenceDeviceType.DirectML => ["onnxruntime.dll", "DirectML.dll"],
        _ => []
    };

    public static string[] DependencyFiles(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.CUDA => ["cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "curand64_10.dll", "cudnn64_9.dll", "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_adv64_9.dll", "cudnn_graph64_9.dll", "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll", "nvrtc64_120_0.dll", "nvrtc-builtins64_128.dll"],
        // TensorRT 目录独立携带 CUDA provider bridge 的全部本机依赖；执行 provider 链仍然只有 TensorRT + CPU。
        InferenceDeviceType.TensorRT => ["cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "curand64_10.dll", "cudnn64_9.dll", "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_adv64_9.dll", "cudnn_graph64_9.dll", "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll", "cudnn_engines_runtime_compiled64_9.dll", "nvrtc64_120_0.dll", "nvrtc-builtins64_128.dll", "nvinfer_10.dll", "nvinfer_plugin_10.dll", "nvonnxparser_10.dll", "nvinfer_builder_resource_10.dll"],
        _ => []
    };

    public static string[] MissingDependencyFiles(InferenceDeviceType engine, string directory)
        => Array.FindAll(DependencyFiles(engine), file => !File.Exists(Path.Combine(directory, file)));

    public static string DescribeMissingDependencies(InferenceDeviceType engine, IReadOnlyCollection<string> missing)
    {
        var components = new List<string>();
        if (missing.Any(file => file.StartsWith("cudart", StringComparison.OrdinalIgnoreCase))) components.Add("CUDA Runtime");
        if (missing.Any(file => file.StartsWith("cublas", StringComparison.OrdinalIgnoreCase))) components.Add("cuBLAS");
        if (missing.Any(file => file.StartsWith("cudnn", StringComparison.OrdinalIgnoreCase))) components.Add("cuDNN");
        if (missing.Any(file => file.StartsWith("nvrtc", StringComparison.OrdinalIgnoreCase))) components.Add("CUDA NVRTC");
        if (missing.Any(file => file.StartsWith("cufft", StringComparison.OrdinalIgnoreCase))) components.Add("cuFFT");
        if (missing.Any(file => file.StartsWith("curand", StringComparison.OrdinalIgnoreCase))) components.Add("cuRAND");
        if (missing.Any(file => file.StartsWith("nvinfer", StringComparison.OrdinalIgnoreCase)
                                || file.StartsWith("nvonnxparser", StringComparison.OrdinalIgnoreCase))) components.Add("TensorRT");
        if (components.Count == 0) components.AddRange(missing);
        return $"{engine} 缺少运行所需的 {string.Join("、", components.Distinct(StringComparer.OrdinalIgnoreCase))}。请在硬件加速设置中点击“补齐依赖”后重新应用。";
    }

    public static RuntimeInstallation Inspect(string baseDirectory, InferenceDeviceType engine)
    {
        if (engine == InferenceDeviceType.Cpu) return new(InferenceInstallationState.Installed, baseDirectory, "内置，无需下载");
        var plugins = Path.Combine(baseDirectory, "Plugins", "OnnxRuntime");
        var directory = RuntimeDirectory(plugins, engine);
        if (engine == InferenceDeviceType.DirectML && !File.Exists(Path.Combine(directory, "onnxruntime.dll"))) directory = baseDirectory;
        foreach (var file in ProviderFiles(engine))
            if (!File.Exists(Path.Combine(directory, file))) return new(InferenceInstallationState.Missing, directory, $"DLL 未安装：缺少 {file}");
        var missing = MissingDependencyFiles(engine, directory);
        return missing.Length == 0
            ? new(InferenceInstallationState.Installed, directory, "DLL 已安装，需通过模型验证确认可用")
            : new(InferenceInstallationState.MissingDependencies, directory, $"依赖不完整：{string.Join("、", missing)}");
    }
}
