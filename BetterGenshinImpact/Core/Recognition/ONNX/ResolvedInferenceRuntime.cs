using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public enum InferenceValidationState { None, PendingRestart, Validating }
public enum InferenceInstallationState { Missing, MissingDependencies, Installed }
public enum InferenceBenchmarkScene { Ocr, DomainTree }

public sealed record InferenceDeviceInfo(string Id, string Name, int Index, string ProviderDevice);

/// <summary>已经解析设备与运行库的不可变运行时。每个会话创建时绑定一个实例，后续设置变化不会影响已有会话。</summary>
public sealed record ResolvedInferenceRuntime(
    InferenceDeviceType Engine, InferenceDeviceInfo Device, bool CpuOcr,
    int CpuThreads, bool EnableModelCache, string RuntimeDirectory)
{
    public PaddleOcrCpuOptions PaddleOcrCpu { get; init; } = new();
    public static InferenceDeviceInfo CpuDevice { get; } = new("CPU", "处理器", 0, "CPU");
    public static string ModelCacheDirectory => Global.Absolute(Path.Combine("Cache", "Inference"));
    public string Description => $"{(Engine == InferenceDeviceType.Cpu ? "CPU" : Engine.ToString())} · {Device.Name}";
    public string OcrDescription => (CpuOcr ? "CPU（强制 OCR 使用 CPU）" : Description)
        + (CpuOcr || Engine == InferenceDeviceType.Cpu ? " · " + PaddleOcrCpu.Describe(CpuThreads) : "");
    public string Fingerprint => InferenceRuntimeFingerprint.Compute(Engine, Device.Id, CpuOcr, CpuThreads,
        EnableModelCache, PaddleOcrCpu);

    private string CacheFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"engine-cache-v1|{Engine}|{Device.Id}|{OnnxRuntimeLoader.RequiredVersion}|CUDA12.8|cuDNN9.8|TensorRT10.8")));

    public string GetCachePath(BgiOnnxModel model)
        => GetCachePathForModel(model.ModalPath, ModelCacheDirectory);

    public string GetCachePathForModel(string modelPath, string cacheDirectory)
    {
        // 普通 engine/timing 缓存不再引用 _ctx.onnx；模型变化或设备变化不能复用旧缓存。
        using var stream = File.OpenRead(modelPath);
        var modelHash = Convert.ToHexString(SHA256.HashData(stream));
        // 合并并缩短目录指纹，避免 Windows 下引擎自身的长文件名叠加超长路径。
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CacheFingerprint + modelHash)))[..16];
        return Path.Combine(cacheDirectory, Engine.ToString(), key);
    }

    public static ResolvedInferenceRuntime Resolve(HardwareAccelerationConfig config,
        InferenceDeviceInfo device, string runtimeDirectory)
    {
        var engine = config.InferenceDevice;
        return new(engine, device, config.CpuOcr, config.Cpu.CpuThreads,
            engine is InferenceDeviceType.OpenVINO or InferenceDeviceType.TensorRT
            && config.GetEngineSettings(engine).EnableModelCache,
            runtimeDirectory) { PaddleOcrCpu = config.PaddleOcrCpu.ToOptions() };
    }

    public static ResolvedInferenceRuntime Cpu(int threads = 0, PaddleOcrCpuOptions? paddleOcrCpu = null,
        string runtimeDirectory = "") => new(InferenceDeviceType.Cpu, CpuDevice, true, threads, false,
        runtimeDirectory) { PaddleOcrCpu = paddleOcrCpu ?? new() };
}

/// <summary>只描述会影响验证结果的配置指纹，不要求构造完整、已解析的运行时。</summary>
public static class InferenceRuntimeFingerprint
{
    public static string Compute(HardwareAccelerationConfig config, InferenceDeviceType engine)
    {
        var settings = config.GetEngineSettings(engine);
        var enableModelCache = engine is InferenceDeviceType.OpenVINO or InferenceDeviceType.TensorRT
                               && settings.EnableModelCache;
        return Compute(engine, settings.DeviceId, config.CpuOcr, config.Cpu.CpuThreads, enableModelCache,
            config.PaddleOcrCpu.ToOptions());
    }

    public static string Compute(InferenceDeviceType engine, string deviceId, bool cpuOcr, int cpuThreads,
        bool enableModelCache, PaddleOcrCpuOptions paddleOcrCpu)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{engine}|{deviceId}|{cpuOcr}|{cpuThreads}|{enableModelCache}|{OnnxRuntimeLoader.RequiredVersion}{paddleOcrCpu.SignatureSuffix}")));
}

public static class InferenceTestImages
{
    // 使用秘境中的石化古树实机截图，供运行库验证与设置页测速共用。
    public const string DomainTreeRelativePath = @"Assets\Model\Domain\test_petrified_trees.jpg";
    public static string DomainTreePath => Global.Absolute(DomainTreeRelativePath);
    public static bool HasDomainTreeImage => File.Exists(DomainTreePath);
}
