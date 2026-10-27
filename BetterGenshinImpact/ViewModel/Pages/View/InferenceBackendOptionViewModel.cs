using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>顶部「引擎分配」二选一卡片：ncnn 与 ONNX Runtime 同级展示。</summary>
public partial class InferenceBackendOptionViewModel(InferenceBackend backend) : ObservableObject
{
    public InferenceBackend Backend { get; } = backend;

    // 引擎显示名统一为「ONNX Runtime」与「ncnn」（全小写），Vulkan / CPU 属于 ncnn 的计算模式，不进入引擎名。
    public string Name => Backend == InferenceBackend.Ncnn ? "ncnn" : "ONNX Runtime";
    public string Description => Backend == InferenceBackend.Ncnn
        ? "可用 Vulkan 显卡或原生 CPU 执行转换后的模型，速度稳定，不受 ONNX Runtime 调度影响。"
        : "按设备选择 CPU、DirectML、OpenVINO 或 CUDA，兼容性最好，可调整线程与缓存。";
    public string Tagline => Backend == InferenceBackend.Ncnn ? "计算模式：Vulkan 显卡 / CPU" : "按显卡/处理器挑选执行设备";

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusText = "";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
}
