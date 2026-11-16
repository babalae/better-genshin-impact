using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>顶部「推理后端」三选一卡片：ONNX Runtime、OpenVINO（C# API）与 ncnn 同级展示。</summary>
public partial class InferenceBackendOptionViewModel(InferenceBackend backend) : ObservableObject
{
    public InferenceBackend Backend { get; } = backend;

    // 引擎显示名统一为「ONNX Runtime」「OpenVINO」「ncnn」（全小写）；
    // Vulkan / CPU 属于 ncnn 的计算模式，设备选择属于 OpenVINO / ONNX 各自的设置区，不进入引擎名。
    public string Name => Backend switch
    {
        InferenceBackend.Ncnn => "ncnn",
        InferenceBackend.OpenVino => "OpenVINO",
        _ => "ONNX Runtime"
    };

    public string Description => Backend switch
    {
        InferenceBackend.Ncnn => "可用 Vulkan 显卡或原生 CPU 执行转换后的模型，速度稳定，不受 ONNX Runtime 调度影响。",
        InferenceBackend.OpenVino => "使用 Intel CPU、显卡或 NPU 直接推理现有 ONNX 模型（OpenVINO C# API），不经 ONNX Runtime。",
        _ => "按设备选择 CPU、DirectML 或 CUDA，兼容性最好，可调整线程与缓存。"
    };

    public string Tagline => Backend switch
    {
        InferenceBackend.Ncnn => "计算模式：Vulkan 显卡 / CPU",
        InferenceBackend.OpenVino => "设备：Intel CPU / GPU / NPU",
        _ => "按显卡/处理器挑选执行设备"
    };

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusText = "";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
}
