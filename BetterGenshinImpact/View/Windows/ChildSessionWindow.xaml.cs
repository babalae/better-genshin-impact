using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.View.Controls.ChildSession;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using FormsScreen = System.Windows.Forms.Screen;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

public partial class ChildSessionWindow : FluentWindow
{
    /// <summary>
    /// 浮层工具栏在鼠标离开后多久收起。
    /// 给得比较宽：全屏下鼠标常常要顺手点一下屏幕边缘的 UI，收太快会反复唤出。
    /// </summary>
    private static readonly TimeSpan FullscreenBarHideDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 鼠标进入窗口顶部这个高度内就唤出浮层工具栏。
    /// </summary>
    private const double FullscreenBarTriggerHeight = 6d;

    /// <summary>
    /// 浮层工具栏距屏幕顶端的位置。
    /// </summary>
    private const double FullscreenBarTopOffset = 8;

    /// <summary>
    /// 浮层工具栏自身高度：Border 上下内边距 6×2 + 按钮 30 + 边框 1×2。
    /// 改工具栏样式时要一起调，通知条的位置依赖它。
    /// </summary>
    private const double FullscreenBarHeight = 44;

    /// <summary>
    /// 通知条在正常模式下距窗口顶端的位置（让开 32px 的标题栏）。
    /// </summary>
    private const double NormalNotificationOffset = 32;

    /// <summary>
    /// 通知条在全屏模式下距屏幕顶端的位置。
    ///
    /// 必须让开浮层工具栏，否则两者重叠：切观看/游戏模式的提示正好在工具栏唤出时
    /// 弹出来，会被工具栏压住。窗口模式下不能再锚标题栏——全屏时标题栏是
    /// Collapsed，以它为锚点的 Popup 会失去定位基准。
    /// </summary>
    private const double FullscreenNotificationOffset = FullscreenBarTopOffset + FullscreenBarHeight + 10;


    public static readonly DependencyProperty IsFullscreenProperty = DependencyProperty.Register(
        nameof(IsFullscreen),
        typeof(bool),
        typeof(ChildSessionWindow),
        new PropertyMetadata(false));

    private readonly ChildSessionWindowViewModel _viewModel;
    private readonly DispatcherTimer _fullscreenBarTimer;
    private readonly ILogger _logger = ChildSessionDiagnosticsLog.Instance;
    private bool _closeRequestInProgress;

    private WindowStyle _styleBeforeFullscreen;
    private ResizeMode _resizeBeforeFullscreen;
    private WindowCornerPreference _cornerPreferenceBeforeFullscreen;
    private WindowBackdropType _backdropTypeBeforeFullscreen;
    private Rect _boundsBeforeFullscreen;

    /// <summary>进入全屏时的 DPI。_boundsBeforeFullscreen 里的 DIP 是按它算的，
    /// 退出时若窗口已经落到另一块不同 DPI 的屏上，换算必须用这个而不是当前 DPI。</summary>
    private DpiScale _dpiBeforeFullscreen = new(1, 1);

    /// <summary>进入全屏前的窗口背景与合成背景。全屏会把两者都压成纯黑，
    /// 退出时必须显式还原——原材质本来就是 None 时，还原 BackdropType 不会触发
    /// ApplyBackdrop，窗口会一直黑着。</summary>
    private Brush? _backgroundBeforeFullscreen;
    private Color? _compositeBackgroundBeforeFullscreen;
    private bool _topmostBeforeFullscreen;
    private double _minWidthBeforeFullscreen;
    private double _maxWidthBeforeFullscreen;
    private double _minHeightBeforeFullscreen;
    private double _maxHeightBeforeFullscreen;
    private bool _suppressStateChange;
    private DateTime _fullscreenBarShownAt;

    /// <summary>
    /// 凭据框轮询间隔。取 400ms：凭据框出现到被拉到前台最多差这一拍，
    /// 又不至于为了探测一个偶发窗口持续占用 CPU。
    /// </summary>
    private const int CredentialDialogPollMilliseconds = 400;

    private readonly object _credentialDialogWatchGate = new();
    private System.Threading.Timer? _credentialDialogWatchTimer;
    private IntPtr _windowHandle;
    private IntPtr _revealedCredentialDialog;

    /// <summary>
    /// 每次为凭据框放下置顶时递增。排队中的「恢复置顶」带着自己的令牌回来，
    /// 令牌对不上就说明期间又出现了新凭据框，这次恢复不能消费它的抑制标记。
    /// </summary>
    private int _topmostSuppressionToken;
    private bool _topmostSuppressedForCredential;

    /// <summary>
    /// 「正在把置顶放下去」的窗口期。此期间 OnWindowActivated 不能消费抑制标记：
    /// 它会在降置顶真正完成之前把标记清掉，之后这次降置顶就没人恢复了。
    /// </summary>
    private bool _topmostDemotionPending;
    private bool _credentialDialogWatchEnabled;

    /// <summary>
    /// 凭据框轮询的「全屏生命周期」代际号。每次进全屏自增，回调据此判断自己是否已过期，
    /// 防止退全屏→再进全屏后，旧代回调把新代状态搅乱。读写都走
    /// <see cref="_credentialDialogWatchGate"/>。
    /// </summary>
    private int _credentialDialogWatchGeneration;

    /// <summary>
    /// 窗口当前是否前台。UI 线程在 Activate/Deactivate 里更新，轮询线程读取——
    /// 读写都走 <see cref="_credentialDialogWatchGate"/>，保证线程间可见。
    /// </summary>
    private bool _windowIsActive;

    internal RdpActiveXHost RdpHost { get; } = new();

    internal bool AllowClose { get; set; }

    /// <summary>
    /// 服务释放前调用：把全屏期间自动切换到游戏鼠标的那一步还原。
    ///
    /// 关窗的常规路径也会做这件事（<c>OnClosed</c>），但那时服务已经标记 disposed，
    /// <c>SetInputMode</c> 会被 <c>ThrowIfDisposed</c> 挡住，自动切换的模式就留在配置里，
    /// 下次启动变成游戏鼠标模式。所以必须赶在标记之前做。
    /// </summary>
    internal void RestoreInputModeBeforeShutdown()
    {
        _viewModel.RestoreInputModeAfterFullscreen();
    }

    /// <summary>
    /// 当前是否处于全屏模式。
    /// </summary>
    public bool IsFullscreen
    {
        get => (bool)GetValue(IsFullscreenProperty);
        private set => SetValue(IsFullscreenProperty, value);
    }

    public ChildSessionWindow(ChildSessionWindowViewModel viewModel)
    {
        DataContext = _viewModel = viewModel;
        InitializeComponent();
        RdpFormsHost.Child = RdpHost;

        _fullscreenBarTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _fullscreenBarTimer.Tick += OnFullscreenBarTimerTick;

        // 隐藏窗口时一定退出全屏：全屏下画面被藏起来后，用户既看不到浮层按钮
        // （退出全屏和切换输入模式的唯一入口），也没法确认当前处于哪种模式——先退干净再隐藏。
        IsVisibleChanged += OnWindowIsVisibleChanged;

        // 最大化按钮就是全屏入口（没有菜单项），所以状态变化要接管。
        StateChanged += OnWindowStateChanged;

        // 全屏下失焦（切到别的程序、弹出凭据框）时临时放下置顶，焦点回来再恢复。
        Deactivated += OnWindowDeactivated;
        Activated += OnWindowActivated;

        // 「固定工具栏」是视图模型上的设置项，这里跟着它决定工具栏是否常驻。
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.TryApplySystemBackdrop(this);
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(OnWindowMessage);

        // 显示器分辨率/DPI 变化时，全屏窗口要跟着重新铺满新边界。全屏期间尺寸被
        // min==max 锁死，WPF 不会自动跟随，得在这里显式重算。
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose || Dispatcher.HasShutdownStarted)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnClosing(e);

        if (_closeRequestInProgress)
        {
            return;
        }

        if (!_viewModel.HasChildSession)
        {
            Hide();
            return;
        }

        var result = ThemedMessageBox.Show(
            "关闭会断开 RDP 并注销桌面分身，其中所有正在运行的软件都会被关闭，未保存的数据会丢失。是否继续？",
            "关闭 BetterGI 桌面分身",
            MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.No,
            this);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _closeRequestInProgress = true;
        _ = LogoffAndHideAsync();
    }

    private HwndSource? _windowSource;

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmExitSizeMove = 0x0232;

        // 拖动/缩放结束时校正一次位置：窗口顶边越过工作区顶部时，标题栏整条看不见，
        // 只能靠任务栏或 Alt+Space 找回来。放在 EXITSIZEMOVE 而不是 MOVE，
        // 避免和拖动过程抢位置；全屏窗口顶边本来就贴着显示器，不需要处理。
        if (message == WmExitSizeMove && !IsFullscreen)
        {
            EnsureTitleBarVisible();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 把窗口顶边压回所在显示器工作区的顶部，保证标题栏始终露得出来。
    ///
    /// 只处理「上边缘跑出屏幕」这一种：左右超出时系统会留一部分可见，用户还能拖回来。
    /// </summary>
    private void EnsureTitleBarVisible()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var dpi = VisualTreeHelper.GetDpi(this);
        var workAreaTop = screen.WorkingArea.Top / (dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1);

        if (Top < workAreaTop)
        {
            Top = workAreaTop;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        // 关窗时可能还停在全屏：输入模式要还原，否则「进全屏时被自动切成游戏鼠标」
        // 会留在配置里。
        _viewModel.RestoreInputModeAfterFullscreen();
        _fullscreenBarTimer.Stop();
        StopCredentialDialogWatch();
        _credentialDialogWatchTimer?.Dispose();
        _credentialDialogWatchTimer = null;

        // 真正关闭时解除两层订阅：旧 ViewModel 对单例服务的事件订阅，以及本窗口
        // 对旧 ViewModel 的 PropertyChanged。普通隐藏不经过这里（OnClosing 已取消），
        // 只有重建或退出时才会走到，此时新旧实例彻底分离。
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.DetachServiceEvents();

        RdpFormsHost.Child = null;
        RdpHost.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 最小化也会让 IsVisible 变 false，但那是用户主动最小化，全屏状态要保留，
        // 恢复后还是全屏。只有真正被 Hide() 藏起来时才退出全屏——否则用户看不到
        // 浮层按钮，也就没法退出全屏或切换输入模式。
        //
        // 判据用 Visibility 而不是 WindowState：全屏窗口被最小化后，Hide() 不会改
        // WindowState（还是 Minimized），只看 WindowState 会把「显式隐藏」当成
        // 「最小化」跳过退全屏，窗口就一直带着全屏状态和自动选的输入模式藏在后台。
        if (e.NewValue is false && IsFullscreen && Visibility != Visibility.Visible)
        {
            ExitFullscreen();
        }
    }

    private void OnFullscreenMinimizeClicked(object sender, RoutedEventArgs e)
    {
        // 与窗口模式的最小化一致：最小化到任务栏。全屏状态保持，恢复后仍是全屏。
        WindowState = WindowState.Minimized;
    }

    private void OnFullscreenRestoreClicked(object sender, RoutedEventArgs e)
    {
        ExitFullscreen();
    }

    /// <summary>
    /// 浮层上的固定按钮：固定后工具栏不再随鼠标离开自动收起，状态写进设置。
    ///
    /// 它与窗口置顶无关——置顶是窗口的总在最前，这里是这条浮层工具栏的显示策略。
    /// </summary>
    private void OnFullscreenTogglePinClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleFullscreenBarPinnedCommand.Execute(null);
    }

    private void UpdateFullscreenPinButton(bool isPinned)
    {
        FullscreenPinButton.Icon = new SymbolIcon
        {
            FontSize = 14,
            Symbol = isPinned ? SymbolRegular.Pin24 : SymbolRegular.PinOff24
        };
        FullscreenPinButton.ToolTip = isPinned ? "取消固定工具栏" : "固定工具栏";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChildSessionWindowViewModel.IsFullscreenBarPinned))
        {
            return;
        }

        var pinned = _viewModel.IsFullscreenBarPinned;
        UpdateFullscreenPinButton(pinned);

        if (IsFullscreen && pinned)
        {
            ShowFullscreenBar();
        }
    }

    private void OnFullscreenSwitchWindowClicked(object sender, RoutedEventArgs e)
    {
        _viewModel.SwitchWindowCommand.Execute(null);
    }

    private void OnFullscreenCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 窗口状态变化的入口：最大化就是「进入全屏游戏模式」，没有单独的菜单入口。
    ///
    /// 先回到 Normal 再按显示器边界摆位——最大化状态下的窗口压不住任务栏（任务栏是 topmost），
    /// 而且我们要连标题栏一起收起来。
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_suppressStateChange)
        {
            return;
        }

        if (WindowState == WindowState.Maximized && !IsFullscreen)
        {
            _suppressStateChange = true;
            try
            {
                WindowState = WindowState.Normal;
            }
            finally
            {
                _suppressStateChange = false;
            }

            EnterFullscreen();
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // 轮询线程恢复置顶前会看这个标记：窗口失焦后绝不能再被顶上去盖住别的程序。
        lock (_credentialDialogWatchGate)
        {
            _windowIsActive = false;
        }

        if (!IsFullscreen)
        {
            return;
        }

        // 全屏期间窗口失焦（切到别的程序、弹出凭据框等）时临时放下置顶：
        // 置顶窗口会盖住「Windows 安全中心」凭据框这类非置顶窗口，即使它抢到了
        // 前台焦点也露不出来。放下置顶后，非置顶的前台窗口就能显示。
        // 用 SetCurrentValue 而不是直接赋值，避免清掉 XAML 里的 OneWay 绑定。
        SetCurrentValue(TopmostProperty, false);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        lock (_credentialDialogWatchGate)
        {
            _windowIsActive = true;
        }

        if (!IsFullscreen)
        {
            return;
        }

        SetCurrentValue(TopmostProperty, true);

        // 凭据框场景里，置顶是用 Win32 直接放下的（当时 UI 线程被模态弹窗卡住，
        // WPF 的 Topmost 属性没变过，SetCurrentValue(true) 是 no-op）。
        // 焦点回到全屏窗时再显式把 WS_EX_TOPMOST 加回去，确保一定盖住任务栏。
        RestoreTopmostAfterCredentialDialog();
    }

    /// <summary>
    /// 全屏期间启动后台轮询，主动发现「Windows 安全中心」凭据框。
    ///
    /// 不能用 DispatcherTimer：凭据框是模态的，UI 线程会卡在它的消息循环里，
    /// DispatcherTimer 跟着停摆（这正是「启动过程中工具栏唤不出」的原因）。
    /// System.Threading.Timer 跑在线程池上，不依赖 UI 线程泵消息。
    /// </summary>
    private void StartCredentialDialogWatch()
    {
        _windowHandle = new WindowInteropHelper(this).Handle;

        lock (_credentialDialogWatchGate)
        {
            _revealedCredentialDialog = IntPtr.Zero;
            _topmostSuppressedForCredential = false;
            _topmostDemotionPending = false;
            _credentialDialogWatchEnabled = true;
            // 每次进全屏是新的一生：旧回调里可能还挂在 Timer 的 tick 上（Timer.Change
            // 不能打断正在执行的 tick），代际号让它们醒来后发现自己已经过期、不再改状态。
            _credentialDialogWatchGeneration++;
            _windowIsActive = IsActive;
        }

        if (_credentialDialogWatchTimer is null)
        {
            _credentialDialogWatchTimer = new System.Threading.Timer(
                OnCredentialDialogWatchTick,
                null,
                CredentialDialogPollMilliseconds,
                CredentialDialogPollMilliseconds);
        }
        else
        {
            _credentialDialogWatchTimer.Change(
                CredentialDialogPollMilliseconds,
                CredentialDialogPollMilliseconds);
        }
    }

    /// <summary>
    /// 停止凭据框轮询，并把可能还挂着「放下置顶」的状态收干净。
    /// </summary>
    private void StopCredentialDialogWatch()
    {
        _credentialDialogWatchTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        lock (_credentialDialogWatchGate)
        {
            _credentialDialogWatchEnabled = false;
        }

        RestoreTopmostAfterCredentialDialog();
    }

    /// <summary>
    /// 凭据框轮询回调（线程池线程）。只做 Win32 调用与字段读写，不碰任何 WPF 对象——
    /// WPF 对象只认创建它的 UI 线程。
    /// </summary>
    private void OnCredentialDialogWatchTick(object? state)
    {
        // 进回调先取自己这一代的编号：整个回调期间都用它判断「自己还活着」，
        // 防止退全屏→再进全屏后，旧代回调把新代的状态搅乱。
        int generation;
        lock (_credentialDialogWatchGate)
        {
            generation = _credentialDialogWatchGeneration;
        }

        try
        {
            IntPtr handle;

            // 令牌和「已揭示的对话框」都要在搜索之前取：Timer 回调可能重叠，
            // 先跑的这次搜索期间另一次可能已经为新凭据框降了置顶、记了新句柄，
            // 搜索完再读就会拿到别人的状态，从而把它们抹掉。
            int scanToken;
            var dialogAtScanStart = IntPtr.Zero;
            lock (_credentialDialogWatchGate)
            {
                if (!_credentialDialogWatchEnabled)
                {
                    return;
                }

                handle = _windowHandle;
                scanToken = _topmostSuppressionToken;
                dialogAtScanStart = _revealedCredentialDialog;
            }

            if (handle == IntPtr.Zero || !WindowNativeMethods.IsWindow(handle))
            {
                return;
            }

            var dialog = ChildSessionAutoLoginService.FindCredentialDialog();
            if (dialog != IntPtr.Zero)
            {
                lock (_credentialDialogWatchGate)
                {
                    if (!_credentialDialogWatchEnabled
                        || generation != _credentialDialogWatchGeneration
                        || dialog == _revealedCredentialDialog)
                    {
                        return;
                    }

                    _revealedCredentialDialog = dialog;
                }

                RevealCredentialDialog(handle, dialog, generation);
                return;
            }

            // 凭据框已经消失（用户提交/取消了），尝试恢复置顶。
            bool marshalRestore;
            var restoreToken = 0;
            lock (_credentialDialogWatchGate)
            {
                // 窗口不在前台时不动、也不清标记：等 OnWindowActivated 时由
                // RestoreTopmostAfterCredentialDialog 在 UI 线程重新检查后再恢复。
                marshalRestore = _topmostSuppressedForCredential
                                 && _credentialDialogWatchEnabled
                                 && generation == _credentialDialogWatchGeneration
                                 && _windowIsActive
                                 && scanToken == _topmostSuppressionToken;
                restoreToken = scanToken;

                // 只有「自己这一代」、且搜索期间没人动过这个标记时才清：
                // 跨代或并发的另一轮可能刚记录了一个新的凭据框，抹掉会让下一轮
                // 对它重复做前台操作。
                if (generation == _credentialDialogWatchGeneration
                    && _revealedCredentialDialog == dialogAtScanStart)
                {
                    _revealedCredentialDialog = IntPtr.Zero;
                }
            }

            if (marshalRestore)
            {
                // 恢复置顶必须回到 UI 线程做：在线程池里直接 SetWindowPos，用的是
                // 「查完状态就过期」的判断，UI 线程这期间可能又失焦/退全屏了。
                var token = restoreToken;
                _ = Dispatcher.BeginInvoke(new Action(() => RestoreTopmostAfterCredentialDialog(token)));
            }
        }
        catch (Exception exception)
        {
            // 轮询跑在后台线程上，任何异常都不能把进程带崩。这一轮没拉到，下一轮再试。
            _logger.LogWarning(exception, "全屏凭据框轮询失败，跳过本轮。");
        }
    }

    /// <summary>
    /// 把刚出现的凭据框拉到前台。必须先在 Win32 层放下自己的置顶，
    /// 否则凭据框作为非置顶窗口会一直被压在全屏窗下面。
    /// </summary>
    private void RevealCredentialDialog(IntPtr handle, IntPtr dialog, int generation)
    {
        // 先把「为凭据框放下置顶」的标记记下，再放下原生置顶：万一这期间退全屏/停轮询，
        // StopCredentialDialogWatch 的 RestoreTopmostAfterCredentialDialog 能看到标记、
        // 把原生置顶收回来，不会留成「标记还没记、置顶已经放下」的死态。
        lock (_credentialDialogWatchGate)
        {
            // 跨代际的旧回调不能碰新代的状态：这期间已经退全屏又进全屏了，
            // 它放下置顶会作用于新代窗口，留下没人恢复的降级。
            if (!_credentialDialogWatchEnabled || generation != _credentialDialogWatchGeneration)
            {
                return;
            }

            _topmostSuppressedForCredential = true;
            _topmostDemotionPending = true;
            _topmostSuppressionToken++;
        }

        if (!WindowNativeMethods.SetWindowTopmost(handle, false))
        {
            // 放下失败（窗口可能正在销毁）：回滚刚记的标记，下一轮再试。
            lock (_credentialDialogWatchGate)
            {
                if (generation == _credentialDialogWatchGeneration)
                {
                    _topmostSuppressedForCredential = false;
                    _topmostDemotionPending = false;
                    _revealedCredentialDialog = IntPtr.Zero;
                }
            }

            return;
        }

        // 放下置顶之后再看一眼监视状态：这中间可能已经退全屏、停了轮询，
        // 甚至退全屏后又进了全屏（代际变了）。任何「自己这一代已经结束」的情况，
        // 这次「放下」都成了没人恢复的残留，必须自己收回去。
        bool rollbackTopmost;
        lock (_credentialDialogWatchGate)
        {
            var stillCurrent = generation == _credentialDialogWatchGeneration;

            // 降置顶已完成，此后 OnWindowActivated 可以正常消费标记来恢复。
            // 但只能清「自己这一代」的 pending：跨代的旧回调不能把新代刚设的
            // pending 抹掉，否则新代的降置顶会没人跟踪。
            if (stillCurrent)
            {
                _topmostDemotionPending = false;
            }

            rollbackTopmost = !_credentialDialogWatchEnabled || !stillCurrent;
            if (rollbackTopmost && stillCurrent)
            {
                // 自己这一代已结束：清掉自己的恢复标记，让新代不受影响。
                _topmostSuppressedForCredential = false;
                _revealedCredentialDialog = IntPtr.Zero;
            }
        }

        if (rollbackTopmost)
        {
            // 不能在这里无条件置顶：这期间可能已经退出全屏，窗口本来就不该在顶层，
            // 强行置顶会让它退出全屏后仍浮在别的程序上面。交给 UI 线程按当前状态同步：
            // 还在全屏 → 仍置顶（前提是窗口在前台）；已退全屏 → 跟随 WPF 的 Topmost。
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                // 到真正执行时，新代可能又为凭据框降了置顶。复查当前抑制/进行中状态：
                // 正在降置顶、或已经为凭据框抑制了置顶，就绝不能再顶上去盖住新凭据框。
                bool suppressed;
                lock (_credentialDialogWatchGate)
                {
                    suppressed = _topmostSuppressedForCredential || _topmostDemotionPending;
                }

                if (suppressed)
                {
                    return;
                }

                if (IsFullscreen)
                {
                    if (_windowIsActive)
                    {
                        WindowNativeMethods.SetWindowTopmost(handle, true);
                    }
                }
                else
                {
                    WindowNativeMethods.SetWindowTopmost(handle, Topmost);
                }
            }));

            return;
        }

        // 激活凭据框之前，确认前台仍是「本次连接工作流」的窗口：
        // 发现弹窗后用户可能已经切到别的程序，这时强行 SW_RESTORE / SetForegroundWindow
        // 会把凭据框拽到那个程序上面、抢走焦点，打断用户正在做的事。
        // 允许前台是凭据框自己（可能是上一轮已经弹出来、被激活过的），或仍是我们。
        //
        // 只信 GetForegroundWindow 的实时结果，不再参考 _windowIsActive：那是 UI 线程
        // 的 Activate/Deactivate 回调维护的缓存，切窗后回调可能还没来得及跑，用它判断
        // 会放过「前台已经是别的程序」的情况。
        var foreground = WindowNativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != dialog && foreground != handle)
        {
            // 前台是别的程序：这次不抢焦点（会把凭据框拽到别人桌上）。
            // 但必须把「已揭示」标记撤回——它是在前台检查之前置位的，
            // 留着的话后续每一轮都会跳过这个句柄，用户切回我们时也看不到凭据框。
            lock (_credentialDialogWatchGate)
            {
                if (generation == _credentialDialogWatchGeneration
                    && _revealedCredentialDialog == dialog)
                {
                    _revealedCredentialDialog = IntPtr.Zero;
                }
            }

            return;
        }

        // 激活之前再确认一次代际：上面放下置顶、查前台都可能耗时，这期间用户完全
        // 可能退出又重进全屏——那时旧回调不该再把对话框拽到新全屏上面。
        lock (_credentialDialogWatchGate)
        {
            if (!_credentialDialogWatchEnabled || generation != _credentialDialogWatchGeneration)
            {
                return;
            }
        }

        // 顺序：先还原（防最小化）→ 提到本带顶部 → 抢前台焦点。
        // 我们进程此刻就是前台进程，SetForegroundWindow 通常能成功；
        // 即便被前台锁挡住，前两步已经让弹窗可见了。
        _ = WindowNativeMethods.ShowWindow(dialog, WindowNativeMethods.SwRestore);
        _ = WindowNativeMethods.BringWindowToTop(dialog);
        var raised = WindowNativeMethods.SetForegroundWindow(dialog);

        if (!raised)
        {
            // 前台锁挡住时标记要撤回，否则后续轮次会跳过这个句柄，
            // 用户看不到凭据框、也没法输入密码。下一轮再试。
            lock (_credentialDialogWatchGate)
            {
                if (generation == _credentialDialogWatchGeneration
                    && _revealedCredentialDialog == dialog)
                {
                    _revealedCredentialDialog = IntPtr.Zero;
                }
            }

            _logger.LogInformation("全屏：凭据框已放下置顶，但没能拿到前台焦点，下一轮重试。");
            return;
        }

        _logger.LogInformation("全屏：检测到凭据框，已放下置顶并把它拉到前台。");
    }

    /// <summary>
    /// 把「为凭据框放下置顶」的状态还原。只会在 UI 线程被调用：焦点回到全屏窗
    /// （<see cref="OnWindowActivated"/>）、退全屏收尾（<see cref="StopCredentialDialogWatch"/>）、
    /// 或凭据框消失后由轮询线程 <c>Dispatcher.BeginInvoke</c> 回来。
    /// 在这里统一重新检查「仍全屏 + 窗口是前台」，避免用过期判断改 Z 序。
    /// </summary>
    /// <param name="expectedToken">
    /// 排队时看到的抑制令牌。0 表示「按当前状态恢复」（焦点回来、退全屏等主动调用）；
    /// 非 0 时若令牌已变，说明期间又降过一次置顶，这次恢复必须让位给新凭据框。
    /// </param>
    private void RestoreTopmostAfterCredentialDialog(int expectedToken = 0)
    {
        bool shouldRestore;
        IntPtr handle;
        lock (_credentialDialogWatchGate)
        {
            // 仍在全屏时，只有窗口是前台才恢复（否则会把本窗顶上去盖住别的程序）；
            // 退出全屏时（IsFullscreen 已置 false）必须恢复原生置顶——凭据框场景里
            // 降的是原生 WS_EX_TOPMOST，WPF 的 Topmost 还是 true，不恢复的话后面
            // SetCurrentValue(Topmost, 原值) 是 no-op，窗口就停在非置顶状态。
            //
            // 降置顶还在进行中（RevealCredentialDialog 还没返回）时不能消费标记：
            // 它马上会把置顶真放下去，此刻清标记等于留下「已放下、无人恢复」的残留。
            shouldRestore = _topmostSuppressedForCredential
                            && !_topmostDemotionPending
                            && (expectedToken == 0 || expectedToken == _topmostSuppressionToken)
                            && (_windowIsActive || !IsFullscreen);
            if (shouldRestore)
            {
                _topmostSuppressedForCredential = false;
                _revealedCredentialDialog = IntPtr.Zero;
            }

            handle = _windowHandle;
        }

        if (shouldRestore && handle != IntPtr.Zero)
        {
            // 按当前意图恢复，而不是无条件置顶：全屏中应当置顶，退出全屏后
            // 要看 WPF 的 Topmost（可能本来就是 false）。
            WindowNativeMethods.SetWindowTopmost(handle, IsFullscreen || Topmost);
        }
    }

    /// <summary>
    /// 进入全屏：铺满当前显示器（含任务栏区域），隐藏标题栏。
    /// </summary>
    internal void EnterFullscreen()
    {
        if (IsFullscreen)
        {
            return;
        }

        _styleBeforeFullscreen = WindowStyle;
        _resizeBeforeFullscreen = ResizeMode;
        _cornerPreferenceBeforeFullscreen = WindowCornerPreference;
        _backdropTypeBeforeFullscreen = WindowBackdropType;
        _topmostBeforeFullscreen = Topmost;
        _boundsBeforeFullscreen = new Rect(Left, Top, ActualWidth, ActualHeight);
        _dpiBeforeFullscreen = VisualTreeHelper.GetDpi(this);
        _backgroundBeforeFullscreen = Background;
        _compositeBackgroundBeforeFullscreen =
            (PresentationSource.FromVisual(this) as HwndSource)?.CompositionTarget?.BackgroundColor;
        _minWidthBeforeFullscreen = MinWidth;
        _maxWidthBeforeFullscreen = MaxWidth;
        _minHeightBeforeFullscreen = MinHeight;
        _maxHeightBeforeFullscreen = MaxHeight;

        IsFullscreen = true;

        _suppressStateChange = true;
        try
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
        }
        finally
        {
            _suppressStateChange = false;
        }

        // 全屏必须是一整块不透明黑。FluentWindow 默认挂了三样会在全屏露馅的东西：
        // ① DWM 圆角（WindowCornerPreference=Round）→ 四个角是圆的；
        // ② 背景材质（Mica）会让窗口背景变透明 → 边缘 1px 能看到背后的窗口；
        // ③ 浅色主题下窗口背景是白的 → 缩放取整留 1px 缝时会露白边。
        // 这里把三者都压掉，退出时再逐项还原。
        WindowCornerPreference = WindowCornerPreference.DoNotRound;
        WindowBackdropType = WindowBackdropType.None;
        Background = Brushes.Black;
        if (PresentationSource.FromVisual(this) is HwndSource { CompositionTarget: HwndTarget target })
        {
            target.BackgroundColor = Colors.Black;
        }

        // 任务栏是 topmost 的，普通窗口压不住它，全屏期间必须临时置顶。
        // 用 SetCurrentValue 而不是直接赋值：XAML 里 Topmost 绑着 ViewModel 的置顶开关，
        // 直接赋值会清掉绑定，退出全屏后置顶按钮就失效了。
        SetCurrentValue(TopmostProperty, true);

        // 标题栏让位给画面。
        WindowTitleBar.Visibility = Visibility.Collapsed;

        ApplyFullscreenBounds();

        // 尺寸锁死是兜底：上面 WindowBackdropType=None 已经让 WindowChrome 按
        // NoResize 重算（缩放命中区归零），这里 min==max 再确保任何路径都缩不动，
        // 避免把「全屏」拖成别的尺寸却又停在 IsFullscreen 状态。
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;

        // 标题栏收起后行 0 高度归零，行 1 就是整个窗口。保持宽高比开启、且显示器
        // 不是 16:9 时，把 RDP 画面等比缩放进居中的 16:9 区域，四周留黑边。
        ApplyFullscreenScaling();

        _viewModel.ApplyInputModeForFullscreen();
        UpdateFullscreenPinButton(_viewModel.IsFullscreenBarPinned);
        NotificationPopup.VerticalOffset = FullscreenNotificationOffset;

        // 进全屏刚设完边界，ActualWidth 还是旧值，立刻定位工具栏会按旧宽度居中，
        // 分辨率变大时工具栏偏在一边。放到布局之后定位。
        ShowFullscreenBar();
        _ = Dispatcher.BeginInvoke(new Action(PositionFullscreenBar), DispatcherPriority.Loaded);
        _fullscreenBarTimer.Start();
        StartCredentialDialogWatch();
    }

    /// <summary>
    /// 退出全屏，还原进入之前的窗口状态。
    /// </summary>
    internal void ExitFullscreen()
    {
        if (!IsFullscreen)
        {
            return;
        }

        IsFullscreen = false;
        _fullscreenBarTimer.Stop();
        StopCredentialDialogWatch();
        HideFullscreenBar();
        NotificationPopup.VerticalOffset = NormalNotificationOffset;

        WindowStyle = _styleBeforeFullscreen;
        ResizeMode = _resizeBeforeFullscreen;
        WindowTitleBar.Visibility = Visibility.Visible;
        SetCurrentValue(TopmostProperty, _topmostBeforeFullscreen);

        // 还原 DWM 圆角与背景材质。WindowBackdropType 回到原值会重新 ApplyBackdrop，
        // 顺带把 Background/合成背景恢复成透明（Mica 需要），所以这里不用手动清 Background。
        WindowCornerPreference = _cornerPreferenceBeforeFullscreen;
        WindowBackdropType = _backdropTypeBeforeFullscreen;

        // 显式还原背景：原材质是 None 时上面这行不会重算背景，全屏压的黑会留下来。
        if (_backgroundBeforeFullscreen is not null)
        {
            Background = _backgroundBeforeFullscreen;
            _backgroundBeforeFullscreen = null;
        }

        if (_compositeBackgroundBeforeFullscreen is { } composite
            && PresentationSource.FromVisual(this) is HwndSource { CompositionTarget: HwndTarget restoredTarget })
        {
            restoredTarget.BackgroundColor = composite;
            _compositeBackgroundBeforeFullscreen = null;
        }

        // 交还给样式：普通窗口下这 3px 是边缘拖拽的命中区。
        RdpFormsHost.ClearValue(FrameworkElement.MarginProperty);

        // 先还掉尺寸锁，再恢复边界：否则下面的 Width/Height 会被全屏锁掉的 min/max 夹住。
        MinWidth = _minWidthBeforeFullscreen;
        MaxWidth = _maxWidthBeforeFullscreen;
        MinHeight = _minHeightBeforeFullscreen;
        MaxHeight = _maxHeightBeforeFullscreen;

        // 不还原成 Maximized：最大化在本窗口就代表全屏，还原成它会立刻又进全屏。
        _suppressStateChange = true;
        try
        {
            WindowState = WindowState.Normal;

            var restoredBounds = EnsureBoundsVisible(_boundsBeforeFullscreen);
            Left = restoredBounds.Left;
            Top = restoredBounds.Top;
            Width = restoredBounds.Width;
            Height = restoredBounds.Height;
        }
        finally
        {
            _suppressStateChange = false;
        }

        _viewModel.RestoreInputModeAfterFullscreen();

        // 全屏期间按分辨率一致性强制过缩放模式，退出后恢复用户自己的设置。
        // 这里失败不能中断下面还原置顶的逻辑，否则窗口会一直浮在最上层。
        try
        {
            RdpHost.SetSmartSizing(_viewModel.IsAdaptive);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "退出全屏时恢复缩放设置失败。");
        }

        // 收尾再同步一次原生置顶：上面的 SetCurrentValue(Topmost, 原值) 在值没变时
        // 不产生任何副作用，而原生 WS_EX_TOPMOST 可能已经被凭据框的置顶管理改过
        // （它降的是原生样式，WPF 这一层不知道），不显式同步就会「退出全屏后仍浮在最上层」。
        var finalHandle = new WindowInteropHelper(this).Handle;
        if (finalHandle != IntPtr.Zero)
        {
            WindowNativeMethods.SetWindowTopmost(finalHandle, Topmost);
        }
    }

    /// <summary>
    /// 退全屏恢复窗口位置前，把坐标夹到「当前还有显示器」的范围内。
    ///
    /// 进全屏后用户可能拔掉了副屏：<c>_boundsBeforeFullscreen</c> 还存着那块屏的坐标，
    /// 直接恢复会把窗口摆到屏幕外。这里只要标题栏仍落在某个工作区里就算可见。
    ///
    /// 注意坐标系：<paramref name="bounds"/> 是 WPF 的 DIP，<c>Screen.WorkingArea</c> 是
    /// 像素，两者必须换算到同一坐标再比较。
    /// </summary>
    private Rect EnsureBoundsVisible(Rect bounds)
    {
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            return bounds;
        }

        // bounds 是按「进全屏时那块屏」的 DPI 记下的 DIP；夹取在像素空间做，
        // 最后再按当前 DPI 换回 DIP 交给窗口。两个 DPI 混用会把窗口摆偏。
        var oldDpi = _dpiBeforeFullscreen;
        var nowDpi = VisualTreeHelper.GetDpi(this);

        const double minVisibleWidth = 120;
        const double minVisibleHeight = 32;
        var boundsPixels = new Rect(
            bounds.Left * oldDpi.DpiScaleX,
            bounds.Top * oldDpi.DpiScaleY,
            bounds.Width * oldDpi.DpiScaleX,
            bounds.Height * oldDpi.DpiScaleY);
        var titleBarPixels = new Rect(
            boundsPixels.Left,
            boundsPixels.Top,
            Math.Max(boundsPixels.Width, minVisibleWidth),
            minVisibleHeight);

        foreach (var screen in screens)
        {
            var area = screen.WorkingArea;
            var visible = Rect.Intersect(
                titleBarPixels,
                new Rect(area.Left, area.Top, area.Width, area.Height));
            if (visible.Width >= minVisibleWidth && visible.Height >= minVisibleHeight)
            {
                // 位置不用夹，但坐标空间必须换：bounds 是旧 DPI 下的 DIP，
                // 直接交给现在的窗口会被按新 DPI 解释，跨屏恢复会偏移。
                return new Rect(
                    boundsPixels.Left / nowDpi.DpiScaleX,
                    boundsPixels.Top / nowDpi.DpiScaleY,
                    boundsPixels.Width / nowDpi.DpiScaleX,
                    boundsPixels.Height / nowDpi.DpiScaleY);
            }
        }

        // 没落在任何屏上：就近夹到最近的工作区，尺寸也收到工作区内（像素），
        // 最后再换回 DIP 返回。
        var target = screens
            .Select(screen => screen.WorkingArea)
            .OrderBy(area =>
            {
                var dx = titleBarPixels.Left < area.Left ? (double)area.Left - titleBarPixels.Left
                    : titleBarPixels.Left > area.Right ? (double)titleBarPixels.Left - area.Right : 0;
                var dy = titleBarPixels.Top < area.Top ? (double)area.Top - titleBarPixels.Top
                    : titleBarPixels.Top > area.Bottom ? (double)titleBarPixels.Top - area.Bottom : 0;
                return dx * dx + dy * dy;
            })
            .First();

        var widthPixels = Math.Min(boundsPixels.Width, target.Width);
        var heightPixels = Math.Min(boundsPixels.Height, target.Height);
        var leftPixels = Math.Clamp(boundsPixels.Left, target.Left, target.Right - widthPixels);
        var topPixels = Math.Clamp(boundsPixels.Top, target.Top, target.Bottom - heightPixels);

        return new Rect(
            leftPixels / nowDpi.DpiScaleX,
            topPixels / nowDpi.DpiScaleY,
            widthPixels / nowDpi.DpiScaleX,
            heightPixels / nowDpi.DpiScaleY);
    }

    /// <summary>
    /// 显示器分辨率或 DPI 变化：全屏时重新铺满新边界。非全屏时不处理——
    /// 普通窗口的位置尺寸归 WindowResizeBehavior 和用户自己管。
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ReapplyFullscreenLayout();
    }

    /// <summary>
    /// 缩放（DPI）变化也会让全屏的物理尺寸失配，但走的是 WM_DPICHANGED，不在
    /// WM_DISPLAYCHANGE 那条路径上，得单独覆盖。
    ///
    /// 注意：WPF 在调这个 override 之后还会用 HwndTarget 应用 SuggestedRect 来收尾缩放，
    /// 此刻同步重算边界会被随后那次 resize 覆盖掉。所以把重算推迟到 dispatcher，
    /// 等 WPF 自己的 DPI 处理跑完再动。
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _ = Dispatcher.BeginInvoke(new Action(ReapplyFullscreenLayout), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 全屏时按当前显示器与 DPI 重算边界、重新锁尺寸、重算黑边与工具栏位置。
    /// 幂等：WM_DISPLAYCHANGE 与 WM_DPICHANGED 同一次变化先后到达也只算一遍。
    /// </summary>
    private void ReapplyFullscreenLayout()
    {
        if (!IsFullscreen)
        {
            return;
        }

        // 先把上一次全屏的尺寸锁放开：WPF 解析尺寸时先套 min/max，再套 Width/Height，
        // 锁还挂在旧值上会把新边界夹回旧尺寸，窗口就停在旧大小。
        MinWidth = 0;
        MaxWidth = double.PositiveInfinity;
        MinHeight = 0;
        MaxHeight = double.PositiveInfinity;

        ApplyFullscreenBounds();

        // 重新锁尺寸（min==max 跟随新边界），并重算缩放模式与黑边布局。
        // 换屏/改分辨率时显示器可能从 1920x1080 变成别的，1:1 就不再合适了，必须重算。
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        ApplyFullscreenScaling();

        // 工具栏定位依赖 ActualWidth，而它要等这次布局跑完才更新（刚设完 Width 时
        // 还是旧值）。放到布局之后再做，否则分辨率缩小后工具栏会停在旧宽度的位置、
        // 跑到窗口外面去。
        _ = Dispatcher.BeginInvoke(new Action(PositionFullscreenBar), DispatcherPriority.Loaded);
    }

    private void ApplyFullscreenBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 用显示器物理边界而不是工作区：全屏要盖住任务栏。
        var screen = FormsScreen.FromHandle(handle);
        var bounds = screen.Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);

        Left = bounds.Left / dpi.DpiScaleX;
        Top = bounds.Top / dpi.DpiScaleY;
        Width = bounds.Width / dpi.DpiScaleX;
        Height = bounds.Height / dpi.DpiScaleY;
    }

    /// <summary>
    /// 全屏下按「保持宽高比」给 RDP 画面留黑边。
    ///
    /// 远端桌面是固定 1920×1080（16:9），窗口却铺满整块显示器；显示器不是 16:9 时，
    /// 自适应缩放会把画面拉伸变形。这里把 RdpFormsHost 缩到居中的 16:9 矩形里，
    /// 四周露出窗口的黑色背景当黑边——缩的是控件本身，所以 RDP 的鼠标坐标映射仍是
    /// 「远端 16:9 → 控件 16:9」的等比关系，不会错位。
    /// </summary>
    private void ApplyFullscreenLetterbox()
    {
        if (!_viewModel.KeepAspectRatio)
        {
            RdpFormsHost.Margin = new Thickness(0);
            return;
        }

        var areaWidth = Width;
        var areaHeight = Height;
        if (areaWidth <= 0 || areaHeight <= 0)
        {
            return;
        }

        const double targetRatio = 16d / 9d;
        var areaRatio = areaWidth / areaHeight;
        if (Math.Abs(areaRatio - targetRatio) < 0.001)
        {
            RdpFormsHost.Margin = new Thickness(0);
            return;
        }

        if (areaRatio > targetRatio)
        {
            // 显示器比 16:9 宽：左右留黑边。
            var contentWidth = areaHeight * targetRatio;
            var sideInset = (areaWidth - contentWidth) / 2;
            RdpFormsHost.Margin = new Thickness(sideInset, 0, sideInset, 0);
        }
        else
        {
            // 显示器比 16:9 高（如 16:10、4:3）：上下留黑边。
            var contentHeight = areaWidth / targetRatio;
            var verticalInset = (areaHeight - contentHeight) / 2;
            RdpFormsHost.Margin = new Thickness(0, verticalInset, 0, verticalInset);
        }
    }

    /// <summary>
    /// 全屏时决定缩放模式与黑边，三条规则按顺序判定：
    ///
    /// 1. 显示器正好等于分身分辨率（1920×1080）→ 1:1 铺满，不留黑边；两项设置都不看。
    /// 2. 显示器长宽都 ≥ 分身分辨率，且用户关掉了「自适应缩放」→ 1:1 居中，四周留黑边；
    ///    此时不看「保持宽高比」，因为 1:1 的尺寸是固定的，留边是必然结果。
    /// 3. 其余（显示器比分身小，或用户开着自适应）→ 自适应缩放；是否留黑边由「保持宽高比」决定。
    ///
    /// 只改 RDP 控件，不动用户配置；退出全屏时再恢复用户设置。
    /// </summary>
    private void ApplyFullscreenScaling()
    {
        // 缩放设置走 COM，失败不能连累全屏本身：此时标题栏已收起、尺寸已锁，
        // 让异常冒出去会让用户既没有普通窗口控件、也没有全屏工具栏。
        try
        {
            ApplyFullscreenScalingCore();
        }
        catch (Exception exception)
        {
            // 缩放没设成功时不要再清黑边：清了只是让 1:1 的画面填满控件（被裁掉），
            // 反而看不出「缩放其实没生效」。保持现有布局，日志里留线索。
            _logger.LogWarning(exception, "全屏缩放设置失败，保持原有布局。");
        }
    }

    private void ApplyFullscreenScalingCore()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var bounds = FormsScreen.FromHandle(handle).Bounds;
        var desktopWidth = ChildSessionService.DefaultDesktopSize.Width;
        var desktopHeight = ChildSessionService.DefaultDesktopSize.Height;

        // 规则 1：分辨率一致，1:1 铺满。
        if (bounds.Width == desktopWidth && bounds.Height == desktopHeight)
        {
            RdpHost.SetSmartSizing(false);
            RdpFormsHost.Margin = new Thickness(0);
            return;
        }

        // 规则 2：显示器足够大且用户选了 1:1，则按原始像素居中，四周留黑边。
        var displayCoversDesktop = bounds.Width >= desktopWidth && bounds.Height >= desktopHeight;
        if (displayCoversDesktop && !_viewModel.IsAdaptive)
        {
            RdpHost.SetSmartSizing(false);

            // 控件的物理尺寸要正好等于分身分辨率，剩下的部分靠 Margin 均分到四周。
            var dpi = VisualTreeHelper.GetDpi(this);
            var contentWidth = desktopWidth / dpi.DpiScaleX;
            var contentHeight = desktopHeight / dpi.DpiScaleY;
            var insetX = Math.Max(0d, (Width - contentWidth) / 2);
            var insetY = Math.Max(0d, (Height - contentHeight) / 2);
            RdpFormsHost.Margin = new Thickness(insetX, insetY, insetX, insetY);
            return;
        }

        // 规则 3：自适应缩放，黑边看「保持宽高比」。
        RdpHost.SetSmartSizing(true);
        ApplyFullscreenLetterbox();
    }

    private void ShowFullscreenBar()
    {
        FullscreenBarPopup.IsOpen = true;
        _fullscreenBarShownAt = DateTime.UtcNow;
        PositionFullscreenBar();
    }

    private void HideFullscreenBar()
    {
        FullscreenBarPopup.IsOpen = false;
    }

    /// <summary>
    /// 浮层工具栏横向居中。Popup 是独立窗口，量不到父窗口的布局，只能手动摆。
    /// </summary>
    private void PositionFullscreenBar()
    {
        if (FullscreenBarPopup.Child is not FrameworkElement bar)
        {
            return;
        }

        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var barWidth = bar.DesiredSize.Width;
        FullscreenBarPopup.HorizontalOffset = Math.Max(0, (ActualWidth - barWidth) / 2);
        FullscreenBarPopup.VerticalOffset = FullscreenBarTopOffset;
    }

    /// <summary>
    /// 轮询光标位置来决定浮层工具栏的显隐。
    ///
    /// 不能靠窗口的 MouseMove：全屏时整个客户区是 RDP 控件的子窗口（WinForms），
    /// WPF 根本收不到鼠标消息。
    /// </summary>
    private void OnFullscreenBarTimerTick(object? sender, EventArgs e)
    {
        if (!IsFullscreen)
        {
            _fullscreenBarTimer.Stop();
            return;
        }

        // 窗口失活（切到别的程序）时不弹工具栏：光标还在屏幕顶边，弹出来会盖在
        // 别的程序上面——Popup 弹出本身就是 topmost，owner 置顶被放下也挡不住。
        if (!_windowIsActive)
        {
            if (FullscreenBarPopup.IsOpen)
            {
                HideFullscreenBar();
            }

            return;
        }

        // 固定状态下工具栏常驻，不再按光标位置收起。
        if (_viewModel.IsFullscreenBarPinned)
        {
            if (!FullscreenBarPopup.IsOpen)
            {
                ShowFullscreenBar();
            }

            return;
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        var local = PointFromScreen(new Point(cursor.X, cursor.Y));
        // 同时限制 X：双显示器顶边对齐时，光标在另一块屏的顶部移动，
        // 换算到本窗口的局部坐标后 Y 仍在触发区，但 X 已经跑到窗口外了。
        var nearTop = local.Y >= 0
                      && local.Y <= FullscreenBarTriggerHeight
                      && local.X >= 0
                      && local.X <= ActualWidth;
        var overBar = IsCursorOverFullscreenBar(local);

        // 光标还在触发区或工具栏上时把计时一直往后推：6 秒从最后一次交互算起，
        // 而不是从弹出那一刻算起。否则停留超过 6 秒后再移开，下一次 tick 立刻收起，
        // 用户刚离开工具栏、还没走到旁边的按钮，工具栏就没了。
        if (nearTop || overBar)
        {
            _fullscreenBarShownAt = DateTime.UtcNow;
        }

        if (!FullscreenBarPopup.IsOpen)
        {
            if (nearTop)
            {
                ShowFullscreenBar();
            }

            return;
        }

        if (!nearTop && !overBar
            && DateTime.UtcNow - _fullscreenBarShownAt > FullscreenBarHideDelay)
        {
            HideFullscreenBar();
        }
    }

    /// <summary>
    /// 光标是否停在浮层工具栏上。Popup 自己收不到鼠标消息，只能按摆放位置算矩形。
    /// </summary>
    private bool IsCursorOverFullscreenBar(Point local)
    {
        if (FullscreenBarPopup.Child is not FrameworkElement bar)
        {
            return false;
        }

        var left = FullscreenBarPopup.HorizontalOffset;
        var top = FullscreenBarPopup.VerticalOffset;
        return local.X >= left
               && local.X <= left + bar.ActualWidth
               && local.Y >= top
               && local.Y <= top + bar.ActualHeight;
    }

    private async Task LogoffAndHideAsync()
    {
        try
        {
            await _viewModel.LogoffAndHideAsync();
        }
        finally
        {
            _closeRequestInProgress = false;
        }
    }
}
