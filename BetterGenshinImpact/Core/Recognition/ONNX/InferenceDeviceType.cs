namespace BetterGenshinImpact.Core.Recognition.ONNX;

public enum InferenceDeviceType
{
    Cpu = 0,
    GpuDirectMl = 1,

    /// <summary>
    /// 旧版的“自动 GPU”配置值。只用于读取历史配置，不再显示给用户。
    /// </summary>
    LegacyAutoGpu = 2,

    OpenVino = 3,
    Cuda = 4
}
