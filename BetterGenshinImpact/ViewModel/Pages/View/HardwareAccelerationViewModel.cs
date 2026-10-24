using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public sealed record InferenceThreadChoice(int Count, string Name);
public sealed record PaddleOcrCpuModeChoice(PaddleOcrCpuMode Mode, string Name, string Description);

public partial class HardwareAccelerationViewModel : ObservableObject, IViewModel, IDisposable
{
    private readonly InferenceRuntimeService _runtime;
    private readonly IOnnxRuntimeInstaller _installer;
    private readonly IInferenceDeviceDiscovery _devices;
    private readonly IInferenceModelDiagnostics _diagnostics;
    private readonly BgiOnnxFactory _factory;
    private readonly IConfigService _configService;
    private readonly IInferenceBenchmarkLeaseProvider _leases;
    private readonly IInferenceSettingsInteraction _interaction;
    private readonly DispatcherTimer _taskStatusTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operationCancellation;
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    public ObservableCollection<InferenceEngineCardViewModel> Engines { get; } = [];
    public ObservableCollection<InferenceBenchmarkResult> Results { get; } = [];
    public InferenceThreadChoice[] CpuThreadChoices { get; }
    public int MaxCpuThreads => Environment.ProcessorCount;
    public PaddleOcrCpuModeChoice[] OcrCpuModeChoices { get; } =
    [
        new(PaddleOcrCpuMode.FollowGlobal, "跟随全局设置", "使用 CPU 卡片中的全局线程数。"),
        new(PaddleOcrCpuMode.RuntimeAuto, "运行库自动调度", "不指定线程参数，由 ONNX Runtime 决定。"),
        new(PaddleOcrCpuMode.GameplayPriority, "游戏流畅优先", "限制 OCR 的并行线程，减少游戏掉帧。"),
        new(PaddleOcrCpuMode.Custom, "自定义设置", "分别指定文本检测、文字识别和模型节点的线程数。")
    ];
    public PaddleOcrCpuConfig PaddleOcrCpu { get; }
    public bool IsOcrCpuCustom => PaddleOcrCpu.Mode == PaddleOcrCpuMode.Custom;
    public bool IsOcrCpuConfigured => PaddleOcrCpu.Mode is PaddleOcrCpuMode.GameplayPriority or PaddleOcrCpuMode.Custom;
    public string OcrCpuPreview => PaddleOcrCpu.ToOptions().Describe(CpuThreads);
    public string OcrCpuApplicability => ForceCpuOcr || SelectedEngine?.IsCpu == true
        ? "应用并重启后用于 PPOCR 的 CPU 检测和识别会话。"
        : "当前选择让 OCR 跟随其他引擎；以下参数会保存，在 PPOCR 使用纯 CPU 时生效。";
    public string OcrCpuModeHint => $"当前可用逻辑处理器数：{Environment.ProcessorCount}。" + (PaddleOcrCpu.Mode switch
    {
        PaddleOcrCpuMode.GameplayPriority => "文本检测使用逻辑处理器数的一半；文字识别使用四分之一并限制在 2～4 个线程。两个模型均顺序执行节点，在降低 OCR CPU 峰值的同时保留必要性能。",
        PaddleOcrCpuMode.Custom => "可分别配置文本检测和文字识别的计算线程。0 表示由 ONNX Runtime 自动选择；模型节点线程数仅在开启节点并行时生效。",
        PaddleOcrCpuMode.RuntimeAuto => "不指定 OCR 的线程数、执行方式和线程等待策略，完全交给 ONNX Runtime 调度；CPU 卡片中的全局线程数不会用于 OCR。",
        _ => "OCR 与其他 CPU 模型使用相同的全局线程数；全局线程数为 0 时由 ONNX Runtime 自动选择。"
    });
    public string CurrentRuntimeText => _runtime.Current.Description;
    public string CurrentEngineName => _runtime.Current.Engine == InferenceDeviceType.Cpu ? "CPU" : _runtime.Current.Engine.ToString();
    public string CurrentOcrText => _runtime.Current.OcrDescription;
    public string CurrentValidationText => _runtime.Current.Engine == InferenceDeviceType.Cpu
        ? "内置 CPU · 无需额外验证"
        : _runtime.IsCurrentValidated ? "模型兼容性检查通过" : "已生效 · 尚未完成模型兼容性检查";
    private HardwareAccelerationConfig Config => _configService.Get().HardwareAccelerationConfig;
    public bool HasPendingChanges => _runtime.HasPendingRestart;
    public string PendingText
    {
        get
        {
            if (!HasPendingChanges) return "";
            var card = Engines.FirstOrDefault(c => c.Engine == Config.InferenceDevice) ?? Engines.First(c => c.IsCpu);
            var savedId = Config.GetEngineSettings(Config.InferenceDevice).DeviceId;
            var device = card.Devices.FirstOrDefault(d => d.Id == savedId)?.Name ?? savedId;
            return $"{card.Name} · {device}";
        }
    }
    public bool HasTreeImage => _diagnostics.HasDomainTreeImage;
    public string TreeImageText => HasTreeImage ? "完整古树 Detect · 预热 1 次，测量 10 次" : "测试图片待提供";
    public string TreeBenchmarkEmptyText => HasTreeImage ? "还没有测速结果" : "接入测试图片后可测量完整 Detect 耗时。";
    public string ValidationHint => HasTreeImage
        ? "重启后将运行一次古树检测，确认所选运行库和设备能够完成真实推理；失败时自动回退 CPU。此检查不判断每个算子的实际执行设备。"
        : "重启后将运行一次 OCR 模型，确认所选运行库和设备能够完成真实推理；失败时自动回退 CPU。此检查不判断每个算子的实际执行设备。";
    public bool CanApply => !IsBusy && SelectedEngine is { SelectedDevice: not null, IsInstalled: true }
        && (HasDraftChanges || HasPendingChanges);
    public string ApplyButtonText => HasPendingChanges && !HasDraftChanges ? "立即重启" : "应用并重启";
    public string SelectionText => SelectedEngine == null ? "请选择推理引擎" : $"{SelectedEngine.Name} · {SelectedEngine.SelectedDevice?.Name ?? "尚无可用设备"}";
    public string ApplyHint => IsBusy ? "请等待当前操作结束"
        : SelectedEngine == null ? "点击卡片选择引擎"
        : !SelectedEngine.IsInstalled || SelectedEngine.SelectedDevice == null ? SelectedEngine.ApplyBlockReason
        : HasDraftChanges ? "尚未应用 · 保存后可立即重启或稍后重启"
        : HasPendingChanges ? "设置已保存 · 等待重启生效"
        : "与当前生效设置相同";
    public string BenchmarkHint => !_leases.IsAvailable ? "性能测试不会自动执行；任务运行中，结束任务后可手动测速。"
        : "性能测试不会自动执行；点击按钮后使用当前生效配置，预热 1 次并连续测量 10 次。";
    public InferenceBenchmarkResult? OcrResult => Results.FirstOrDefault(r => r.Scene == "OCR");
    public InferenceBenchmarkResult? TreeResult => Results.FirstOrDefault(r => r.Scene == "古树检测");
    public bool HasOcrResult => OcrResult != null;
    public bool HasTreeResult => TreeResult != null;
    private bool HasDraftChanges
    {
        get
        {
            if (SelectedEngine?.SelectedDevice == null) return false;
            if (HasPendingChanges)
            {
                var saved = Config.GetEngineSettings(Config.InferenceDevice);
                return SelectedEngine.Engine != Config.InferenceDevice || SelectedEngine.SelectedDevice.Id != saved.DeviceId
                    || ForceCpuOcr != Config.CpuOcr || CpuThreads != Config.Cpu.CpuThreads
                    || PaddleOcrCpu.ToOptions() != Config.PaddleOcrCpu.ToOptions()
                    || (SelectedEngine.SupportsCache && SelectedEngine.Settings.EnableModelCache != saved.EnableModelCache);
            }
            var current = _runtime.Current;
            return SelectedEngine.Engine != current.Engine || SelectedEngine.SelectedDevice.Id != current.Device.Id
                || ForceCpuOcr != current.CpuOcr || CpuThreads != current.CpuThreads
                || PaddleOcrCpu.ToOptions() != current.PaddleOcrCpu
                || (SelectedEngine.SupportsCache && SelectedEngine.Settings.EnableModelCache != current.EnableModelCache)
                || (!SelectedEngine.IsCpu && !string.Equals(SelectedEngine.RuntimeDirectory, current.RuntimeDirectory, StringComparison.OrdinalIgnoreCase));
        }
    }
    public bool CanBenchmark => !IsBusy && _leases.IsAvailable;
    public bool CanBenchmarkTree => CanBenchmark && HasTreeImage;
    public bool CanInteract => !IsBusy;

    [ObservableProperty] private InferenceEngineCardViewModel? _selectedEngine;
    [ObservableProperty] private bool _forceCpuOcr;
    [ObservableProperty] private int _cpuThreads;
    [ObservableProperty] private bool _isOcrAdvancedExpanded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasOperationMessage))] private string _progressText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string _errorDetails = "";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorDetails);
    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(ProgressText);

    public HardwareAccelerationViewModel(InferenceRuntimeService runtime, IOnnxRuntimeInstaller installer,
        IInferenceDeviceDiscovery devices, IInferenceModelDiagnostics diagnostics, BgiOnnxFactory factory, IConfigService configService,
        IInferenceBenchmarkLeaseProvider leases, IInferenceSettingsInteraction interaction)
    {
        _runtime = runtime;
        _installer = installer;
        _devices = devices;
        _diagnostics = diagnostics;
        _factory = factory;
        _configService = configService;
        _leases = leases;
        _interaction = interaction;
        _forceCpuOcr = Config.CpuOcr;
        _cpuThreads = Config.Cpu.CpuThreads;
        PaddleOcrCpu = PaddleOcrCpuConfig.FromOptions(Config.PaddleOcrCpu.ToOptions());
        _isOcrAdvancedExpanded = false;
        PaddleOcrCpu.PropertyChanged += OcrCpuSettingsChanged;
        CpuThreadChoices = Enumerable.Range(0, Environment.ProcessorCount + 1)
            .Select(i => new InferenceThreadChoice(i, i == 0 ? "自动（ORT 默认）" : i.ToString())).ToArray();
        foreach (var engine in Enum.GetValues<InferenceDeviceType>().Where(engine => engine.IsSelectable()))
        {
            var card = new InferenceEngineCardViewModel(engine, Config.GetEngineSettings(engine));
            card.PropertyChanged += CardChanged;
            card.Settings.PropertyChanged += CardSettingsChanged;
            Engines.Add(card);
        }
        // TensorRT 已下线：历史配置可能在启动准备之前仍是 TRT，这里回退到 CPU 卡片而不是直接抛异常。
        SelectedEngine = Engines.FirstOrDefault(card => card.Engine == Config.InferenceDevice)
            ?? Engines.First(card => card.IsCpu);
        _taskStatusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => RefreshAvailability(), Dispatcher.CurrentDispatcher);
        _runtime.SettingsChanged += RuntimeSettingsChanged;
    }

    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InferenceEngineCardViewModel.SelectedDevice))
        {
            if (!IsBusy) SelectedEngine = (InferenceEngineCardViewModel)sender!;
            RefreshAvailability();
        }
    }
    private void CardSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsBusy && e.PropertyName == nameof(InferenceEngineSettings.EnableModelCache))
            SelectedEngine = Engines.First(card => ReferenceEquals(card.Settings, sender));
        RefreshAvailability();
    }
    partial void OnForceCpuOcrChanged(bool value) => RefreshAvailability();
    partial void OnCpuThreadsChanged(int value) => RefreshAvailability();
    private void OcrCpuSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaddleOcrCpuConfig.Mode) && PaddleOcrCpu.Mode == PaddleOcrCpuMode.Custom)
            IsOcrAdvancedExpanded = true;
        OnPropertyChanged(nameof(IsOcrCpuCustom));
        OnPropertyChanged(nameof(IsOcrCpuConfigured)); OnPropertyChanged(nameof(OcrCpuModeHint));
        RefreshAvailability();
    }
    private void RuntimeSettingsChanged(object? sender, EventArgs e)
    {
        if (!_taskStatusTimer.Dispatcher.CheckAccess())
        {
            _taskStatusTimer.Dispatcher.BeginInvoke(() => RuntimeSettingsChanged(sender, e));
            return;
        }
        if (_disposed) return;
        OnPropertyChanged(nameof(CurrentRuntimeText)); OnPropertyChanged(nameof(CurrentEngineName));
        OnPropertyChanged(nameof(CurrentOcrText));
        OnPropertyChanged(nameof(CurrentValidationText));
        OnPropertyChanged(nameof(HasPendingChanges)); OnPropertyChanged(nameof(PendingText));
        foreach (var card in Engines)
        {
            card.IsCurrent = card.Engine == _runtime.Current.Engine;
            card.HasFailure = Config.LastFailedEngine == card.Engine && !string.IsNullOrWhiteSpace(Config.LastValidationError)
                && !(card.IsCurrent && _runtime.IsCurrentValidated);
            if (card.IsCurrent && card.IsInstalled)
            {
                card.StatusText = card.IsCpu ? "内置 · 无需安装" : (_runtime.IsCurrentValidated ? "模型验证通过" : "已生效 · 未验证");
                if (!card.HasFailure) card.Details = "运行库已就绪。\n\n运行库目录：\n" + card.RuntimeDirectory;
            }
            else if (card.HasFailure)
            {
                card.StatusText = "上次验证未通过";
                card.Details = Config.LastValidationError + "\n\n运行库目录：\n" + card.RuntimeDirectory;
            }
            else if (card.IsInstalled && string.IsNullOrWhiteSpace(card.ApplyBlockReason))
                card.StatusText = Config.ValidatedEngines.Contains(card.Engine) ? "此前验证通过" : "已安装 · 尚未验证";
        }
        RefreshAvailability();
    }
    partial void OnSelectedEngineChanged(InferenceEngineCardViewModel? value)
    {
        foreach (var card in Engines) card.IsSelected = card == value;
        RefreshAvailability();
    }
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract)); RefreshAvailability();
    }
    private void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanBenchmark)); OnPropertyChanged(nameof(CanBenchmarkTree));
        OnPropertyChanged(nameof(ApplyHint)); OnPropertyChanged(nameof(ApplyButtonText)); OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(BenchmarkHint));
        OnPropertyChanged(nameof(OcrCpuPreview)); OnPropertyChanged(nameof(OcrCpuApplicability));
    }

    [RelayCommand]
    private async Task ShowEngineDetailsAsync(InferenceEngineCardViewModel card)
        => await ThemedMessageBox.ShowAsync($"{card.StatusText}\n\n{card.Details}\n\n{card.DeviceMessage}",
            $"{card.Name} · 运行库信息", MessageBoxButton.OK,
            card.HasFailure ? ThemedMessageBox.MessageBoxIcon.Warning : ThemedMessageBox.MessageBoxIcon.Information);

    [RelayCommand]
    private async Task ShowErrorAsync() => await ThemedMessageBox.ShowAsync(ErrorDetails, "操作详情", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error);

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(token => RefreshCoreAsync(token));

    private async Task RefreshCoreAsync(CancellationToken token, InferenceEngineCardViewModel? updatedCard = null)
    {
        ProgressText = "正在检测运行库和设备…";
        // 下载完成只刷新变更的引擎，避免其他运行库的探测拖住“应用并重启”。手动检测优先处理所选卡片。
        var cards = updatedCard != null ? new[] { updatedCard } : Engines.OrderByDescending(card => card == SelectedEngine).ToArray();
        foreach (var card in cards)
        {
            var installation = _installer.Inspect(card.Engine);
            card.RuntimeDirectory = installation.Directory;
            card.IsInstalled = installation.State == InferenceInstallationState.Installed;
            card.ApplyBlockReason = installation.State switch
            {
                InferenceInstallationState.Missing => "请先下载运行库，再应用设置",
                InferenceInstallationState.MissingDependencies => "运行库已安装，仍需点击“补齐配套依赖”",
                _ => "正在识别支持的设备…"
            };
            card.CanDownload = installation.State == InferenceInstallationState.Missing && !card.IsCpu;
            card.CanCompleteDependencies = installation.State == InferenceInstallationState.MissingDependencies;
            card.IsCurrent = card.Engine == _runtime.Current.Engine;
            card.HasFailure = Config.LastFailedEngine == card.Engine && Config.LastValidationError.Length > 0
                && !(card.IsCurrent && _runtime.IsCurrentValidated);
            card.Details = installation.Description + "\n\n运行库目录：\n" + installation.Directory;
            card.StatusText = installation.State == InferenceInstallationState.Missing ? "DLL 未安装" : "配套依赖不完整";
            if (card.HasFailure)
            {
                card.StatusText = "上次验证未通过";
                card.Details += "\n\n上次验证结果（历史记录）：\n" + Config.LastValidationError;
            }
            else if (card.IsCurrent && card.IsInstalled)
                card.StatusText = card.IsCpu ? "内置 · 无需安装" : (_runtime.IsCurrentValidated ? "模型验证通过" : "已生效 · 未验证");
            else if (card.IsInstalled)
                card.StatusText = Config.ValidatedEngines.Contains(card.Engine) ? "此前验证通过" : "已安装 · 尚未验证";
            var preferred = card.SelectedDevice?.Id ?? card.Settings.DeviceId;
            try
            {
                var found = await Task.Run(() => _devices.GetDevices(card.Engine, installation.Directory), token);
                card.UpdateDevices(found, preferred);
                if (card.IsInstalled) card.ApplyBlockReason = found.Count == 0 ? "未发现支持的设备，请查看卡片详情" : "";
                card.DeviceMessage = found.Count == 0 ? (card.IsInstalled ? "未发现支持的设备，请查看详情。" : "安装完成后将自动识别设备。") : $"已发现 {found.Count} 个支持的设备";
                if (card.IsInstalled && found.Count == 0) card.StatusText = "未发现支持的设备";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                card.UpdateDevices([], preferred);
                card.ApplyBlockReason = "设备探测失败，请查看卡片详情";
                card.HasFailure = true;
                card.StatusText = "设备探测失败";
                card.DeviceMessage = "请查看详情，检查设备驱动和运行库。";
                card.Details += "\n\n设备探测错误：\n" + e;
            }
        }
        OnPropertyChanged(nameof(HasTreeImage)); OnPropertyChanged(nameof(TreeImageText)); OnPropertyChanged(nameof(TreeBenchmarkEmptyText));
        OnPropertyChanged(nameof(ValidationHint)); OnPropertyChanged(nameof(PendingText));
        ProgressText = "运行库与设备信息已更新。";
        await Task.Run(() => _installer.CleanupInstalledDownloadsAsync(token), token);
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!CanApply || SelectedEngine == null) return;
        await RunAsync(async token =>
        {
            ProgressText = "正在保存推理设置…";
            if (HasDraftChanges) _runtime.Apply(SelectedEngine.Engine, SelectedEngine.Settings, ForceCpuOcr, CpuThreads, PaddleOcrCpu.ToOptions());
            ProgressText = "设置已保存，等待重启生效。";
            await OfferRestartAsync();
        });
    }

    [RelayCommand]
    private async Task UseCpuAsync()
    {
        if (IsBusy) return;
        SelectedEngine = Engines.First(card => card.IsCpu);
        ForceCpuOcr = true; CpuThreads = 0;
        var defaults = new PaddleOcrCpuOptions().Normalize();
        PaddleOcrCpu.Mode = defaults.Mode;
        PaddleOcrCpu.DetectionThreads = defaults.DetectionThreads;
        PaddleOcrCpu.RecognitionThreads = defaults.RecognitionThreads;
        PaddleOcrCpu.InterOpThreads = defaults.InterOpThreads;
        PaddleOcrCpu.ParallelExecution = defaults.ParallelExecution;
        PaddleOcrCpu.AllowSpinning = defaults.AllowSpinning;
        await ApplyAsync();
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        if (!IsBusy && HasPendingChanges) await RunAsync(_ => OfferRestartAsync());
    }

    private async Task OfferRestartAsync()
    {
        if (await _interaction.ConfirmRestartAsync(PendingText, ValidationHint, CurrentEngineName, !_leases.IsAvailable)
            && !_lifetime.IsCancellationRequested) _interaction.Restart();
    }

    [RelayCommand]
    private async Task DownloadRuntimeAsync(InferenceEngineCardViewModel card) => await DownloadAsync(card, false);
    [RelayCommand]
    private async Task CompleteDependenciesAsync(InferenceEngineCardViewModel card) => await DownloadAsync(card, true);

    private async Task DownloadAsync(InferenceEngineCardViewModel card, bool dependencies)
    {
        if (IsBusy) return;
        SelectedEngine = card;
        await RunAsync(async token =>
        {
            ProgressText = "正在获取下载清单…";
            var packages = await _installer.GetDownloadPlanAsync(card.Engine, dependencies, token);
            if (packages.Count == 0) { await RefreshCoreAsync(token, card); ProgressText = "所需组件已经安装，设备信息已更新。"; return; }
            if (!await _interaction.ConfirmDownloadAsync(packages, dependencies)) return;
            token.ThrowIfCancellationRequested();
            var progress = new Progress<RuntimeDownloadProgress>(p =>
            {
                ProgressText = p.Message; ProgressValue = p.Percentage; IsProgressIndeterminate = p.Total <= 0;
            });
            await Task.Run(() => _installer.InstallAsync(card.Engine, packages, progress, token), token);
            // 安装、识别与按钮更新属于同一次操作，不能在 IsBusy 切换的间隙丢掉刷新。
            await RefreshCoreAsync(token, card);
            ProgressText = card.IsInstalled && card.SelectedDevice != null
                ? $"{card.Name} 已就绪，点击“应用并重启”即可使用。"
                : card.ApplyBlockReason;
        });
    }

    [RelayCommand] private async Task BenchmarkOcrAsync() => await BenchmarkAsync(InferenceBenchmarkScene.Ocr);
    [RelayCommand] private async Task BenchmarkTreeAsync() => await BenchmarkAsync(InferenceBenchmarkScene.DomainTree);
    private async Task BenchmarkAsync(InferenceBenchmarkScene scene)
    {
        if (!CanBenchmark || (scene == InferenceBenchmarkScene.DomainTree && !HasTreeImage)) return;
        await RunAsync(async token =>
        {
            var progress = new Progress<InferenceBenchmarkProgress>(p =>
            {
                ProgressText = $"{p.Scene} · {p.Completed}/{p.Total}";
                ProgressValue = 100d * p.Completed / p.Total;
                IsProgressIndeterminate = false;
            });
            var result = await _diagnostics.BenchmarkAsync(scene, _factory.ForCurrentRuntime(), progress, token);
            foreach (var previous in Results.Where(r => r.Scene == result.Scene).ToArray()) Results.Remove(previous);
            Results.Add(result);
            OnPropertyChanged(nameof(OcrResult)); OnPropertyChanged(nameof(TreeResult));
            OnPropertyChanged(nameof(HasOcrResult)); OnPropertyChanged(nameof(HasTreeResult));
            ProgressText = "测速完成，结果来自当前生效配置。";
        });
    }

    private Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _disposed) return Task.CompletedTask;
        IsBusy = true; ErrorDetails = ""; ProgressValue = 0; IsProgressIndeterminate = true;
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        return _operation = ExecuteAsync(action, _operationCancellation);
    }
    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationTokenSource cancellation)
    {
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { ProgressText = "操作已取消，已释放测试资源；下载进度保留供下次续传。"; }
        catch (Exception e) { ProgressText = "操作失败：" + e.Message; ErrorDetails = e.ToString(); }
        finally
        {
            _operationCancellation = null; cancellation.Dispose(); IsBusy = false; IsProgressIndeterminate = false;
        }
    }
    [RelayCommand] private void Cancel() { _operationCancellation?.Cancel(); ProgressText = "正在取消，等待当前操作安全结束…"; }

    [RelayCommand]
    private void OpenRuntimeFolder()
    {
        try
        {
            Directory.CreateDirectory(_installer.PluginsDirectory);
            using var process = Process.Start("explorer.exe", _installer.PluginsDirectory);
        }
        catch (Exception e) { ThemedMessageBox.Show($"无法打开运行库目录：{e.Message}", "打开目录失败"); }
    }
    [RelayCommand]
    public void OnOpenCacheFolder()
    {
        Directory.CreateDirectory(ResolvedInferenceRuntime.ModelCacheDirectory);
        using var process = Process.Start("explorer.exe", ResolvedInferenceRuntime.ModelCacheDirectory);
    }

    public async Task StopAsync() { _lifetime.Cancel(); await _operation; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _taskStatusTimer.Stop();
        _runtime.SettingsChanged -= RuntimeSettingsChanged;
        PaddleOcrCpu.PropertyChanged -= OcrCpuSettingsChanged;
        foreach (var card in Engines)
        {
            card.PropertyChanged -= CardChanged;
            card.Settings.PropertyChanged -= CardSettingsChanged;
        }
        _lifetime.Dispose();
    }
}
