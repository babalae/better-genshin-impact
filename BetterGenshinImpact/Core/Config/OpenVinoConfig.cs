using System;
using System.Text.Json.Serialization;
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

    partial void OnDeviceIdChanged(string value)
    {
        // 脚本或其他调用方写入空设备名时回落 CPU，与配置读取的兜底一致。
        if (string.IsNullOrWhiteSpace(value)) DeviceId = "CPU";
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
    }

    /// <summary>只比较是否变化用的稳定签名（设备与开关；缓存开关参与设置变更判定）。</summary>
    [JsonIgnore]
    public string SignatureSuffix => OpenVinoBackendSettings.FromConfig(this).SignatureSuffix;
}
