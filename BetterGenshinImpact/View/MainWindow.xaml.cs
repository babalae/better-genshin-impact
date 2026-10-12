using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.ViewModel;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Ui;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray.Controls;

namespace BetterGenshinImpact.View;

public partial class MainWindow : FluentWindow, INavigationWindow
{
    private readonly ILogger<MainWindow> _logger = App.GetLogger<MainWindow>();
    private ScrollViewer? _currentScrollViewer;
    private double _targetOffset;
    private double _inputVelocity;
    private double _lastInputTime;
    private bool _isContinuousInput;
    private bool _isInertiaActive;
    private bool _isRenderingSubscribed;
    private DateTime _lastFrameTime = DateTime.Now;
    private readonly double _continuousLerpFactor = 0.35;
    private readonly double _discreteLerpFactor = 0.85;
    private readonly double _scrollSensitivity = 0.27;
    private readonly double _inertiaDecay = 0.94;
    private readonly double _minVelocity = 0.3;
    private const double TargetFps = 60.0;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, INavigationService navigationService, ISnackbarService snackbarService)
    {
        _logger.LogDebug("主窗体实例化");
        DataContext = ViewModel = viewModel;

        InitializeComponent();
        this.InitializeDpiAwareness();

        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        navigationService.SetNavigationControl(RootNavigation);

        Application.Current.MainWindow = this;

        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnGlobalPreviewMouseWheel), true);

        Loaded += (s, e) => Activate();

        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _lastNonMinimizedWindowState = WindowState;
            }
        };
    }

    private void OnGlobalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindScrollViewerUnderMouse(e);
        if (scrollViewer == null)
            return;

        e.Handled = true;

        if (_currentScrollViewer != scrollViewer)
        {
            _currentScrollViewer = scrollViewer;
            if (scrollViewer.CanContentScroll)
            {
                scrollViewer.CanContentScroll = false;
                scrollViewer.UpdateLayout();
            }

            _targetOffset = scrollViewer.VerticalOffset;
        }

        double now = Environment.TickCount;

        double delta = e.Delta;
        double scrollAmount = delta * _scrollSensitivity;

        _targetOffset -= scrollAmount;
        _targetOffset = Math.Max(0, Math.Min(_targetOffset, scrollViewer.ScrollableHeight));

        // 鼠标滚轮 delta 固定为 ±120，触控板 delta 小且变化（通常 < 50）
        bool isMouseWheel = Math.Abs(delta) >= 100;

        if (!isMouseWheel)
        {
            // 触控板：累积速度用于后续惯性
            _isContinuousInput = true;
            _inputVelocity = -scrollAmount * 0.35;
        }
        else
        {
            // 鼠标滚轮：目标位移已在 _targetOffset 中，无需惯性
            _isContinuousInput = false;
            _inputVelocity = 0;
        }
        _isInertiaActive = false;
        _lastInputTime = now;

        // 按需订阅：有滚动活动才监听渲染帧
        if (!_isRenderingSubscribed)
        {
            _lastFrameTime = DateTime.Now;
            _isRenderingSubscribed = true;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
        }
    }

    private ScrollViewer? FindScrollViewerUnderMouse(MouseWheelEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject
                      ?? Mouse.DirectlyOver as DependencyObject;
        while (current != null)
        {
            if (current is ScrollViewer scrollViewer && scrollViewer.ScrollableHeight > 0)
            {
                return scrollViewer;
            }

            if (current is System.Windows.Controls.ListViewItem listViewItem
                && ItemsControl.ItemsControlFromItemContainer(listViewItem) is System.Windows.Controls.ListView listView)
            {
                var listScrollViewer = FindVisualChild<ScrollViewer>(listView);
                if (listScrollViewer is { ScrollableHeight: > 0 })
                {
                    return listScrollViewer;
                }
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement)
                   ?? (contentElement as FrameworkContentElement)?.Parent;
        }

        if (child is FrameworkElement { Parent: not null } frameworkElement)
        {
            return frameworkElement.Parent;
        }

        return VisualTreeHelper.GetParent(child);
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_currentScrollViewer == null)
            return;

        double current = _currentScrollViewer.VerticalOffset;
        double diff = _targetOffset - current;

        if (Math.Abs(diff) < 0.1 && Math.Abs(_inputVelocity) < _minVelocity)
        {
            // 滚动结束，取消渲染订阅
            if (_isRenderingSubscribed)
            {
                _isRenderingSubscribed = false;
                CompositionTarget.Rendering -= OnCompositionTargetRendering;
            }
            return;
        }

        double deltaTime = (DateTime.Now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = DateTime.Now;
        deltaTime = Math.Min(deltaTime, 0.1);

        double dtScale = deltaTime * TargetFps;
        double effectiveLerp = _isContinuousInput ? _continuousLerpFactor : _discreteLerpFactor;
        double normalizedLerp = 1.0 - Math.Pow(1.0 - effectiveLerp, dtScale);
        double normalizedDecay = Math.Pow(_inertiaDecay, dtScale);

        double now = Environment.TickCount;
        double timeSinceLastInput = now - _lastInputTime;

        if (timeSinceLastInput > 80 && Math.Abs(diff) < 1.0)
        {
            _isInertiaActive = true;
        }

        if (_isInertiaActive)
        {
            if (Math.Abs(_inputVelocity) > _minVelocity)
            {
                _inputVelocity *= normalizedDecay;
                _targetOffset += _inputVelocity * dtScale;
                _targetOffset = Math.Max(0, Math.Min(_targetOffset, _currentScrollViewer.ScrollableHeight));
            }
            else
            {
                _isInertiaActive = false;
            }
        }

        diff = _targetOffset - current;
        if (Math.Abs(diff) > 0.1)
        {
            double newPosition = current + diff * normalizedLerp;
            _currentScrollViewer.ScrollToVerticalOffset(newPosition);
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            var foundChild = FindVisualChild<T>(child);
            if (foundChild != null)
            {
                return foundChild;
            }
        }
        return null;
    }

    private bool _windowPositionRestored;
    private WindowState _lastNonMinimizedWindowState;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHelper.TryApplySystemBackdrop(this);
        RestoreWindowPosition();
    }

    private void RestoreWindowPosition()
    {
        if (_windowPositionRestored)
        {
            return;
        }

        var config = ViewModel.Config.CommonConfig;
        if (!config.RestoreWindowPositionAndSize)
        {
            return;
        }

        if (config.WindowWidth > 0 && config.WindowHeight > 0)
        {
            Width = config.WindowWidth;
            Height = config.WindowHeight;
        }

        ClampWindowToScreen(config.WindowLeft, config.WindowTop);

        if (config.WindowState == 2)
        {
            Loaded += (_, _) => WindowState = WindowState.Maximized;
        }

        _windowPositionRestored = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveWindowPosition();
        _logger.LogDebug("主窗体退出");
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        RemoveHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnGlobalPreviewMouseWheel));
        base.OnClosed(e);
        App.GetService<NotifyIconViewModel>()?.Exit();
    }

    private void SaveWindowPosition()
    {
        var config = ViewModel.Config.CommonConfig;

        // Determine the correct state: if the window is minimized (e.g. hidden to tray),
        // use the last non-minimized state so a previously maximized window is restored correctly.
        var effectiveState = WindowState == WindowState.Minimized
            ? _lastNonMinimizedWindowState
            : WindowState;

        if (effectiveState == WindowState.Maximized)
        {
            config.WindowState = 2;
        }
        else
        {
            config.WindowState = 0;
        }

        if (WindowState == WindowState.Normal)
        {
            config.WindowLeft = Left;
            config.WindowTop = Top;
            config.WindowWidth = Width;
            config.WindowHeight = Height;
        }
        else
        {
            // Maximized or Minimized: save RestoreBounds (the normal bounds before the state change)
            config.WindowLeft = RestoreBounds.Left;
            config.WindowTop = RestoreBounds.Top;
            config.WindowWidth = RestoreBounds.Width;
            config.WindowHeight = RestoreBounds.Height;
        }
    }

    /// <summary>
    /// Clamp the window position so it lands within an actual monitor's working area.
    /// Validates against each physical monitor rather than the VirtualScreen bounding box,
    /// avoiding unreachable positions in non-rectangular multi-monitor layouts.
    /// </summary>
    private void ClampWindowToScreen(double left, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var testX = (int)((left + Width / 2) * dpi.DpiScaleX);
        var testY = (int)((top + 15) * dpi.DpiScaleY);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(testX, testY));
        var work = screen.WorkingArea;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workRight = work.Right / dpi.DpiScaleX;
        var workBottom = work.Bottom / dpi.DpiScaleY;

        Left = Math.Max(workLeft, Math.Min(left, workRight - Width));
        Top = Math.Max(workTop, Math.Min(top, workBottom - 30));
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private void OnNotifyIconLeftDoubleClick(NotifyIcon sender, RoutedEventArgs e)
    {
        App.GetService<NotifyIconViewModel>()?.ShowOrHide();
    }

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
        RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();
}
