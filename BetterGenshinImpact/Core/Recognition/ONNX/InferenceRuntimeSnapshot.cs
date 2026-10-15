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

/// <summary>每个会话创建时冻结。设置页草稿及延后验证不能改变现有推理会话的参数。</summary>
public sealed record InferenceRuntimeSnapshot(
    InferenceDeviceType Engine, InferenceDeviceInfo Device, bool CpuOcr,
    int CpuThreads, bool EnableModelCache, string RuntimeDirectory)
{
    public PaddleOcrCpuOptions PaddleOcrCpu { get; init; } = new();
    public static string ModelCacheDirectory => Global.Absolute(Path.Combine("Cache", "Inference"));
    public string Description => $"{(Engine == InferenceDeviceType.Cpu ? "CPU" : Engine.ToString())} · {Device.Name}";
    public string OcrDescription => (CpuOcr ? "CPU（强制 OCR 使用 CPU）" : Description)
        + (CpuOcr || Engine == InferenceDeviceType.Cpu ? " · " + PaddleOcrCpu.Describe(CpuThreads) : "");
    public string Signature => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Engine}|{Device.Id}|{CpuOcr}|{CpuThreads}|{EnableModelCache}|{OnnxRuntimeLoader.RequiredVersion}{PaddleOcrCpu.SignatureSuffix}")));
    private string CacheSignature => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"engine-cache-v1|{Engine}|{Device.Id}|{OnnxRuntimeLoader.RequiredVersion}|CUDA12.8|cuDNN9.8|TensorRT10.8")));

    public string GetCachePath(BgiOnnxModel model)
        => GetCachePathForModel(model.ModalPath, ModelCacheDirectory);

    public string GetCachePathForModel(string modelPath, string cacheDirectory)
    {
        // 普通 engine/timing 缓存不再引用 _ctx.onnx；模型变化或设备变化不能复用旧缓存。
        using var stream = File.OpenRead(modelPath);
        var modelHash = Convert.ToHexString(SHA256.HashData(stream));
        // 合并并缩短目录指纹，避免 Windows 下引擎自身的长文件名叠加超长路径。
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CacheSignature + modelHash)))[..16];
        return Path.Combine(cacheDirectory, Engine.ToString(), key);
    }

    public static InferenceRuntimeSnapshot Create(HardwareAccelerationConfig config,
        InferenceDeviceInfo device, string runtimeDirectory) => new(config.InferenceDevice, device,
        config.CpuOcr, config.Cpu.CpuThreads, config.InferenceDevice is InferenceDeviceType.OpenVINO or InferenceDeviceType.TensorRT
            && config.GetEngineSettings(config.InferenceDevice).EnableModelCache,
        runtimeDirectory) { PaddleOcrCpu = config.PaddleOcrCpu.ToOptions() };

    public static InferenceRuntimeSnapshot CpuDefault(int threads = 0) => new(InferenceDeviceType.Cpu,
        new InferenceDeviceInfo("CPU", "处理器", 0, "CPU"), true, threads, false, "");
}

public static class InferenceTestImages
{
    // 使用秘境中的石化古树实机截图，供运行库验证与设置页测速共用。
    public const string DomainTreeRelativePath = @"Assets\Model\Domain\test_petrified_trees.jpg";
    public static string DomainTreePath => Global.Absolute(DomainTreeRelativePath);
    public static bool HasDomainTreeImage => File.Exists(DomainTreePath);
}
