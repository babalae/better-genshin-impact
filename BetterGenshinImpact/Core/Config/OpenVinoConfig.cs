using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     OpenVINO（C# API）后端的独立持久化配置：逐用途开关、设备与编译缓存。
///     <para>
///         独立持有而不复用旧的引擎设置（<c>HardwareAccelerationConfig.OpenVINO</c>）：
///         旧字段属于 ONNX Runtime 执行提供程序时代，保留只为配置回写兼容；本类是独立后端的参数来源。
///     </para>
/// </summary>
public partial class OpenVinoConfig : ObservableObject
{
    /// <summary>OCR 是否走 OpenVINO 后端。开启后 Det/Rec 直接推理现有 ONNX 模型文件，无需转换产物。</summary>
    [ObservableProperty]
    private bool _useOcr;

    /// <summary>YOLO 检测 / 分类是否走 OpenVINO 后端。开启后所有已登记模型直接推理现有 ONNX 文件。</summary>
    [ObservableProperty]
    private bool _useYolo;

    /// <summary>OpenVINO 设备名（CPU / GPU.0 / NPU…），来自设置页的设备发现。</summary>
    [ObservableProperty]
    private string _deviceId = "CPU";

    /// <summary>启用模型编译缓存（CACHE_DIR）。首次编译较慢的 GPU / NPU 设备建议开启。</summary>
    [ObservableProperty]
    private bool _enableModelCache;

    /// <summary>
    ///     NPU 识别（Rec）模型的固定输入宽度。NPU 编译器要求静态形状：
    ///     文本行等比缩放到模型高度后补零边到该宽度，超宽文本会被压缩到固定宽度（可能识别不全，调大可缓解）。
    ///     仅在设备为 NPU 时生效；CPU 保持动态宽度，GPU 走宽度桶（见 <see cref="GpuRecWidthBuckets" />）。
    /// </summary>
    [ObservableProperty]
    private int _npuRecWidth = OpenVinoBackendSettings.DefaultNpuRecWidth;

    /// <summary>
    ///     设备为 NPU 时检测（Det）模型的运行设备。Det 改为静态画布后可留在 NPU（出厂值）；
    ///     长截图担心小字召回下降时，可改为回落到 CPU（动态形状）或其它设备。
    /// </summary>
    [ObservableProperty]
    private string _npuDetDevice = OpenVinoBackendSettings.DefaultNpuDetDevice;

    /// <summary>
    ///     Det 静态画布边长（正方形边长）。GPU/NPU 上 Det 等比缩放长边到该值并右下补零，
    ///     消除动态形状的逐尺寸编译卡顿；0 表示强制动态。CPU 路线恒动态，不受此值影响。
    ///     BetterGI 截屏区域常大于 640，担心小字召回时可调到 960。
    /// </summary>
    [ObservableProperty]
    private int _detStaticSide = OpenVinoBackendSettings.DefaultDetStaticSide;

    /// <summary>
    ///     GPU 识别（Rec）模型的静态宽度桶（逗号分隔，如 "320,640,960"）。
    ///     GPU 动态形状是预览特性，每个新宽度首次推理触发秒级 kernel 编译；
    ///     按文本行宽度选最小够用的桶补零推理，一次编译永久复用，超宽文本压缩到最大桶。
    ///     空串表示保持动态宽度。仅在设备为 GPU 时生效。
    /// </summary>
    [ObservableProperty]
    private string _gpuRecWidthBuckets = OpenVinoBackendSettings.DefaultGpuRecWidthBuckets;

    partial void OnDeviceIdChanged(string value)
    {
        // 脚本或其他调用方写入空设备名时回落 CPU，与配置读取的兜底一致。
        if (string.IsNullOrWhiteSpace(value)) DeviceId = "CPU";
    }

    partial void OnNpuDetDeviceChanged(string value)
    {
        // 与 DeviceId 相同的空值兜底，避免脚本写入空串导致装配失败。
        if (string.IsNullOrWhiteSpace(value)) NpuDetDevice = OpenVinoBackendSettings.DefaultNpuDetDevice;
    }

    /// <summary>把设置页草稿（写入时 Normalize 成深拷贝）落到持久化对象；草稿后续修改不影响已保存配置。</summary>
    public void ApplySettings(OpenVinoBackendSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        UseOcr = normalized.UseOcr;
        UseYolo = normalized.UseYolo;
        DeviceId = normalized.DeviceId;
        EnableModelCache = normalized.EnableModelCache;
        NpuRecWidth = normalized.NpuRecWidth;
        NpuDetDevice = normalized.NpuDetDevice;
        GpuRecWidthBuckets = normalized.GpuRecWidthBuckets;
        DetStaticSide = normalized.DetStaticSide;
    }
}
