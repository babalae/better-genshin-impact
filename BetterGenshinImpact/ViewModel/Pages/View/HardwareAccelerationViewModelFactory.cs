using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>每次打开设置页创建独立草稿与取消源，依赖由容器注入，窗口关闭后释放。</summary>
public sealed class HardwareAccelerationViewModelFactory(
    InferenceRuntimeService runtime, IOnnxRuntimeInstaller installer, IInferenceDeviceDiscovery devices,
    IInferenceModelDiagnostics diagnostics, BgiOnnxFactory factory, IConfigService configService,
    IInferenceBenchmarkLeaseProvider leases, IInferenceSettingsInteraction interaction)
{
    public HardwareAccelerationViewModel Create() => new(runtime, installer, devices, diagnostics, factory, configService, leases, interaction);
}
