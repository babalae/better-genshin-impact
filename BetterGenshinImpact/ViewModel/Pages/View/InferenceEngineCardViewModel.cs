using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.ONNX;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class InferenceEngineCardViewModel(InferenceDeviceType engine, InferenceEngineSettings settings) : ObservableObject
{
    public InferenceDeviceType Engine { get; } = engine;
    public string Name => Engine == InferenceDeviceType.Cpu ? "CPU" : Engine.ToString();
    public string Description => Engine switch
    {
        InferenceDeviceType.Cpu => "内置处理器推理，兼容性最好。",
        InferenceDeviceType.DirectML => "使用支持 DirectML 的显卡，无需额外计算库。",
        InferenceDeviceType.OpenVINO => "使用 Intel CPU、显卡或 NPU。",
        InferenceDeviceType.CUDA => "使用 NVIDIA 显卡执行推理。",
        _ => "使用 NVIDIA TensorRT 优化推理，首次构建模型可能较慢。"
    };
    public InferenceEngineSettings Settings { get; } = settings.Copy();
    [ObservableProperty] private ObservableCollection<InferenceDeviceInfo> _devices = [];
    public bool SupportsCache => Engine is InferenceDeviceType.OpenVINO or InferenceDeviceType.TensorRT;
    public bool IsCpu => Engine == InferenceDeviceType.Cpu;
    public bool IsRecommended => Engine == InferenceDeviceType.OpenVINO;
    [ObservableProperty] private InferenceDeviceInfo? _selectedDevice;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _hasFailure;
    [ObservableProperty] private string _statusText = "正在检查…";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetails))] private string _details = "";
    [ObservableProperty] private string _deviceMessage = "";
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _canCompleteDependencies;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _runtimeDirectory = "";
    [ObservableProperty] private string _applyBlockReason = "正在检查运行库与设备…";
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    partial void OnSelectedDeviceChanged(InferenceDeviceInfo? value) => Settings.DeviceId = value?.Id ?? "";

    public void UpdateDevices(IReadOnlyList<InferenceDeviceInfo> devices, string preferredId)
    {
        // 整体替换列表后最后恢复选择，避免 ComboBox 清空旧列表时把保存的设备编号覆盖为空。
        var supported = devices.DistinctBy(device => device.Id).ToArray();
        Devices = new ObservableCollection<InferenceDeviceInfo>(supported);
        SelectedDevice = supported.FirstOrDefault(device => device.Id == preferredId) ?? supported.FirstOrDefault();
    }
}
