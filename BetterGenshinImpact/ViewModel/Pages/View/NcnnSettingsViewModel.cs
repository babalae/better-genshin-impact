using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>ncnn 计算模式选项（Vulkan / CPU）。</summary>
public sealed record NcnnComputeModeChoice(NcnnComputeMode Mode, string Name, string Description);

/// <summary>设备详情键值行。</summary>
public sealed record NcnnDetailRow(string Label, string Value);

/// <summary>
///     「硬件加速设置 → ncnn」的全部设置状态：引擎分配（全局后端与逐用途开关）、计算模式、
///     真实设备、三套调优草稿、设备详情与显存、产物自检。
///     <para>
///         与 <see cref="HardwareAccelerationViewModel" /> 的关系：本类通过构造注入的 owner 只读访问页面级状态
///         （所选 ONNX 引擎、待重启标记等）用于文案；ncnn 草稿经 <see cref="BuildDraft" /> 交由页面统一 Apply。
///     </para>
/// </summary>
public sealed partial class NcnnSettingsViewModel : ObservableObject
{
    private readonly HardwareAccelerationViewModel? _owner;
    private NcnnBackendSettings _baseline;

    /// <summary>设备枚举的过期令牌：丢弃慢查询返回时的过期结果，避免旧数据覆盖新选择。</summary>
    private int _deviceQueryGate;

    /// <summary>设备详情查询的过期令牌，语义同 <see cref="_deviceQueryGate" />。</summary>
    private int _detailsQueryGate;

    private bool _canInteract = true;
    private bool _devicesLoaded;
    private bool _disposed;

    /// <summary>已查询详情的设备索引；-1 表示尚未查询或已失效（避免高频刷新时重复 P/Invoke）。</summary>
    private int _detailsIndex = -1;

    /// <param name="owner">宿主页面 ViewModel；单元测试等无页面场景可传 null，相关文案自动降级。</param>
    public NcnnSettingsViewModel(NcnnConfig saved, HardwareAccelerationViewModel? owner = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        _owner = owner;
        _baseline = NcnnBackendSettings.FromConfig(saved);
        var normalized = _baseline;

        // 三套参数完全独立；旧配置缺失时由 FromConfig 统一回落到 ncnn 默认值。
        // 标题使用标准用途名：文本检测（Det）/ 文本识别（Rec）/ YOLO。
        DetTuning = new NcnnTuningViewModel("文本检测（Det）",
            "文本检测（DB）使用 det_dyn 动态模型，调优只影响该用途的会话。", normalized.Det);
        RecTuning = new NcnnTuningViewModel("文本识别（Rec）",
            "文本识别（CTC）使用 rec_dyn 动态宽度模型，线程与并行会话可独立设置。", normalized.Rec);
        YoloTuning = new NcnnTuningViewModel("YOLO",
            "所有 ncnn YOLO 模型共用这套参数；像素直输需模型自带归一化。", normalized.Yolo);
        NcnnTunings = [DetTuning, RecTuning, YoloTuning];
        foreach (var tuning in NcnnTunings) tuning.Changed += OnTuningChanged;

        _ncnnComputeMode = normalized.ComputeMode;
        _ncnnGpuIndex = normalized.GpuIndex;
        _selectedBackend = normalized.Backend;
        _useNcnnOcr = normalized.UseOcr;
        _useNcnnYolo = normalized.UseYolo;

        NcnnThreadChoices = Enumerable.Range(1, NcnnTuningSettings.MaxThreads)
            .Select(i => new InferenceThreadChoice(i,
                i == NcnnTuningSettings.DefaultThreads ? $"{i}（默认）" : i.ToString())).ToArray();
        NcnnParallelChoices = Enumerable.Range(1, NcnnTuningSettings.MaxParallelSessions)
            .Select(i => new InferenceThreadChoice(i, i == 1 ? "1（顺序执行，推荐）" : $"{i} 路并行")).ToArray();
        BackendOptions.Add(new InferenceBackendOptionViewModel(InferenceBackend.Ncnn));
        BackendOptions.Add(new InferenceBackendOptionViewModel(InferenceBackend.Onnx));
        // 构造阶段只做文件层探测（字段初始化已完成）；设备枚举与 Vulkan 详情由 Activate / ReloadAssets 触发，
        // 避免仅仅打开设置页或构造测试就初始化 Vulkan。
    }

    // ---------------- 三套调优草稿 ----------------

    public NcnnTuningViewModel DetTuning { get; }
    public NcnnTuningViewModel RecTuning { get; }
    public NcnnTuningViewModel YoloTuning { get; }
    public NcnnTuningViewModel[] NcnnTunings { get; }
    public InferenceThreadChoice[] NcnnThreadChoices { get; }
    public InferenceThreadChoice[] NcnnParallelChoices { get; }

    /// <summary>三套调优的草稿变更汇总，用于「精度与调优」卡片头部副标题。</summary>
    public string TuningChangeSummary
    {
        get
        {
            var changed = NcnnTunings.Count(t => t.HasChanges);
            return changed == 0 ? "三套参数均与已保存一致" : $"{changed}/3 套已修改 · 应用并重启后生效";
        }
    }

    public NcnnComputeModeChoice[] NcnnComputeModeChoices { get; } =
    [
        new(NcnnComputeMode.Vulkan, "Vulkan", "调用 Vulkan 显卡，速度最快；设备名称由 ncnn_shim 枚举。"),
        new(NcnnComputeMode.Cpu, "CPU", "不依赖 Vulkan 驱动；使用下方线程数在处理器上推理。")
    ];

    /// <summary>页面忙碌时统一禁用子控件（由主 ViewModel 单向同步）。</summary>
    public bool CanInteract
    {
        get => _canInteract;
        set
        {
            if (SetProperty(ref _canInteract, value)) OnPropertyChanged(nameof(CanProbe));
        }
    }

    /// <summary>当前是否处于活跃状态：用户展开过 ncnn 卡片或后端已选 ncnn。为 true 时刷新才会更新设备详情。</summary>
    public bool IsActive { get; set; }

    /// <summary>任意草稿变化（引擎分配 / 调优 / 计算模式 / 设备），页面据此刷新底部摘要与「尚未应用」。</summary>
    public event EventHandler? Changed;

    // ---------------- 引擎分配（全局后端 + 逐用途开关） ----------------

    /// <summary>全局后端选择只决定两个用途开关的默认值，逐用途开关才是实际生效来源。</summary>
    [ObservableProperty] private InferenceBackend _selectedBackend = InferenceBackend.Onnx;

    /// <summary>OCR 是否走 ncnn（草稿；应用后写入 NcnnConfig.UseOcr）。</summary>
    [ObservableProperty] private bool _useNcnnOcr;

    /// <summary>YOLO 是否走 ncnn（草稿；应用后写入 NcnnConfig.UseYolo）。</summary>
    [ObservableProperty] private bool _useNcnnYolo;

    public ObservableCollection<InferenceBackendOptionViewModel> BackendOptions { get; } = [];

    public bool IsNcnnBackend => SelectedBackend == InferenceBackend.Ncnn;
    public bool IsOnnxBackend => !IsNcnnBackend;
    public bool UsesNcnn => UseNcnnOcr || UseNcnnYolo;

    private bool OwnerEngineIsCpu => _owner?.SelectedEngine?.IsCpu == true;

    /// <summary>某个用途单独切回 ONNX 时的目标名称。</summary>
    public string OnnxTargetName => _owner != null && _owner.ForceCpuOcr && !OwnerEngineIsCpu
        ? "CPU（强制 OCR）"
        : _owner?.SelectedEngine?.Name ?? "ONNX Runtime";

    public string EffectiveCombinationText => $"OCR · {(UseNcnnOcr
        ? $"ncnn · {NcnnDeviceText} · PP-OCRv6（固定模型）"
        : $"ONNX Runtime · {OnnxTargetName}")}　|　YOLO · {(UseNcnnYolo
        ? $"ncnn · {NcnnDeviceText}"
        : $"ONNX Runtime · {_owner?.SelectedEngine?.Name ?? "ONNX Runtime"} · {_owner?.SelectedEngine?.SelectedDevice?.Name ?? "尚无可用设备"}")}";

    /// <summary>已保存的全局后端摘要（引擎分配区底部状态条）。</summary>
    public string SavedBackendText
    {
        get
        {
            if (_owner == null) return UsesNcnn ? "ncnn · 单元测试上下文" : "ONNX Runtime";
            var config = _owner.Config.Ncnn;
            return config.PreferredBackend == InferenceBackend.Ncnn
                ? $"ncnn · {SavedNcnnDeviceText(config)} · ONNX Runtime 侧 {_owner.Config.InferenceDevice}"
                : $"ONNX Runtime · {_owner.Config.InferenceDevice}";
        }
    }

    internal static string SavedNcnnDeviceText(NcnnConfig config) =>
        config.ComputeMode == NcnnComputeMode.Cpu
            ? $"CPU · {config.DetTuning.Threads} 线程"
            : $"Vulkan 设备 #{config.GpuIndex}";

    public string OcrOverrideHint => IsNcnnBackend
        ? "当前全局为 ncnn；关闭后 OCR 由 ONNX Runtime 承担。"
        : "当前全局为 ONNX Runtime；开启后 OCR 覆盖为 ncnn。";

    public string YoloOverrideHint => IsNcnnBackend
        ? "当前全局为 ncnn；关闭后 YOLO 由 ONNX Runtime 承担。"
        : "当前全局为 ONNX Runtime；开启后 YOLO 覆盖为 ncnn。";

    // ---------------- 计算模式与设备 ----------------

    /// <summary>ncnn 计算模式：Vulkan 显卡加速或 ncnn 原生 CPU 推理。</summary>
    [ObservableProperty] private NcnnComputeMode _ncnnComputeMode = NcnnComputeMode.Vulkan;
    [ObservableProperty] private NcnnDeviceInfo? _selectedNcnnDevice;
    [ObservableProperty] private int _ncnnGpuIndex;

    public ObservableCollection<NcnnDeviceInfo> NcnnDevices { get; } = [];

    public bool IsNcnnVulkan => NcnnComputeMode == NcnnComputeMode.Vulkan;
    public bool IsNcnnCpu => !IsNcnnVulkan;
    public bool HasNcnnDevices => NcnnDevices.Count > 0;

    public string NcnnDeviceText => IsNcnnCpu
        ? $"CPU · {DetTuning.Settings.Threads} 线程（检测基准）"
        : SelectedNcnnDevice?.Name ?? $"Vulkan 设备 #{NcnnGpuIndex}";

    public string NcnnDeviceHint => IsNcnnCpu
        ? "ncnn 原生 CPU 推理，三套线程数各自独立，不需要 Vulkan 驱动。"
        : HasNcnnDevices
            ? "设备名称来自 ncnn_shim 的 Vulkan 枚举；索引 0 通常为独显。"
            : "未能枚举到 Vulkan 设备；请检查显卡驱动，或改用 CPU 计算模式。";

    /// <summary>ncnn 卡片的简短状态（产物与运行库是否齐全）。</summary>
    public string CardStatus => !NcnnShimReady ? "运行库缺失" : NcnnAssetsReady ? "产物齐全" : "部分产物缺失";

    /// <summary>卡片的能力摘要；没有详情时退回产物状态。</summary>
    public string CardSummary => NcnnDeviceDetails is { } d
        ? $"{NcnnDeviceText} · FP16 {d.Fp16Text} · INT8 {d.Int8Text}" + (d.ResizableBarEnabled ? " · ReBAR" : "")
        : $"{NcnnDeviceText} · {CardStatus}";

    // ---------------- 运行库与产物 ----------------

    /// <summary>ncnn 资产就绪情况（打开设置页与手动重新检测时探测一次）。</summary>
    [ObservableProperty] private NcnnAvailability _ncnnStatus = NcnnAvailability.Detect();

    public bool NcnnShimReady => NcnnStatus.ShimReady;
    public string NcnnSummary => NcnnStatus.Summary;
    public bool NcnnAssetsReady => NcnnStatus.IsReady;
    public IReadOnlyList<NcnnAssetGroup> NcnnAssetGroups => NcnnStatus.Groups;

    public string NcnnRuntimeText => NcnnShimReady
        ? "已就绪（ncnn_shim）"
        : "缺失 · 需要 ncnn_shim.dll";

    public string NcnnApiText => NcnnShimReady
        ? "版本化接口（create_ex / infer_ex）：调优参数、张量元信息、设备详情可用"
        : "运行库缺失 · 无法读取 shim 能力";

    // ---------------- 设备详情与显存 ----------------

    [ObservableProperty] private NcnnDeviceDetails? _ncnnDeviceDetails;
    [ObservableProperty] private uint _ncnnHeapBudgetMB;

    /// <summary>设备详情查询失败的原因（不影响设备选择，仅在诊断区展示）。</summary>
    [ObservableProperty] private string _deviceDetailsErrorText = "";

    public bool HasDeviceDetailsError => DeviceDetailsErrorText.Length > 0;

    partial void OnDeviceDetailsErrorTextChanged(string value) => OnPropertyChanged(nameof(HasDeviceDetailsError));

    public bool HasNcnnDeviceDetails => NcnnDeviceDetails != null;

    public IReadOnlyList<NcnnDetailRow> NcnnDetailRows => NcnnDeviceDetails is { } d ? BuildDetailRows(d) : [];

    public string NcnnHeapBudgetText => NcnnHeapBudgetMB == 0 ? "显存预算未知" : $"{NcnnHeapBudgetMB} MB";

    public string NcnnCapabilityText => NcnnDeviceDetails is { } d
        ? $"FP16 {d.Fp16Text} · INT8 {d.Int8Text}" + (d.ResizableBarEnabled ? " · ReBAR" : "")
        : "";

    // ---------------- 模型自检 ----------------

    [ObservableProperty] private string _ncnnBlobText = "尚未探测；可点击「探测 blob 名」读取真实输入输出名与形状。";
    [ObservableProperty] private bool _isProbing;

    /// <summary>探测按钮可用性：页面忙碌或正在探测时置灰。</summary>
    public bool CanProbe => CanInteract && !IsProbing;

    partial void OnIsProbingChanged(bool value) => OnPropertyChanged(nameof(CanProbe));

    // ---------------- 对外契约 ----------------

    /// <summary>把本区草稿（引擎分配 + 三套调优 + 计算模式）合成一份完整设置，供页面 Apply 使用。</summary>
    public NcnnBackendSettings BuildDraft()
        => new(SelectedBackend, UseNcnnOcr, UseNcnnYolo, NcnnComputeMode, NcnnGpuIndex,
            DetTuning.Snapshot(), RecTuning.Snapshot(), YoloTuning.Snapshot());

    /// <summary>草稿与已保存基线是否存在差异（页面「尚未应用」判断的 ncnn 部分）。</summary>
    public bool HasDraftChanges => !BuildDraft().Normalize().Equals(_baseline);

    /// <summary>保存成功后把草稿标记为基线，失败时保留「尚未应用」状态供用户重试。</summary>
    public void AcceptBaseline()
    {
        foreach (var tuning in NcnnTunings) tuning.AcceptBaseline();
        _baseline = BuildDraft().Normalize();
        OnPropertyChanged(nameof(TuningChangeSummary));
    }

    /// <summary>「恢复默认」把引擎分配、计算模式与三套调优一起回到出厂值（不影响其他页面设置）。</summary>
    public void ResetToDefault()
    {
        SelectedBackend = InferenceBackend.Onnx;
        UseNcnnOcr = false;
        UseNcnnYolo = false;
        NcnnComputeMode = NcnnComputeMode.Vulkan;
        NcnnGpuIndex = 0;
        foreach (var tuning in NcnnTunings) tuning.Reload(NcnnTuningSettings.CreateDefault());
    }

    /// <summary>
    ///     用户展开 ncnn 卡片或切到 ncnn 后端：标记活跃并做一次完整探测（资产文件 + Vulkan 设备枚举 + 详情）。
    ///     ncnn 设置的可配置性独立于全局引擎选择——先配置、后启用。
    /// </summary>
    [RelayCommand]
    public void Activate()
    {
        if (_disposed) return;
        IsActive = true;
        ReloadAssets();
    }

    /// <summary>
    ///     重新探测资产文件（初始化、手动检测时调用）。
    ///     <paramref name="enumerateDevices" /> 为 false 时跳过设备枚举，避免从没展开过 ncnn 卡片的用户承担 Vulkan 初始化开销。
    ///     设备枚举与详情查询在后台线程执行原生调用，结果回 UI 线程后再更新集合（首次枚举会初始化 Vulkan 实例，耗时可达秒级）。
    /// </summary>
    public void ReloadAssets(bool enumerateDevices = true)
    {
        if (_disposed) return;
        NcnnStatus = NcnnAvailability.Detect();
        if (!enumerateDevices) return;
        EnsureDevices(force: true);
        RefreshDetails(NcnnGpuIndex, force: true);
    }

    /// <summary>
    ///     高频刷新的安全入口：只做设备与详情的按索引缓存查询，不做文件探测，也不重复 P/Invoke。
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        RefreshBackendOptions();
        OnPropertyChanged(nameof(EffectiveCombinationText)); OnPropertyChanged(nameof(OnnxTargetName));
        OnPropertyChanged(nameof(OcrOverrideHint)); OnPropertyChanged(nameof(YoloOverrideHint));
        if (!IsActive || !NcnnShimReady || !IsNcnnVulkan || !HasNcnnDevices) return;
        RefreshDetails(NcnnGpuIndex);
    }

    // ---------------- 内部刷新 ----------------

    /// <summary>引擎分配二选一卡片的当前/待重启/缺失状态。</summary>
    private void RefreshBackendOptions()
    {
        if (_owner == null) return;
        var pending = _owner.HasPendingChanges;
        foreach (var option in BackendOptions)
        {
            option.IsCurrent = option.Backend == _owner.Config.Ncnn.PreferredBackend;
            option.IsBlocked = option.Backend == InferenceBackend.Ncnn && !NcnnShimReady;
            option.StatusText = option.Backend switch
            {
                // 设备详情可用时把能力摘要一并显示（FP16 / INT8 / ReBAR），否则只显示设备与产物状态。
                InferenceBackend.Ncnn => CardSummary + (option.IsCurrent && pending ? " · 待重启" : ""),
                _ => option.IsCurrent
                    ? (pending ? $"{_owner.Config.InferenceDevice} · 待重启" : $"{_owner.Config.InferenceDevice} · 当前使用")
                    : $"{_owner.SelectedEngine?.Name ?? _owner.Config.InferenceDevice.ToString()}"
            };
        }
    }

    /// <summary>
    ///     枚举 Vulkan 设备。原生调用（首次会初始化 ncnn 的 Vulkan 实例）放在后台线程；
    ///     结果回 UI 线程应用，过期结果（期间又发起了新枚举）直接丢弃。
    /// </summary>
    private async void EnsureDevices(bool force)
    {
        if (_disposed || !IsNcnnVulkan || !NcnnShimReady) return;
        if (_devicesLoaded && !force) return;
        _devicesLoaded = true;
        if (force) NcnnShim.ResetDeviceCache();

        var ticket = Interlocked.Increment(ref _deviceQueryGate);
        NcnnDeviceInfo[] devices;
        var defaultIndex = -1;
        try
        {
            var query = await Task.Run(() => NcnnShim.QueryDevices()).ConfigureAwait(true);
            if (_disposed || ticket != Volatile.Read(ref _deviceQueryGate)) return;
            devices = [.. query.Devices];
            defaultIndex = query.DefaultIndex;
        }
        catch
        {
            // 枚举失败（驱动异常 / 导出缺失）时设备列表留空，交由 NcnnDeviceHint 提示，不让异常打断配置操作。
            if (_disposed || ticket != Volatile.Read(ref _deviceQueryGate)) return;
            devices = [];
        }

        // 与现有列表逐项比较，无变化时不动集合：Clear/重填会把 ComboBox 的 SelectedItem 冲空并回写 null，
        // 在用户点击选择的同一事件周期内发生时表现为「设备能看到但无法选择」。
        var changed = NcnnDevices.Count != devices.Length
                      || !devices.Zip(NcnnDevices).All(pair => pair.First.Index == pair.Second.Index
                                                               && pair.First.Name == pair.Second.Name);
        if (changed)
        {
            NcnnDevices.Clear();
            foreach (var device in devices) NcnnDevices.Add(device);
        }

        OnPropertyChanged(nameof(HasNcnnDevices));
        OnPropertyChanged(nameof(NcnnDeviceHint));
        SyncSelectedDevice(defaultIndex);
    }

    /// <summary>按草稿索引对齐选中项；仅在当前选中与期望不一致时才写，避免覆盖用户正在进行的选择。</summary>
    private void SyncSelectedDevice(int defaultIndex)
    {
        if (NcnnDevices.Count == 0)
        {
            if (SelectedNcnnDevice != null) SelectedNcnnDevice = null;
            return;
        }

        // 已保存/草稿索引优先；索引不存在时用 ncnn 默认设备（独显优先），最后退回第一项。
        var current = NcnnDevices.FirstOrDefault(device => device.Index == NcnnGpuIndex)
                      ?? NcnnDevices.FirstOrDefault(device => device.Index == defaultIndex)
                      ?? NcnnDevices[0];
        if (SelectedNcnnDevice?.Index != current.Index) SelectedNcnnDevice = current;
    }

    /// <summary>
    ///     读取单个设备的能力与显存预算。原生调用放在后台线程，结果回 UI 线程应用；
    ///     查询失败降级为无详情，原因在诊断区展示，不阻断设备选择。
    /// </summary>
    private async void RefreshDetails(int gpuIndex, bool force = false)
    {
        if (_disposed) return;
        if (!IsNcnnVulkan || !NcnnShimReady)
        {
            Interlocked.Increment(ref _detailsQueryGate);
            _detailsIndex = -1;
            NcnnDeviceDetails = null;
            NcnnHeapBudgetMB = 0;
            DeviceDetailsErrorText = "";
            return;
        }

        if (!force && _detailsIndex == gpuIndex) return;
        _detailsIndex = gpuIndex;

        var ticket = Interlocked.Increment(ref _detailsQueryGate);
        try
        {
            var details = await Task.Run(() => NcnnShim.QueryDeviceDetails(gpuIndex)).ConfigureAwait(true);
            var budget = details?.HeapBudgetMB ?? 0;
            if (budget == 0)
                budget = await Task.Run(() => NcnnShim.QueryHeapBudget(gpuIndex)).ConfigureAwait(true);
            if (_disposed || ticket != Volatile.Read(ref _detailsQueryGate)) return;
            NcnnDeviceDetails = details;
            NcnnHeapBudgetMB = budget;
            DeviceDetailsErrorText = "";
        }
        catch (Exception e)
        {
            if (_disposed || ticket != Volatile.Read(ref _detailsQueryGate)) return;
            NcnnDeviceDetails = null;
            NcnnHeapBudgetMB = 0;
            DeviceDetailsErrorText = e.Message;
        }
    }

    private void OnTuningChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(NcnnDeviceText));
        OnPropertyChanged(nameof(TuningChangeSummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedBackendChanged(InferenceBackend value)
    {
        // 全局切换把两个用途开关重置为该引擎默认值，高级区再单独覆盖。
        UseNcnnOcr = value == InferenceBackend.Ncnn;
        UseNcnnYolo = value == InferenceBackend.Ncnn;
        OnPropertyChanged(nameof(IsNcnnBackend)); OnPropertyChanged(nameof(IsOnnxBackend));
        OnPropertyChanged(nameof(OcrOverrideHint)); OnPropertyChanged(nameof(YoloOverrideHint));
        // 顶部切到 ncnn 才探测资产与设备，避免 ONNX 用户承担 Vulkan 初始化开销（枚举异步执行）。
        if (value == InferenceBackend.Ncnn) Activate();
        RefreshBackendOptions();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnUseNcnnOcrChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveCombinationText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnUseNcnnYoloChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveCombinationText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNcnnComputeModeChanged(NcnnComputeMode value)
    {
        OnPropertyChanged(nameof(IsNcnnVulkan));
        OnPropertyChanged(nameof(NcnnDeviceText));
        OnPropertyChanged(nameof(NcnnDeviceHint));
        EnsureDevices(force: true);
        RefreshDetails(NcnnGpuIndex, force: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNcnnGpuIndexChanged(int value)
    {
        if (SelectedNcnnDevice?.Index != value)
            SelectedNcnnDevice = NcnnDevices.FirstOrDefault(device => device.Index == value);
        RefreshDetails(value, force: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedNcnnDeviceChanged(NcnnDeviceInfo? value)
    {
        if (value != null && NcnnGpuIndex != value.Index) NcnnGpuIndex = value.Index;
        OnPropertyChanged(nameof(NcnnDeviceText));
        OnPropertyChanged(nameof(CardSummary));
    }

    partial void OnNcnnDeviceDetailsChanged(NcnnDeviceDetails? value)
    {
        OnPropertyChanged(nameof(HasNcnnDeviceDetails));
        OnPropertyChanged(nameof(NcnnDetailRows));
        OnPropertyChanged(nameof(NcnnCapabilityText));
        OnPropertyChanged(nameof(CardSummary));
    }

    partial void OnNcnnHeapBudgetMBChanged(uint value) => OnPropertyChanged(nameof(NcnnHeapBudgetText));

    partial void OnNcnnStatusChanged(NcnnAvailability value)
    {
        OnPropertyChanged(nameof(NcnnShimReady));
        OnPropertyChanged(nameof(NcnnSummary));
        OnPropertyChanged(nameof(NcnnAssetsReady));
        OnPropertyChanged(nameof(NcnnAssetGroups));
        OnPropertyChanged(nameof(NcnnRuntimeText));
        OnPropertyChanged(nameof(NcnnApiText));
        OnPropertyChanged(nameof(CardStatus));
        OnPropertyChanged(nameof(CardSummary));
        RefreshBackendOptions();
    }

    /// <summary>只读探测：加载最小桶的 OCR det / rec 模型，输出真实 blob 名与形状，不影响业务会话。</summary>
    [RelayCommand]
    private async Task ProbeNcnnBlobsAsync()
    {
        if (IsProbing) return;
        if (!NcnnShimReady)
        {
            NcnnBlobText = "ncnn_shim.dll 缺失，无法探测；请先补齐运行库。";
            return;
        }

        IsProbing = true;
        NcnnBlobText = "正在读取 ncnn 模型的 blob 名…";
        try
        {
            var gpuIndex = IsNcnnCpu ? -1 : NcnnGpuIndex;
            var det = DetTuning.Snapshot();
            var rec = RecTuning.Snapshot();
            NcnnBlobText = await Task.Run(() => NcnnBlobProbe.Describe(null,
                NcnnAvailability.OcrModelDirectory, det, rec, gpuIndex));
        }
        catch (Exception ex)
        {
            NcnnBlobText = "探测失败：" + ex.Message;
        }
        finally
        {
            IsProbing = false;
        }
    }

    /// <summary>设备详情键值行；未知字段按「未知」显示而不是留空，避免表格错位。</summary>
    private static IReadOnlyList<NcnnDetailRow> BuildDetailRows(NcnnDeviceDetails d) =>
    [
        new("设备名称", d.Name),
        new("设备类型", d.TypeName),
        new("Vendor / Device ID", $"{d.VendorText} / {d.DeviceIdText}"),
        new("Vulkan API 版本", d.ApiVersionText),
        new("驱动版本", d.DriverVersionText),
        new("驱动名称", string.IsNullOrWhiteSpace(d.DriverName) ? "未知" : d.DriverName),
        new("计算 / 传输队列", string.Create(CultureInfo.InvariantCulture,
            $"{d.ComputeQueueCount} / {d.TransferQueueCount}")),
        new("统一队列", d.UnifiedComputeTransferQueue ? "是" : "否"),
        new("ReBAR（显存直通）", d.ResizableBarEnabled ? "已启用" : "未启用"),
        new("image storage", d.SupportImageStorage ? "支持" : "不支持"),
        new("shader local memory", d.PreferShaderLocalMemory ? "优先使用" : "不使用"),
        new("FP16", d.Fp16Text),
        new("INT8", d.Int8Text),
        new("INT16", d.Int16Text),
        new("BF16", d.SupportBf16Storage ? "storage 支持" : "不支持"),
        new("Subgroup", string.Create(CultureInfo.InvariantCulture,
            $"{d.SubgroupSize}（{d.MinSubgroupSize}–{d.MaxSubgroupSize}）")),
        new("Subgroup 能力", DescribeSubgroup(d)),
        new("共享内存 / 工作组", string.Create(CultureInfo.InvariantCulture,
            $"{d.MaxSharedMemorySize / 1024} KB / {d.MaxWorkgroupInvocations}")),
        new("性能评分", string.Create(CultureInfo.InvariantCulture, $"{d.RoughScore}")),
        new("显存预算", d.HeapBudgetMB == 0
            ? "未知"
            : string.Create(CultureInfo.InvariantCulture, $"{d.HeapBudgetMB} MB"))
    ];

    private static string DescribeSubgroup(NcnnDeviceDetails d)
    {
        var parts = new List<string>(3);
        if (d.SupportSubgroupSizeControl) parts.Add("size control");
        if (d.SupportComputeFullSubgroups) parts.Add("full subgroups");
        if (d.SupportFpFastMath) parts.Add("fast math");
        return parts.Count == 0 ? "无额外能力" : string.Join(" / ", parts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 令牌自增使仍在途的后台查询结果在回 UI 线程时被丢弃。
        Interlocked.Increment(ref _deviceQueryGate);
        Interlocked.Increment(ref _detailsQueryGate);
        foreach (var tuning in NcnnTunings) tuning.Changed -= OnTuningChanged;
        Changed = null;
    }
}
