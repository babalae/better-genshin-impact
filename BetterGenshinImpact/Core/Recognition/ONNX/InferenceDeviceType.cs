// ReSharper disable InconsistentNaming
using System.Linq;
using System.Text.Json.Serialization;


namespace BetterGenshinImpact.Core.Recognition.ONNX;

[JsonConverter(typeof(InferenceDeviceTypeJsonConverter))]
public enum InferenceDeviceType
{
    // 保持已有后端的配置编号，旧 Gpu 的编号 2 不再复用，读取时回退 CPU。
    Cpu = 0,
    DirectML = 1,
    OpenVINO = 3,
    CUDA = 4,
    // TensorRT 推理速度特别慢（首次还要构建引擎，等待时间更长），已从设置页与首页下线：
    // 界面不再提供该选项，历史配置在启动准备阶段自动回退 CPU。枚举编号、运行库与 provider 代码保留，便于日后恢复。
    TensorRT = 5
}

/// <summary>
/// 推理引擎在界面上的可用范围。TensorRT 首次构建引擎耗时过长，已从设置页与首页下线；
/// 枚举编号、运行库与 provider 代码保留，便于日后恢复。
/// </summary>
public static class InferenceDeviceTypeExtensions
{
    /// <summary>设置页与首页可供用户选择的推理引擎，顺序即界面顺序。</summary>
    public static readonly InferenceDeviceType[] Selectable =
        [InferenceDeviceType.Cpu, InferenceDeviceType.DirectML, InferenceDeviceType.OpenVINO, InferenceDeviceType.CUDA];

    public static bool IsSelectable(this InferenceDeviceType engine) => Selectable.Contains(engine);
}
