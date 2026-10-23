using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>顶部推理后端二选一卡片：ncnn Vulkan 与 ONNX Runtime 同级展示。</summary>
public partial class InferenceBackendOptionViewModel(InferenceBackend backend) : ObservableObject
{
    public InferenceBackend Backend { get; } = backend;
    public string Name => Backend == InferenceBackend.Ncnn ? "ncnn Vulkan" : "ONNX Runtime";
    public string Description => Backend == InferenceBackend.Ncnn
        ? "可用 Vulkan 显卡或原生 CPU 执行转换后的模型，速度稳定，不受 ONNX 运行时调度影响。"
        : "按设备选择 CPU、DirectML、OpenVINO 或 CUDA，兼容性最好，可调整线程与缓存。";
    public string Tagline => Backend == InferenceBackend.Ncnn ? "Vulkan 显卡 / CPU 可选" : "按显卡/处理器挑选执行设备";

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isBlocked;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _statusText = "";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
}
