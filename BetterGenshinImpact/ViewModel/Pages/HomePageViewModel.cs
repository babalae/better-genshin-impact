using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Markdown;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.System;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ViewModel, IDisposable
{
    private bool _disposed;
    [ObservableProperty] private IEnumerable<EnumItem<CaptureModes>> _modeNames = EnumExtensions.ToEnumItems<CaptureModes>();

    [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

    [ObservableProperty] private bool _taskDispatcherEnabled = false;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartTriggerCommand))]
    private bool _startButtonEnabled = true;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StopTriggerCommand))]
    private bool _stopButtonEnabled = true;

    public AllConfig Config { get; set; }

    public bool IsChildSessionEntryVisible => InstanceBootstrap.Current.Context.IsRoot;

    private MaskWindow? _maskWindow;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly TaskTriggerDispatcher _taskDispatcher;
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();
    private readonly IBannerImageService _bannerImageService;
    private CancellationTokenSource? _bannerDownloadCancellationTokenSource;

    // 记录上次使用原神的句柄
    private IntPtr _hWnd;
    private readonly GenshinHdrRestartStateStore _hdrRestartStateStore = new();

    [ObservableProperty] private InferenceDeviceType[] _inferenceDeviceTypes = Enum.GetValues<InferenceDeviceType>();

    [ObservableProperty] private ImageSource _bannerImageSource;

    private const string DefaultBannerImagePath = "pack://application:,,,/Resources/Images/banner.jpg";
    private readonly string _customBannerImagePath = Global.Absolute("User/Images/custom_banner.jpg");
    [ObservableProperty]
    private bool _isCustomNetworkBanner = false;
    private readonly ChildSessionService _childSessionService;

    /// <summary>
    /// 初始化 <c>HomePageViewModel</c> 的新实例。
    /// </summary>
    public HomePageViewModel(
        IConfigService configService,
        TaskTriggerDispatcher taskTriggerDispatcher,
        ChildSessionService childSessionService,
        IBannerImageService bannerImageService)
    {
        _taskDispatcher = taskTriggerDispatcher;
        _childSessionService = childSessionService;
        _bannerImageService = bannerImageService;
        Config = configService.Get();
        ReadGameInstallPath();
        InitializeBannerImage();


        // WindowsGraphicsCapture 只支持 Win10 18362 及以上的版本 (Windows 10 version 1903 or later)
        // https://github.com/babalae/better-genshin-impact/issues/394
        if (!OsVersionHelper.IsWindows10_1903_OrGreater)
        {
            // 两种 Windows Graphics Capture 模式具有相同的最低系统版本要求。
            _modeNames = _modeNames.Where(x =>
                    x.EnumName != CaptureModes.WindowsGraphicsCapture.ToString() &&
                    x.EnumName != CaptureModes.WindowsGraphicsCaptureHdr.ToString())
                .ToList();

            // DirectML 是在 Windows 10 版本 1903 和 Windows SDK 的相应版本中引入的。
            // https://learn.microsoft.com/zh-cn/windows/ai/directml/dml
            _inferenceDeviceTypes = _inferenceDeviceTypes
                .Where(x => x != InferenceDeviceType.GpuDirectMl)
                .ToArray();
        }

        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "Close")
            {
                OnClosed();
            }
            else if (msg.PropertyName == "SwitchTriggerStatus")
            {
                if (_taskDispatcherEnabled)
                {
                    OnStopTrigger();
                }
                else
                {
                    _ = OnStartTriggerAsync();
                }
            }
        });
    }

    private bool _autoRun = true;

    [RelayCommand]
    private void OpenChildSessionWindow()
    {
        _childSessionService.ShowWindow();
    }

    [RelayCommand]
    private void OnLoaded()
    {
        // OnTest();

        // 组件首次加载时运行一次。
        if (!_autoRun)
        {
            return;
        }

        _autoRun = false;

        // 只对纯 "start" 参数自动启动截图器
        // startOneDragon、--startGroups 等由各自流程中的 StartGameTask 处理
        HandleActivation(CommandLineOptions.Instance);
    }

    public void HandleActivation(CommandLineOptions commandLineOptions)
    {
        if (commandLineOptions.Action == CommandLineAction.Start)
        {
            _ = OnStartTriggerAsync();
        }

        // TODO: 多实例独立任务选择面板入口预留。
        // 后续在此判断可用子实例，并由选择面板决定 task.* 请求的目标实例。
    }

    private void OnClosed()
    {
        CancelBannerDownload();
        OnStopTrigger();
        // 等待任务结束
        _maskWindow?.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OnClosed();
        _taskDispatcher.UiTaskStopTickEvent -= OnUiTaskStopTick;
        _taskDispatcher.UiTaskStartTickEvent -= OnUiTaskStartTick;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _mouseKeyMonitor.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private async Task OnCaptureModeDropDownChanged()
    {
        // 启动的情况下重启
        if (TaskDispatcherEnabled)
        {
            _logger.LogInformation("► 切换捕获模式至[{Mode}]，截图器自动重启...", Config.CaptureMode);
            OnStopTrigger();
            await OnStartTriggerAsync();
        }
    }

    // [RelayCommand]
    // private void OnInferenceDeviceTypeDropDownChanged(string value)
    // {
    // }

    /// <summary>
    /// 处理 <c>OnStartCaptureTest</c> 对应的事件或状态更新。
    /// </summary>
    [RelayCommand]
    private void OnStartCaptureTest()
    {
        var picker = new PickerWindow(true);

        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                var captureWindow = new CaptureTestWindow();
                try
                {
                    captureWindow.StartCapture(hWnd, GetCaptureMode());
                    captureWindow.Show();
                }
                catch (Exception e)
                {
                    captureWindow.Close();
                    _logger.LogError(e, "测试截图器启动失败");
                    ThemedMessageBox.Error($"测试截图器启动失败：{e.GetBaseException().Message}");
                }
            }
            else
            {
                ThemedMessageBox.Error("选择的窗体句柄为空");
            }
        }
    }

    /// <summary>
    /// 处理 <c>OnManualPickWindow</c> 对应的事件或状态更新。
    /// </summary>
    [RelayCommand]
    private async Task OnManualPickWindow()
    {
        var picker = new PickerWindow();
        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                _hWnd = hWnd;
                var captureMode = GetCaptureMode();
                var target = ClassifyCaptureTarget(hWnd);
                if (target.Status == GenshinCaptureTargetStatus.Unavailable)
                {
                    _logger.LogError(target.Error, "无法读取手动选择窗口的进程身份");
                    await ShowGenshinEditionUnknownAsync();
                    return;
                }

                // 手动选择器允许任意已确认的非桌面目标；只有国服/国际服客户端才触碰 HDR 注册表。
                if (target.Status == GenshinCaptureTargetStatus.Desktop &&
                    !await DisableGenshinHdrIfNeededAsync(captureMode, hWnd, target.Edition))
                {
                    return;
                }

                Start(hWnd, captureMode);
            }
            else
            {
                ThemedMessageBox.Error("选择的窗体句柄为空！");
            }
        }
    }

    [RelayCommand]
    private async Task OpenDisplayAdvancedGraphicsSettingsAsync()
    {
        // ms-settings:display
        // ms-settings:display-advancedgraphics
        // ms-settings:display-advancedgraphics-default
        await Launcher.LaunchUriAsync(new Uri("ms-settings:display-advancedgraphics"));
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    /// <summary>
    /// 处理 <c>OnStartTriggerAsync</c> 对应的事件或状态更新。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    public async Task OnStartTriggerAsync()
    {
        // 先把字符串配置归一化为有效枚举，后续 HDR 策略与实际启动共用同一个结果。
        var captureMode = GetCaptureMode();
        var hWnd = SystemControl.FindGenshinImpactHandle();
        var configuredEdition = ResolveConfiguredGenshinEdition();
        var initialTarget = hWnd != IntPtr.Zero
            ? ClassifyCaptureTarget(hWnd)
            : configuredEdition == GenshinGameEdition.Unknown
                ? GenshinCaptureTargetClassification.NonRegistry()
                : GenshinCaptureTargetClassification.Desktop(configuredEdition);
        if (initialTarget.Status == GenshinCaptureTargetStatus.Unavailable)
        {
            _logger.LogError(initialTarget.Error, "无法读取自动选择游戏窗口的进程身份");
            await ShowGenshinEditionUnknownAsync();
            return;
        }

        var edition = initialTarget.Edition;
        if (initialTarget.Status == GenshinCaptureTargetStatus.Desktop &&
            !await DisableGenshinHdrIfNeededAsync(captureMode, hWnd, edition))
        {
            return;
        }

        if (hWnd == IntPtr.Zero)
        {
            if (Config.GenshinStartConfig.LinkedStartEnabled)
            {
                if (string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
                {
                    await ThemedMessageBox.ErrorAsync("没有找到原神的安装路径");
                    return;
                }

                hWnd = await SystemControl.StartFromLocalAsync(Config.GenshinStartConfig.InstallPath);
                if (hWnd != IntPtr.Zero)
                {
                    TaskContext.Instance().LinkedStartGenshinTime = DateTime.Now; // 标识关联启动原神的时间
                    // StartFromLocalAsync 可能返回启动前已存在但当时尚无主窗口的旧进程；必须按最终 PID 再检查一次。
                    var finalTarget = ClassifyCaptureTarget(hWnd);
                    if (finalTarget.Status == GenshinCaptureTargetStatus.Unavailable)
                    {
                        _logger.LogError(finalTarget.Error, "无法读取关联启动后游戏窗口的进程身份");
                        await ShowGenshinEditionUnknownAsync();
                        return;
                    }

                    edition = finalTarget.Edition;
                    if (finalTarget.Status == GenshinCaptureTargetStatus.Desktop &&
                        !await DisableGenshinHdrIfNeededAsync(captureMode, hWnd, edition))
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (hWnd == IntPtr.Zero)
            {
                await ThemedMessageBox.ErrorAsync("未找到原神窗口，请先启动原神！");
                return;
            }
        }

        Start(hWnd, captureMode);
    }

    /// <summary>
    /// 执行 <c>DisableGenshinHdrIfNeededAsync</c> 对应的处理逻辑。
    /// </summary>
    private async Task<bool> DisableGenshinHdrIfNeededAsync(
        CaptureModes captureMode,
        IntPtr runningGameHandle,
        GenshinGameEdition edition)
    {
        if (captureMode == CaptureModes.WindowsGraphicsCaptureHdr || edition == GenshinGameEdition.Unknown)
        {
            return true;
        }

        var registryTarget = GenshinHdrRegistryHelper.GetHdrRegistryFullValuePath(edition);
        if (registryTarget is null)
        {
            _logger.LogError("无法确定 {Edition} 对应的原神 HDR 注册表目标", edition);
            await ShowHdrRegistryFailureAsync("无法确定当前游戏版本对应的 HDR 注册表项。");
            return false;
        }

        var policyLockResult = await _hdrRestartStateStore.TryAcquirePolicyLockAsync();
        if (!policyLockResult.Success)
        {
            _logger.LogError(policyLockResult.Error, "获取原神 HDR 跨进程策略锁失败");
            await ShowHdrRestartStateFailureAsync();
            return false;
        }

        using var policyLock = policyLockResult.LockHandle!;

        var processId = GetProcessId(runningGameHandle);
        var restartCheck = _hdrRestartStateStore.CheckAndPrune(
            processId,
            edition,
            registryTarget);
        if (restartCheck.Status == GenshinHdrRestartCheckStatus.StateUnavailable)
        {
            _logger.LogError(restartCheck.Error, "读取或清理原神 HDR 待重启状态失败");
            policyLock.Dispose();
            await ShowHdrRestartStateFailureAsync();
            return false;
        }

        if (restartCheck.Status == GenshinHdrRestartCheckStatus.RestartRequired)
        {
            var registryRequiresManualAction = false;
            if (Config.GenshinStartConfig.AutoDisableGenshinHdrEnabled)
            {
                // marker 可能在注册表写入前因 BetterGI 异常退出而留下；重试写 0，但旧游戏仍必须重启。
                var retryPreparationResult = default(GenshinHdrRestartStateWriteResult);
                var retryResult = GenshinHdrRegistryHelper.TryDisableHdr(
                    edition,
                    target =>
                    {
                        // 即使当前拦截来自旧 Applied 屏障，再次从 1 写 0 也必须建立新代次。
                        retryPreparationResult = _hdrRestartStateStore.TryPrepareRegistryChange(edition, target);
                        return retryPreparationResult.Success;
                    });
                registryRequiresManualAction = IsHdrRegistryFailure(retryResult.Status);
                if (registryRequiresManualAction)
                {
                    _logger.LogError(
                        retryResult.Error ?? retryPreparationResult.Error,
                        "重试关闭原神 HDR 注册表失败，目标：{RegistryTarget}",
                        registryTarget);
                }
                else if (!TryCompleteHdrRegistryChange(edition, registryTarget))
                {
                    policyLock.Dispose();
                    await ShowHdrRestartStateFailureAsync();
                    return false;
                }
            }
            else
            {
                var readResult = GenshinHdrRegistryHelper.GetHdrState(edition);
                if (readResult.State is
                    GenshinHdrRegistryValueState.Disabled or GenshinHdrRegistryValueState.NotConfigured)
                {
                    // 用户已手动关闭时提交 Pending 代次；当前旧进程仍需重启，后续新进程才能放行。
                    if (!TryCompleteHdrRegistryChange(edition, registryTarget))
                    {
                        policyLock.Dispose();
                        await ShowHdrRestartStateFailureAsync();
                        return false;
                    }
                }
                else
                {
                    registryRequiresManualAction = true;
                    if (readResult.Error is not null)
                    {
                        _logger.LogError(readResult.Error, "读取原神 HDR 注册表状态失败");
                    }
                }
            }

            policyLock.Dispose();
            await ShowHdrRestartRequiredAsync(registryRequiresManualAction);
            return false;
        }

        if (!Config.GenshinStartConfig.AutoDisableGenshinHdrEnabled)
        {
            return true;
        }

        var processIdentityResult = _hdrRestartStateStore.ReadProcessIdentity(processId);
        var markerWriteResult = default(GenshinHdrRestartStateWriteResult);
        Func<string, bool> prepareBeforeWrite = target =>
        {
            if (runningGameHandle == IntPtr.Zero)
            {
                // 即使尚无可见窗口，也要先写版本级 Pending 代次，覆盖隐藏窗口和同版本多进程。
                markerWriteResult = _hdrRestartStateStore.TryPrepareRegistryChange(edition, target);
                return markerWriteResult.Success;
            }

            if (processIdentityResult.Status != GenshinProcessIdentityReadStatus.Found)
            {
                return false;
            }

            // 注册表写 0 前必须先同步落盘；失败时 helper 不会执行注册表写入。
            markerWriteResult = _hdrRestartStateStore.TryMarkRestartRequired(
                processIdentityResult.Identity,
                edition,
                target);
            return markerWriteResult.Success;
        };

        // 只查询并修改当前运行或即将关联启动的版本，不能触碰另一版本的 HDR 状态。
        var disableResult = GenshinHdrRegistryHelper.TryDisableHdr(edition, prepareBeforeWrite);
        switch (disableResult.Status)
        {
            case GenshinHdrDisableStatus.NotConfigured:
            case GenshinHdrDisableStatus.AlreadyDisabled:
                if (!TryCompleteHdrRegistryChange(edition, registryTarget))
                {
                    policyLock.Dispose();
                    await ShowHdrRestartStateFailureAsync();
                    return false;
                }

                // 防御并发实例在本次初查后提交新屏障；策略锁内复核后才可放行。
                var finalCheck = _hdrRestartStateStore.CheckAndPrune(processId, edition, registryTarget);
                if (finalCheck.Status == GenshinHdrRestartCheckStatus.StateUnavailable)
                {
                    _logger.LogError(finalCheck.Error, "最终复核原神 HDR 待重启状态失败");
                    policyLock.Dispose();
                    await ShowHdrRestartStateFailureAsync();
                    return false;
                }

                if (finalCheck.Status == GenshinHdrRestartCheckStatus.RestartRequired)
                {
                    policyLock.Dispose();
                    await ShowHdrRestartRequiredAsync(registryRequiresManualAction: false);
                    return false;
                }

                return true;
            case GenshinHdrDisableStatus.Disabled:
                if (!TryCompleteHdrRegistryChange(edition, registryTarget))
                {
                    policyLock.Dispose();
                    await ShowHdrRestartStateFailureAsync();
                    return false;
                }

                _logger.LogWarning(
                    "检测到原神 HDR 已开启并已自动关闭，注册表目标：{RegistryTarget}",
                    disableResult.RegistryTarget);
                if (runningGameHandle != IntPtr.Zero)
                {
                    // 注册表修改无法改变当前进程；持久化 marker 会跨 BetterGI 重启继续拦截。
                    policyLock.Dispose();
                    await ShowHdrRestartRequiredAsync(registryRequiresManualAction: false);
                    return false;
                }

                return true;
            case GenshinHdrDisableStatus.PreparationFailed:
                var preparationError = markerWriteResult.Error ??
                                       processIdentityResult.Error ??
                                       disableResult.Error;
                _logger.LogError(
                    preparationError,
                    "保存原神 HDR 待重启状态失败；为避免状态丢失，未修改注册表");
                policyLock.Dispose();
                await ShowHdrRestartStateFailureAsync();
                return false;
            case GenshinHdrDisableStatus.ReadFailed:
            case GenshinHdrDisableStatus.WriteFailed:
            case GenshinHdrDisableStatus.UnsupportedEdition:
            default:
                _logger.LogError(
                    disableResult.Error,
                    "读取或关闭原神 HDR 注册表失败，目标：{RegistryTarget}",
                    registryTarget);
                policyLock.Dispose();
                await ShowHdrRegistryFailureAsync(
                    disableResult.Error?.GetBaseException().Message ?? "未知错误");
                return false;
        }
    }

    /// <summary>
    /// 解析并返回 <c>ResolveConfiguredGenshinEdition</c> 对应的结果。
    /// </summary>
    private GenshinGameEdition ResolveConfiguredGenshinEdition()
    {
        var installPath = Config.GenshinStartConfig.InstallPath;
        return Config.GenshinStartConfig.LinkedStartEnabled &&
               File.Exists(installPath) &&
               GenshinHdrRegistryHelper.TryResolveEditionFromExecutablePath(
                   installPath,
                   out var configuredEdition)
            ? configuredEdition
            : GenshinGameEdition.Unknown;
    }

    /// <summary>
    /// 识别并返回 <c>ClassifyCaptureTarget</c> 对应的结果。
    /// </summary>
    private static GenshinCaptureTargetClassification ClassifyCaptureTarget(IntPtr hWnd)
    {
        using var process = SystemControl.GetProcessByHandle(hWnd);
        if (process is null)
        {
            return GenshinCaptureTargetClassification.Unavailable(
                new InvalidOperationException("无法根据窗口句柄取得进程。"));
        }

        try
        {
            return GenshinHdrRegistryHelper.TryResolveEditionFromProcessName(
                process.ProcessName,
                out var edition)
                ? GenshinCaptureTargetClassification.Desktop(edition)
                : GenshinCaptureTargetClassification.NonRegistry();
        }
        catch (Exception e)
        {
            return GenshinCaptureTargetClassification.Unavailable(e);
        }
    }

    private enum GenshinCaptureTargetStatus
    {
        Desktop,
        NonRegistry,
        Unavailable,
    }

    private readonly record struct GenshinCaptureTargetClassification(
        GenshinCaptureTargetStatus Status,
        GenshinGameEdition Edition = GenshinGameEdition.Unknown,
        Exception? Error = null)
    {
        /// <summary>
        /// 执行 <c>Desktop</c> 对应的处理逻辑。
        /// </summary>
        public static GenshinCaptureTargetClassification Desktop(GenshinGameEdition edition) =>
            edition == GenshinGameEdition.Unknown
                ? Unavailable(new InvalidOperationException("无法确认配置的桌面客户端版本。"))
                : new(GenshinCaptureTargetStatus.Desktop, edition);

        /// <summary>
        /// 执行 <c>NonRegistry</c> 对应的处理逻辑。
        /// </summary>
        public static GenshinCaptureTargetClassification NonRegistry() =>
            new(GenshinCaptureTargetStatus.NonRegistry);

        /// <summary>
        /// 执行 <c>Unavailable</c> 对应的处理逻辑。
        /// </summary>
        public static GenshinCaptureTargetClassification Unavailable(Exception error) =>
            new(GenshinCaptureTargetStatus.Unavailable, Error: error);
    }

    /// <summary>
    /// 获取 <c>GetProcessId</c> 对应的数据。
    /// </summary>
    private static uint GetProcessId(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return 0;
        }

        _ = User32.GetWindowThreadProcessId(hWnd, out var processId);
        return processId;
    }

    /// <summary>
    /// 判断 <c>IsHdrRegistryFailure</c> 所描述的条件是否成立。
    /// </summary>
    private static bool IsHdrRegistryFailure(GenshinHdrDisableStatus status)
    {
        return status is GenshinHdrDisableStatus.PreparationFailed or
            GenshinHdrDisableStatus.ReadFailed or
            GenshinHdrDisableStatus.WriteFailed or
            GenshinHdrDisableStatus.UnsupportedEdition;
    }

    /// <summary>
    /// 尝试执行 <c>TryCompleteHdrRegistryChange</c> 对应的操作。
    /// </summary>
    private bool TryCompleteHdrRegistryChange(
        GenshinGameEdition edition,
        string registryTarget)
    {
        var result = _hdrRestartStateStore.TryCompleteRegistryChange(
            edition,
            registryTarget,
            DateTime.UtcNow.Ticks);
        if (result.Success)
        {
            return true;
        }

        _logger.LogError(result.Error, "提交原神 HDR 版本变更状态失败");
        return false;
    }

    /// <summary>
    /// 执行 <c>ShowHdrRestartRequiredAsync</c> 对应的处理逻辑。
    /// </summary>
    private static async Task ShowHdrRestartRequiredAsync(bool registryRequiresManualAction)
    {
        var message = registryRequiresManualAction
            ? "当前游戏仍在使用修改前的 HDR 状态，且 HDR 注册表尚未确认关闭。请手动关闭原神 HDR 并重启游戏后再启动 BetterGI。"
            : "当前游戏仍在使用修改前的 HDR 状态。请重启游戏后再启动 BetterGI，以确保截图颜色正确。";
        await ThemedMessageBox.WarningAsync(message);
    }

    /// <summary>
    /// 执行 <c>ShowHdrRestartStateFailureAsync</c> 对应的处理逻辑。
    /// </summary>
    private static async Task ShowHdrRestartStateFailureAsync()
    {
        await ThemedMessageBox.ErrorAsync(
            "无法读取或保存原神 HDR 待重启状态。为避免 BetterGI 重启后错误放行旧游戏进程，本次未修改 HDR 设置，也未启动 SDR 捕获。请检查 LocalAppData/BetterGI/State 目录权限后重试。");
    }

    /// <summary>
    /// 执行 <c>ShowHdrRegistryFailureAsync</c> 对应的处理逻辑。
    /// </summary>
    private static async Task ShowHdrRegistryFailureAsync(string error)
    {
        await ThemedMessageBox.ErrorAsync(
            $"无法读取或关闭原神 HDR 设置：{error}\n为避免 SDR 截图过曝或泛白，本次未启动捕获。请手动关闭原神 HDR 后重试。");
    }

    /// <summary>
    /// 执行 <c>ShowGenshinEditionUnknownAsync</c> 对应的处理逻辑。
    /// </summary>
    private static async Task ShowGenshinEditionUnknownAsync()
    {
        await ThemedMessageBox.ErrorAsync(
            "已找到游戏窗口，但无法确认它属于国服还是国际服。为避免跳过对应版本的 HDR 安全检查，本次未启动捕获。请确认使用官方桌面客户端后重试。");
    }

    /// <summary>
    /// 启动当前组件或任务的处理流程。
    /// </summary>
    private void Start(IntPtr hWnd, CaptureModes? requestedCaptureMode = null)
    {
        Debug.WriteLine($"原神启动句柄{hWnd}");
        lock (this)
        {
            if (Config.TriggerInterval <= 0)
            {
                ThemedMessageBox.Error("触发器触发频率必须大于0");
                return;
            }

            if (!TaskDispatcherEnabled)
            {
                _hWnd = hWnd;
                try
                {
                    _taskDispatcher.Start(
                        hWnd,
                        requestedCaptureMode ?? GetCaptureMode(),
                        Config.TriggerInterval);
                }
                catch (Exception e)
                {
                    _taskDispatcher.Stop();
                    // TaskContext.Init 已在创建捕获器前将上下文标记为已初始化；启动失败时
                    // TaskDispatcherEnabled 仍为 false，常规 Stop() 不会再次进入清理分支，必须在此回滚。
                    var taskContext = TaskContext.Instance();
                    taskContext.IsInitialized = false;
                    taskContext.GameHandle = IntPtr.Zero;
                    taskContext.LinkedStartGenshinTime = DateTime.MinValue;
                    taskContext.CaptureColorMode = CaptureColorMode.Sdr;
                    _hWnd = IntPtr.Zero;
                    _logger.LogError(e, "截图器启动失败");
                    ThemedMessageBox.Error($"截图器启动失败：{e.GetBaseException().Message}");
                    return;
                }
                _taskDispatcher.UiTaskStopTickEvent -= OnUiTaskStopTick;
                _taskDispatcher.UiTaskStartTickEvent -= OnUiTaskStartTick;
                _taskDispatcher.UiTaskStopTickEvent += OnUiTaskStopTick;
                _taskDispatcher.UiTaskStartTickEvent += OnUiTaskStartTick;
                _maskWindow ??= new MaskWindow();
                _maskWindow.Show();
                MaskWindow.Instance().RefreshPosition();
                App.GetService<CustomHtmlMaskService>()?.ShowIfEnabled();
                _mouseKeyMonitor.Subscribe(hWnd);
                TaskDispatcherEnabled = true;
            }
        }
    }

    /// <summary>
    /// 获取 <c>GetCaptureMode</c> 对应的数据。
    /// </summary>
    private CaptureModes GetCaptureMode()
    {
        if (Config.CaptureMode.TryToCaptureMode(out var mode))
        {
            // 持久化配置可能来自更高版本系统，启动前仍需再次验证能力而不只过滤下拉列表。
            if (OsVersionHelper.IsWindows10_1903_OrGreater ||
                mode is not (CaptureModes.WindowsGraphicsCapture or CaptureModes.WindowsGraphicsCaptureHdr))
            {
                return mode;
            }
        }

        TaskContext.Instance().Config.CaptureMode = CaptureModes.BitBlt.ToString();
        return CaptureModes.BitBlt;
    }

    private bool CanStopTrigger() => StopButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStopTrigger))]
    private void OnStopTrigger()
    {
        Stop();
    }

    private void Stop()
    {
        lock (this)
        {
            if (TaskDispatcherEnabled)
            {
                CancellationContext.Instance.Cancel(); // 取消独立任务的运行
                _taskDispatcher.Stop();
                if (_maskWindow != null && _maskWindow.IsExist())
                {
                    _maskWindow?.Hide();
                }
                else
                {
                    _maskWindow?.Close();
                    _maskWindow = null;
                }

                TaskDispatcherEnabled = false;
                _mouseKeyMonitor.Unsubscribe();
                TaskContext.Instance().IsInitialized = false;
            }
        }
    }

    private void OnUiTaskStopTick(object? sender, EventArgs e)
    {
        UIDispatcherHelper.Invoke(Stop);
    }

    private void OnUiTaskStartTick(object? sender, EventArgs e)
    {
        UIDispatcherHelper.Invoke(() => Start(_hWnd));
    }

    [RelayCommand]
    public void OnGoToWikiUrl()
    {
        Process.Start(new ProcessStartInfo("https://www.bettergi.com/doc.html") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OnTest()
    {
        // var result = OcrFactory.Paddle.OcrResult(new Mat(@"E:\HuiTask\更好的原神\自动秘境\自动战斗\队伍识别\x2.png", ImreadModes.Grayscale));
        // foreach (var region in result.Regions)
        // {
        //     Debug.WriteLine($"{region.Text}");
        // }

        //try
        //{
        //    YoloV8 predictor = new(Global.Absolute("Assets\\Model\\Fish\\bgi_fish.onnx"));
        //    using var memoryStream = new MemoryStream();
        //    new Bitmap(Global.Absolute("test_yolo.png")).Save(memoryStream, ImageFormat.Bmp);
        //    memoryStream.Seek(0, SeekOrigin.Begin);
        //    var result = predictor.Detect(memoryStream);
        //    ThemedMessageBox.Show(JsonSerializer.Serialize(result));
        //}
        //catch (Exception e)
        //{
        //    ThemedMessageBox.Show(e.StackTrace);
        //}

        // Mat tar = new(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\selected.png", ImreadModes.Grayscale);
        //  var mask = OpenCvCommonHelper.CreateMask(tar, new Scalar(0, 0, 0));
        // var src = new Mat(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\Clip_20240309_135839.png", ImreadModes.Grayscale);
        // var src2 = src.Clone();
        // var res = MatchTemplateHelper.MatchOnePicForOnePic(src, mask);
        // // 把结果画到原图上
        // foreach (var t in res)
        // {
        //     Cv2.Rectangle(src2, t, new Scalar(0, 0, 255));
        // }
        //
        // Cv2.ImWrite(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\x1.png", src2);
    }

    [RelayCommand]
    public async Task SelectInstallPathAsync()
    {
        await Task.Run(() =>
        {
            // 弹出选择文件夹对话框
            var dialog = new Ookii.Dialogs.Wpf.VistaOpenFileDialog
            {
                Filter = "原神|YuanShen.exe;GenshinImpact.exe|可执行文件|*.exe|所有文件|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                Config.GenshinStartConfig.InstallPath = path;
            }
        });
    }

    private void ReadGameInstallPath()
    {
        // 检查用户是否配置了原神安装目录，如果没有，尝试从注册表中读取
        if (string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
        {
            Task.Run(async () =>
            {
                var p1 = RegistryGameLocator.GetDefaultGameInstallPath();
                if (!string.IsNullOrEmpty(p1))
                {
                    Config.GenshinStartConfig.InstallPath = p1;
                }
                else
                {
                    var p2 = await UnityLogGameLocator.LocateSingleGamePathAsync();
                    if (!string.IsNullOrEmpty(p2))
                    {
                        Config.GenshinStartConfig.InstallPath = p2;
                    }
                }
            });
        }
    }

    //[RelayCommand]
    //private void OnOpenGameCommandLineDocument()
    //{
    //    string md = File.ReadAllText(Global.Absolute(@"Assets\Strings\gicli.md"), Encoding.UTF8);

    //    md = WebUtility.HtmlEncode(md);
    //    string md2html = File.ReadAllText(Global.Absolute(@"Assets\Strings\md2html.html"), Encoding.UTF8);
    //    var html = md2html.Replace("{{content}}", md);

    //    WebpageWindow win = new()
    //    {
    //        Title = "启动参数说明",
    //        Width = 800,
    //        Height = 600,
    //        Owner = Application.Current.MainWindow,
    //        WindowStartupLocation = WindowStartupLocation.CenterOwner
    //    };

    //    win.NavigateToHtml(html);
    //    win.ShowDialog();
    //}

    [RelayCommand]
    private void OnOpenGameCommandLineDocument()
    {
        // 创建 MarkdownView 来显示内容
        var markdownView = new MarkdownView
        {
            FilePath = Global.Absolute(@"Assets\Strings\gicli.md"),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 12),
            LinkNavigationMode = MarkdownLinkNavigationMode.SystemDefault
        };

        // 创建两行的 Grid 容器
        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TitleBar 行
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容行

        // 创建 TitleBar
        var titleBar = new TitleBar
        {
            Title = "启动参数说明",
            Icon = new ImageIcon
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(@"pack://application:,,,/Resources/Images/logo.png", UriKind.Absolute))
            },
        };
        System.Windows.Controls.Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // 将 MarkdownView 添加到第二行
        System.Windows.Controls.Grid.SetRow(markdownView, 1);
        grid.Children.Add(markdownView);

        // 创建 FluentWindow 来显示内容
        var dialogWindow = new FluentWindow
        {
            Content = grid,
            Width = 800,
            Height = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Manual,
            WindowBackdropType = WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        dialogWindow.ShowDialog();
    }

    [RelayCommand]
    public void OnOpenHardwareAccelerationSettings()
    {
        var dialogWindow = new FluentWindow
        {
            Title = "硬件加速设置",
            Content = new HardwareAccelerationView(new HardwareAccelerationViewModel()),
            Width = 800,
            Height = 600,
            MinWidth = 800,
            MaxWidth = 800,
            MinHeight = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Auto,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        var result = dialogWindow.ShowDialog();
    }

    #region 背景图片管理

    private void InitializeBannerImage()
    {
        LoadFallbackBannerImage();
        try
        {
            // 检查url文件
            var url = _bannerImageService.ReadConfiguredUrl();
            // 判断是否有内容
            if (!string.IsNullOrEmpty(url))
            {
                _ = DownloadAndApplyBannerImageAsync(url, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化背景图片失败，使用现有背景图片");
        }
    }

    private void LoadFallbackBannerImage()
    {
        IsCustomNetworkBanner = false;
        try
        {
            // 检查是否存在自定义图片
            if (File.Exists(_customBannerImagePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                BannerImageSource = bitmap;
                _logger.LogInformation("已加载自定义背景图片");
            }
            else
            {
                // 使用默认图片
                BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
                _logger.LogInformation("已加载默认背景图片");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化背景图片失败，使用默认图片");
            BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
        }
    }

    private async Task DownloadAndApplyBannerImageAsync(string url, bool showErrorToast)
    {
        CancelBannerDownload();
        var cancellationTokenSource = new CancellationTokenSource();
        _bannerDownloadCancellationTokenSource = cancellationTokenSource;

        try
        {
            if (!await _bannerImageService.DownloadAndSaveAsync(url, cancellationTokenSource.Token))
            {
                return;
            }

            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            RefreshBannerImage();
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            // 新操作替代旧下载或恢复默认图片时无需提示。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载自定义背景图片url失败，使用现有背景图片");
            if (showErrorToast)
            {
                Toast.Error($"下载自定义背景图片url失败：{ex.Message}");
            }

            LoadFallbackBannerImage();
        }
        finally
        {
            if (ReferenceEquals(_bannerDownloadCancellationTokenSource, cancellationTokenSource))
            {
                _bannerDownloadCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private void RefreshBannerImage()
    {
        IsCustomNetworkBanner = true;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(Path.GetFullPath(_bannerImageService.NetworkImagePath));
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        BannerImageSource = bitmap;
        _logger.LogInformation("已加载自定义背景图片url");
    }

    private void CancelBannerDownload()
    {
        var cancellationTokenSource = _bannerDownloadCancellationTokenSource;
        _bannerDownloadCancellationTokenSource = null;
        cancellationTokenSource?.Cancel();
        _bannerImageService.InvalidatePendingDownloads();
    }

    [RelayCommand]
    private void ChangeBannerImage()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ResetBannerImage();
                
                var selectedFile = openFileDialog.FileName;

                // 确保目标目录存在
                var directory = Path.GetDirectoryName(_customBannerImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 复制图片到自定义路径
                File.Copy(selectedFile, _customBannerImagePath, true);

                // 更新UI
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
                bitmap.EndInit();
                BannerImageSource = bitmap;
                Toast.Success("背景图片更换成功！");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更换背景图片失败");
            Toast.Error($"更换背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ChangeWebBannerImage()
    {
        try
        {
            CancelBannerDownload();
            // 打开窗口
            var vm = App.GetService<WebImageInputViewModel>();
            var webImageInput = new WebImageInput(vm!);
            webImageInput.Owner = Application.Current.MainWindow;
            vm!.SubmitCompleted += RefreshBannerImage;
            try
            {
                webImageInput.ShowDialog();
            }
            finally
            {
                vm.SubmitCompleted -= RefreshBannerImage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更换背景图片失败");
            Toast.Error($"更换背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshWebBannerImageAsync()
    {
        try
        {
            // 检查url文件
            var url = _bannerImageService.ReadConfiguredUrl();
            // 判断是否有内容
            if (!string.IsNullOrEmpty(url))
            {
                await DownloadAndApplyBannerImageAsync(url, true);
            }
            else
            {
                CancelBannerDownload();
                LoadFallbackBannerImage();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新背景图片失败");
            Toast.Error($"刷新背景图片失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetBannerImage()
    {
        try
        {
            CancelBannerDownload();
            // 获取自定义图片的完整路径
            var customImageFullPath = Path.GetFullPath(_customBannerImagePath);
            _logger.LogInformation("尝试恢复默认背景图片，自定义图片路径: {CustomPath}", customImageFullPath);

            // 先切换到默认图片，释放自定义图片的文件锁
            var defaultBitmap = new BitmapImage();
            defaultBitmap.BeginInit();
            defaultBitmap.UriSource = new Uri(DefaultBannerImagePath, UriKind.Absolute);
            defaultBitmap.CacheOption = BitmapCacheOption.OnLoad;
            defaultBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
            defaultBitmap.EndInit();
            BannerImageSource = defaultBitmap;
            
            if (File.Exists(customImageFullPath))
            {
                File.Delete(customImageFullPath);
            }
            _bannerImageService.ResetNetworkImage();
            IsCustomNetworkBanner = false;
            Toast.Success("已恢复为默认背景图片！");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复默认背景图片失败");
            Toast.Warning("已恢复为默认背景图片！但清除自定义图片失败，请手动删除文件。");
        }
    }

    #endregion
}
