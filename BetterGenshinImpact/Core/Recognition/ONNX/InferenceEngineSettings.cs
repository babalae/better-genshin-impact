using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>每个引擎独立保存的用户设置，原生 provider 的参数由工厂统一生成。</summary>
public partial class InferenceEngineSettings : ObservableObject
{
    [ObservableProperty] private string _deviceId = "";
    [ObservableProperty] private int _cpuThreads;
    [ObservableProperty] private bool _enableModelCache = true;

    public InferenceEngineSettings Copy() => new()
    {
        DeviceId = DeviceId, CpuThreads = CpuThreads, EnableModelCache = EnableModelCache
    };
}
