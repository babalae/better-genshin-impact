using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>用户在设置页选择的全局推理后端；实际生效由各后端的逐用途开关决定，以便保留单项覆盖能力。</summary>
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
///     <see cref="UseOcr" /> / <see cref="UseYolo" /> 是逐用途开关，
///     <see cref="Det" /> / <see cref="Rec" /> / <see cref="Yolo" /> 是三套完全独立的调优参数。
///     全局后端意图由 <see cref="HardwareAccelerationConfig.PreferredBackend" /> 承载，不再随 ncnn 参数携带。
/// </summary>
public sealed record NcnnBackendSettings(
    bool UseOcr, bool UseYolo,
    NcnnComputeMode ComputeMode, int GpuIndex,
    NcnnTuningSettings Det, NcnnTuningSettings Rec, NcnnTuningSettings Yolo)
{
    public const int MaxGpuIndex = 7;

    public bool IsValid => GpuIndex is >= 0 and <= MaxGpuIndex
        && Det.IsThreadsValid() && Det.IsGpuThreadsValid()
        && Rec.IsThreadsValid() && Rec.IsGpuThreadsValid()
        && Yolo.IsThreadsValid() && Yolo.IsGpuThreadsValid();

    /// <summary>CPU 模式下传给 shim 的 gpuIndex：shim 以负值关闭 Vulkan 计算。</summary>
    public int EffectiveGpuIndex => ComputeMode == NcnnComputeMode.Cpu ? -1 : GpuIndex;

    /// <summary>只比较是否变化用的稳定签名，不参与验证指纹。</summary>
    public string SignatureSuffix =>
        $"|ncnn|{UseOcr}|{UseYolo}|{ComputeMode}|{GpuIndex}|det{Det.SignatureSuffix}|rec{Rec.SignatureSuffix}|yolo{Yolo.SignatureSuffix}";

    /// <summary>返回深拷贝并修正越界值，避免草稿对象与已保存配置共享引用。</summary>
    public NcnnBackendSettings Normalize() => new(UseOcr, UseYolo, ComputeMode,
        Math.Clamp(GpuIndex, 0, MaxGpuIndex),
        Det.NormalizedCopy(), Rec.NormalizedCopy(), Yolo.NormalizedCopy());

    public static NcnnBackendSettings FromConfig(NcnnConfig config) => new NcnnBackendSettings(
        config.UseOcr, config.UseYolo,
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

    public static bool IsGpuThreadsValid(this NcnnTuningSettings tuning)
        => tuning.GpuThreads is >= 1 and <= NcnnTuningSettings.MaxThreads;
}
