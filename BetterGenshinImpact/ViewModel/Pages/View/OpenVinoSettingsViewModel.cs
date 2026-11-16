using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenVino;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
///     「硬件加速设置 → OpenVINO」的全部设置状态：逐用途开关、设备选择、编译缓存与运行库安装。
///     OpenVINO 直接推理现有 ONNX 模型文件，无转换产物自检；运行库由 OpenVinoRuntimeInstaller 按需下载。
///     与宿主页面的关系同 ncnn 子 ViewModel：经 <see cref="IInferencePageContext" /> 只读访问页面级状态用于文案，
///     草稿经 <see cref="BuildDraft" /> 交由页面统一 Apply。
/// </summary>
public sealed partial class OpenVinoSettingsViewModel : ObservableObject
{
    private readonly IInferencePageContext? _owner;
    private OpenVinoBackendSettings _baseline;

    /// <summary>设备枚举的过期令牌：丢弃慢查询返回时的过期结果。</summary>
    private int _deviceQueryGate;

    private bool _canInteract = true;
    private bool _devicesLoaded;
    private bool _disposed;

    /// <param name="owner">宿主页面的交互面；单元测试等无页面场景可传 null，相关文案自动降级。</param>
    public OpenVinoSettingsViewModel(OpenVinoConfig saved, IInferencePageContext? owner = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _owner = owner;
        _baseline = OpenVinoBackendSettings.FromConfig(saved);
        var normalized = _baseline;
        _useOpenVinoOcr = normalized.UseOcr;
        _useOpenVinoYolo = normalized.UseYolo;
        _enableModelCache = normalized.EnableModelCache;
        _deviceId = normalized.DeviceId;
    }

    /// <summary>页面忙碌时统一禁用子控件（由主 ViewModel 单向同步）。</summary>
    public bool CanInteract
    {
        get => _canInteract;
        set => SetProperty(ref _canInteract, value);
    }

    /// <summary>任意草稿变化，页面据此刷新底部摘要与「尚未应用」。</summary>
    public event EventHandler? Changed;

    // ---------------- 逐用途开关 ----------------

    /// <summary>OCR 是否走 OpenVINO（草稿；应用后写入 OpenVinoConfig.UseOcr）。</summary>
    [ObservableProperty] private bool _useOpenVinoOcr;

    /// <summary>YOLO 是否走 OpenVINO（草稿；应用后写入 OpenVinoConfig.UseYolo）。</summary>
    [ObservableProperty] private bool _useOpenVinoYolo;

    public bool UsesOpenVino => UseOpenVinoOcr || UseOpenVinoYolo;

    /// <summary>运行库是否就绪（决定「应用并重启」是否可用）。</summary>
    public bool RuntimeReady => OpenVinoRuntime.IsInstalled();

    private bool OwnerEngineIsCpu => _owner?.SelectedEngine?.IsCpu == true;

    /// <summary>某个用途单独切回 ONNX 时的目标名称。</summary>
    public string OnnxTargetName => _owner != null && _owner.ForceCpuOcr && !OwnerEngineIsCpu
        ? "CPU（强制 OCR）"
        : _owner?.SelectedEngine?.Name ?? "ONNX Runtime";

    public string OcrOverrideHint => IsOpenVinoBackend
        ? "当前全局为 OpenVINO；关闭后 OCR 由 ONNX Runtime 承担。"
        : "当前全局不是 OpenVINO；开启后 OCR 覆盖为 OpenVINO。";

    public string YoloOverrideHint => IsOpenVinoBackend
        ? "当前全局为 OpenVINO；关闭后 YOLO 由 ONNX Runtime 承担。"
        : "当前全局不是 OpenVINO；开启后 YOLO 覆盖为 OpenVINO。";

    public bool IsOpenVinoBackend => _owner?.SelectedBackendDraft == InferenceBackend.OpenVino;

    // ---------------- 设备与缓存 ----------------

    /// <summary>OpenVINO 设备名草稿（CPU / GPU.0 / NPU…）。</summary>
    [ObservableProperty] private string _deviceId = "CPU";

    /// <summary>启用模型编译缓存（首次 GPU / NPU 编译较慢时建议开启）。</summary>
    [ObservableProperty] private bool _enableModelCache;

    public ObservableCollection<OpenVinoDeviceInfo> Devices { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    public string DeviceText => SelectedDevice?.DisplayName ?? DeviceId;

    public string DeviceHint => HasDevices
        ? "设备名称来自 OpenVINO 运行库的枚举；GPU 为 Intel 显卡或核显。"
        : RuntimeReady
            ? "未能枚举到推理设备；请检查运行库安装与设备驱动。"
            : "尚未安装 OpenVINO 运行库；请点击「下载运行库」。";

    [ObservableProperty] private OpenVinoDeviceInfo? _selectedDevice;

    partial void OnSelectedDeviceChanged(OpenVinoDeviceInfo? value)
    {
        if (value != null && DeviceId != value.DeviceId) DeviceId = value.DeviceId;
        OnPropertyChanged(nameof(DeviceText));
    }

    partial void OnDeviceIdChanged(string value) => OnPropertyChanged(nameof(DeviceText));

    // ---------------- 运行库 ----------------

    /// <summary>OpenVINO 运行库安装与设备摘要（设置页卡片状态）。</summary>
    public string RuntimeText => RuntimeReady ? "已就绪（Plugins/OpenVINO）" : "未安装 · 需要 OpenVINO 运行库";

    /// <summary>OpenVINO 卡片的简短状态。</summary>
    public string CardStatus => !RuntimeReady ? "运行库缺失" : HasDevices ? "运行库就绪" : "运行库就绪 · 设备待检测";

    /// <summary>卡片的能力摘要。</summary>
    public string CardSummary => RuntimeReady ? $"{DeviceText} · 设备 {Devices.Count} 个" : "运行库未安装";

    public bool CanDownload => !RuntimeReady && CanInteract;

    [RelayCommand]
    private async Task DownloadRuntimeAsync()
    {
        if (_owner == null || !CanDownload) return;
        var installer = _owner.OpenVinoInstaller;
        if (installer == null) return;
        await _owner.RunOperationAsync(async token =>
        {
            var packages = await installer.GetDownloadPlanAsync(token);
            if (packages.Count == 0)
            {
                RefreshDevices(force: true);
                return;
            }

            if (!await _owner.ConfirmDownloadAsync(packages)) return;
            token.ThrowIfCancellationRequested();
            var progress = new Progress<RuntimeDownloadProgress>(p => _owner.ReportProgress(p.Message, p.Percentage, p.Total <= 0));
            await Task.Run(() => installer.InstallAsync(packages, progress, token), token);
            await Task.Run(() => installer.CleanupInstalledDownloadsAsync(token), token);
            Refresh();
            OnPropertyChanged(nameof(RuntimeReady)); OnPropertyChanged(nameof(RuntimeText));
            OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CardStatus)); OnPropertyChanged(nameof(CardSummary));
            OnPropertyChanged(nameof(DeviceHint));
        });
    }

    // ---------------- 对外契约 ----------------

    /// <summary>把本区草稿合成一份完整设置，供页面 Apply 使用。</summary>
    public OpenVinoBackendSettings BuildDraft()
        => new(UseOpenVinoOcr, UseOpenVinoYolo, DeviceId, EnableModelCache);

    /// <summary>草稿与已保存基线是否存在差异（页面「尚未应用」判断的 OpenVINO 部分）。</summary>
    public bool HasDraftChanges => !BuildDraft().Normalize().Equals(_baseline);

    /// <summary>保存成功后把草稿标记为基线，失败时保留「尚未应用」状态供用户重试。</summary>
    public void AcceptBaseline()
    {
        _baseline = BuildDraft().Normalize();
    }

    /// <summary>「恢复默认」把用途开关、设备与缓存一起回到出厂值（不影响其他页面设置）。</summary>
    public void ResetToDefault()
    {
        UseOpenVinoOcr = false;
        UseOpenVinoYolo = false;
        DeviceId = "CPU";
        EnableModelCache = false;
    }

    /// <summary>用户切到 OpenVINO 后端卡片：标记需要设备枚举并触发一次。</summary>
    public void Activate()
    {
        RefreshDevices(force: !_devicesLoaded);
    }

    /// <summary>重新探测运行库与设备（初始化、手动检测时调用）。</summary>
    public void ReloadAssets(bool enumerateDevices = true)
    {
        OnPropertyChanged(nameof(RuntimeReady)); OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(CardStatus)); OnPropertyChanged(nameof(CardSummary));
        if (!enumerateDevices) return;
        RefreshDevices(force: true);
    }

    /// <summary>高频刷新的安全入口。</summary>
    public void Refresh()
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(IsOpenVinoBackend));
        OnPropertyChanged(nameof(OcrOverrideHint)); OnPropertyChanged(nameof(YoloOverrideHint));
        OnPropertyChanged(nameof(RuntimeReady)); OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(CardStatus)); OnPropertyChanged(nameof(CardSummary));
        OnPropertyChanged(nameof(DeviceHint));
        RefreshDevices(force: false);
    }

    /// <summary>
    ///     枚举 OpenVINO 设备（openvino_c.dll 即用即卸，后台线程执行）；结果回 UI 线程后更新集合。
    /// </summary>
    private async void RefreshDevices(bool force)
    {
        if (_disposed) return;
        if (!RuntimeReady) return;
        if (_devicesLoaded && !force) return;
        _devicesLoaded = true;

        var ticket = Interlocked.Increment(ref _deviceQueryGate);
        OpenVinoDeviceInfo[] devices;
        try
        {
            devices = [.. await Task.Run(() => OpenVinoStartup.DiscoverDevices(null))];
        }
        catch
        {
            // 枚举失败时设备列表留空，交由 DeviceHint 提示，不让异常打断配置操作。
            devices = [];
        }

        if (_disposed || ticket != Volatile.Read(ref _deviceQueryGate)) return;
        // 与现有列表逐项比较，无变化时不动集合（避免 ComboBox 选择被冲空）。
        var changed = Devices.Count != devices.Length
                      || !devices.Zip(Devices).All(pair => pair.First.DeviceId == pair.Second.DeviceId
                                                           && pair.First.DisplayName == pair.Second.DisplayName);
        if (changed)
        {
            Devices.Clear();
            foreach (var device in devices) Devices.Add(device);
        }

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(DeviceHint));
        SyncSelectedDevice();
    }

    /// <summary>按草稿设备名对齐选中项；仅在当前选中与期望不一致时才写。</summary>
    private void SyncSelectedDevice()
    {
        if (Devices.Count == 0)
        {
            if (SelectedDevice != null) SelectedDevice = null;
            return;
        }

        var current = Devices.FirstOrDefault(device => device.DeviceId == DeviceId) ?? Devices[0];
        if (SelectedDevice?.DeviceId != current.DeviceId) SelectedDevice = current;
    }

    partial void OnUseOpenVinoOcrChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnUseOpenVinoYoloChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnEnableModelCacheChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _deviceQueryGate);
        Changed = null;
    }
}
