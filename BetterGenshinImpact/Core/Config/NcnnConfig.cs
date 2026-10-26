using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     ncnn 后端的独立持久化配置：全局后端意图、逐用途开关、计算模式与三套调优参数。
///     <para>
///         独立持有而不平铺进 <see cref="HardwareAccelerationConfig" />：
///         ONNX 侧配置结构损坏被整体重置、或换用逻辑核数较少的处理器触发回退时，这里的参数不受波及。
///     </para>
/// </summary>
public partial class NcnnConfig : ObservableObject
{
    /// <summary>
    ///     全局推理后端选择意图。界面「引擎分配」二选一写入这里；实际生效仍以
    ///     <see cref="UseOcr" /> / <see cref="UseYolo" /> 为准，便于单项覆盖。
    /// </summary>
    [ObservableProperty]
    private InferenceBackend _preferredBackend = InferenceBackend.Onnx;

    /// <summary>
    ///     OCR 是否走 ncnn 后端。全局选择 ncnn 时默认开启，可在设置区单独覆盖回 ONNX。
    ///     开启后 Det/Rec 全部走 ncnn，需资产目录内提供 ncnn_shim.dll 与 det_dyn/rec_dyn 动态模型；
    ///     运行库或产物不可用时直接报错并阻止启动，不再静默回退所选推理引擎。
    /// </summary>
    [ObservableProperty]
    private bool _useOcr;

    /// <summary>
    ///     YOLO 检测是否走 ncnn 后端。全局选择 ncnn 时默认开启，可在设置区单独覆盖回 ONNX。
    ///     开启后所有检测模型都必须有同名 ncnn 产物；缺失或创建失败时直接报错，
    ///     需要自定义模型时请关闭该开关。
    /// </summary>
    [ObservableProperty]
    private bool _useYolo;

    /// <summary>ncnn 的计算后端：Vulkan 显卡加速或 ncnn 原生 CPU 推理。</summary>
    [ObservableProperty]
    private NcnnComputeMode _computeMode = NcnnComputeMode.Vulkan;

    /// <summary>ncnn 使用的 Vulkan 设备索引。0 表示首个 Vulkan 设备（通常是独显）。</summary>
    [ObservableProperty]
    private int _gpuIndex;

    /// <summary>OCR 文本检测的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _detTuning = NcnnTuningSettings.CreateDefault();

    /// <summary>OCR 文字识别的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _recTuning = NcnnTuningSettings.CreateDefault();

    /// <summary>YOLO 检测 / 分类的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _yoloTuning = NcnnTuningSettings.CreateDefault();

    partial void OnPreferredBackendChanged(InferenceBackend value)
    {
        // 脚本或其他调用方写入未知值时回落 ONNX，与配置读取的兜底一致。
        if (!Enum.IsDefined(value)) PreferredBackend = InferenceBackend.Onnx;
    }

    partial void OnComputeModeChanged(NcnnComputeMode value)
    {
        if (!Enum.IsDefined(value)) ComputeMode = NcnnComputeMode.Vulkan;
    }

    /// <summary>把设置页草稿（写入时 Normalize 成深拷贝）落到持久化对象；草稿后续修改不影响已保存配置。</summary>
    public void ApplySettings(NcnnBackendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        PreferredBackend = normalized.Backend;
        UseOcr = normalized.UseOcr;
        UseYolo = normalized.UseYolo;
        ComputeMode = normalized.ComputeMode;
        GpuIndex = normalized.GpuIndex;
        DetTuning = normalized.Det;
        RecTuning = normalized.Rec;
        YoloTuning = normalized.Yolo;
    }

    /// <summary>恢复出厂值。「恢复默认 CPU」与整体配置重置以外的路径不得调用。</summary>
    public void ResetToDefaults()
    {
        PreferredBackend = InferenceBackend.Onnx;
        UseOcr = false;
        UseYolo = false;
        ComputeMode = NcnnComputeMode.Vulkan;
        GpuIndex = 0;
        DetTuning = NcnnTuningSettings.CreateDefault();
        RecTuning = NcnnTuningSettings.CreateDefault();
        YoloTuning = NcnnTuningSettings.CreateDefault();
    }
}
