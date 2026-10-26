using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>用户在设置页选择的全局推理后端；实际生效由两个用途开关决定，以便保留单项覆盖能力。</summary>
public enum InferenceBackend
{
    Onnx = 0,
    Ncnn = 1
}

/// <summary>ncnn 的计算后端：Vulkan 显卡加速或 ncnn 原生 CPU 推理。</summary>
public enum NcnnComputeMode
{
    Vulkan = 0,
    Cpu = 1
}

/// <summary>
///     ncnn 后端的生效参数快照，供设置页草稿比较与装配时透传。
///     <see cref="Backend" /> 是全局选择意图，<see cref="UseOcr" /> / <see cref="UseYolo" /> 是逐用途覆盖后的结果：
///     同一组开关在不同全局意图下含义不同（ncnn 全局 + OCR 回退 / ONNX 全局 + YOLO 走 ncnn），因此必须显式携带。
///     <see cref="Det" /> / <see cref="Rec" /> / <see cref="Yolo" /> 是三套完全独立的调优参数。
/// </summary>
public sealed record NcnnBackendSettings(
    InferenceBackend Backend, bool UseOcr, bool UseYolo,
    NcnnComputeMode ComputeMode, int GpuIndex,
    NcnnTuningSettings Det, NcnnTuningSettings Rec, NcnnTuningSettings Yolo)
{
    public const int DefaultThreads = NcnnTuningSettings.DefaultThreads;
    public const int MaxThreads = NcnnTuningSettings.MaxThreads;
    public const int MaxParallelSessions = NcnnTuningSettings.MaxParallelSessions;
    public const int MaxGpuIndex = 7;

    public static NcnnBackendSettings Default { get; } = new(InferenceBackend.Onnx, false, false,
        NcnnComputeMode.Vulkan, 0,
        NcnnTuningSettings.CreateDefault(), NcnnTuningSettings.CreateDefault(), NcnnTuningSettings.CreateDefault());

    /// <summary>检测用途的线程数（摘要与日志使用的代表值）。</summary>
    public int Threads => Det.Threads;

    public bool IsValid => GpuIndex is >= 0 and <= MaxGpuIndex
        && Det.IsThreadsValid() && Rec.IsThreadsValid() && Yolo.IsThreadsValid();

    /// <summary>CPU 模式下传给 shim 的 gpuIndex：shim 以负值关闭 Vulkan 计算。</summary>
    public int EffectiveGpuIndex => ComputeMode == NcnnComputeMode.Cpu ? -1 : GpuIndex;

    /// <summary>只比较是否变化用的稳定签名，不参与验证指纹。</summary>
    public string SignatureSuffix =>
        $"|ncnn|{Backend}|{UseOcr}|{UseYolo}|{ComputeMode}|{GpuIndex}|det{Det.SignatureSuffix}|rec{Rec.SignatureSuffix}|yolo{Yolo.SignatureSuffix}";

    /// <summary>返回深拷贝并修正越界值，避免草稿对象与已保存配置共享引用。</summary>
    public NcnnBackendSettings Normalize() => new(Backend, UseOcr, UseYolo, ComputeMode,
        Math.Clamp(GpuIndex, 0, MaxGpuIndex),
        Det.NormalizedCopy(), Rec.NormalizedCopy(), Yolo.NormalizedCopy());

    public static NcnnBackendSettings FromConfig(NcnnConfig config) => new NcnnBackendSettings(
        config.PreferredBackend, config.UseOcr, config.UseYolo,
        config.ComputeMode, config.GpuIndex,
        config.DetTuning ?? NcnnTuningSettings.CreateDefault(),
        config.RecTuning ?? NcnnTuningSettings.CreateDefault(),
        config.YoloTuning ?? NcnnTuningSettings.CreateDefault()).Normalize();

    public bool Equals(NcnnBackendSettings? other) => other != null && SignatureSuffix == other.SignatureSuffix;

    public override int GetHashCode() => SignatureSuffix.GetHashCode(StringComparison.Ordinal);
}

internal static class NcnnTuningValidationExtensions
{
    public static bool IsThreadsValid(this NcnnTuningSettings tuning)
        => tuning.Threads is >= 1 and <= NcnnTuningSettings.MaxThreads;
}
