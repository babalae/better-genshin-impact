using System;
using System.ComponentModel;
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
    private bool _topmostBeforeFullscreen;
    private double _minWidthBeforeFullscreen;
    private double _maxWidthBeforeFullscreen;
    private double _minHeightBeforeFullscreen;
    private double _maxHeightBeforeFullscreen;
    private bool _suppressStateChange;
    private DateTime _fullscreenBarShownAt;

    private GameModeSnapshot? _gameModeSnapshot;

    internal RdpActiveXHost RdpHost { get; } = new();

    internal bool AllowClose { get; set; }

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

        // 隐藏窗口时一定退出全屏：全屏下的画面被藏起来后，用户既看不到浮层按钮，
        // 也没法把游戏模式关掉——先退干净再隐藏。
        IsVisibleChanged += OnWindowIsVisibleChanged;

        // 最大化按钮就是全屏入口（没有菜单项），所以状态变化要接管。
        StateChanged += OnWindowStateChanged;

        // 「固定工具栏」是视图模型上的设置项，这里跟着它决定工具栏是否常驻。
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.TryApplySystemBackdrop(this);
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

    protected override void OnClosed(EventArgs e)
    {
        // 关窗时可能还停在全屏：游戏模式是改过的系统设置，必须还原；
        // 输入模式也要还原，否则「进全屏时被自动切成游戏鼠标」会留在配置里。
        RestoreGameMode();
        _viewModel.RestoreInputModeAfterFullscreen();
        _fullscreenBarTimer.Stop();
        RdpFormsHost.Child = null;
        RdpHost.Dispose();
        base.OnClosed(e);
    }

    private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 最小化也会让 IsVisible 变 false，但那是用户主动最小化，全屏状态要保留，
        // 恢复后还是全屏。只有真正被 Hide() 藏起来时才退出全屏——否则用户看不到
        // 浮层按钮，也没法关掉游戏模式。
        if (e.NewValue is false && IsFullscreen && WindowState != WindowState.Minimized)
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

    /// <summary>
    /// 进入全屏：铺满当前显示器（含任务栏区域），隐藏标题栏，并打开 Windows 游戏模式。
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
        ApplyFullscreenLetterbox();

        ApplyGameMode();
        _viewModel.ApplyInputModeForFullscreen();
        UpdateFullscreenPinButton(_viewModel.IsFullscreenBarPinned);
        ShowFullscreenBar();
        _fullscreenBarTimer.Start();
    }

    /// <summary>
    /// 退出全屏，还原进入之前的窗口状态与系统游戏模式设置。
    /// </summary>
    internal void ExitFullscreen()
    {
        if (!IsFullscreen)
        {
            return;
        }

        IsFullscreen = false;
        _fullscreenBarTimer.Stop();
        HideFullscreenBar();

        WindowStyle = _styleBeforeFullscreen;
        ResizeMode = _resizeBeforeFullscreen;
        WindowTitleBar.Visibility = Visibility.Visible;
        SetCurrentValue(TopmostProperty, _topmostBeforeFullscreen);

        // 还原 DWM 圆角与背景材质。WindowBackdropType 回到原值会重新 ApplyBackdrop，
        // 顺带把 Background/合成背景恢复成透明（Mica 需要），所以这里不用手动清 Background。
        WindowCornerPreference = _cornerPreferenceBeforeFullscreen;
        WindowBackdropType = _backdropTypeBeforeFullscreen;

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

            Left = _boundsBeforeFullscreen.Left;
            Top = _boundsBeforeFullscreen.Top;
            Width = _boundsBeforeFullscreen.Width;
            Height = _boundsBeforeFullscreen.Height;
        }
        finally
        {
            _suppressStateChange = false;
        }

        RestoreGameMode();
        _viewModel.RestoreInputModeAfterFullscreen();
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
    /// 打开 Windows 游戏模式（全屏即游戏模式，没有开关）。只做一次，退出全屏时统一还原。
    /// </summary>
    private void ApplyGameMode()
    {
        if (_gameModeSnapshot is not null)
        {
            return;
        }

        _gameModeSnapshot = WindowsGameMode.TryEnable(_logger);
    }

    private void RestoreGameMode()
    {
        var snapshot = _gameModeSnapshot;
        if (snapshot is null)
        {
            return;
        }

        // 还原失败（注册表一时被占用等）就留着快照：关窗时还会再试一次，
        // 直接清掉等于把系统设置永久改掉了。
        if (WindowsGameMode.Restore(snapshot, _logger))
        {
            _gameModeSnapshot = null;
        }
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
        FullscreenBarPopup.VerticalOffset = 8;
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
        var nearTop = local.Y >= 0 && local.Y <= FullscreenBarTriggerHeight;

        if (!FullscreenBarPopup.IsOpen)
        {
            if (nearTop)
            {
                ShowFullscreenBar();
            }

            return;
        }

        if (!nearTop && !IsCursorOverFullscreenBar(local)
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
