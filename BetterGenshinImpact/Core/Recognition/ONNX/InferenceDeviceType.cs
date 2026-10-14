// ReSharper disable InconsistentNaming
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
    TensorRT = 5
}
