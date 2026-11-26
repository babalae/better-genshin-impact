using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Mask;

/// <summary>
/// 遮罩窗口宿主。显隐、置顶、跟随的策略全部集中在这里：
/// 调用方只上报游戏窗口的事实，宿主在调用线程上算出期望状态，变化时才合并投递到 UI 线程应用。
/// </summary>
public sealed class MaskWindowHost : IMaskWindowHost
{
    private readonly Func<MaskWindow> _windowFactory;
    private readonly MaskWindowConfig _config;
    private readonly ILogger<MaskWindowHost> _logger;
    private readonly UiCoalescer _applyCoalescer;
    private readonly object _lock = new();

    // 期望状态，受 _lock 保护
    private bool _attached;
    private bool _desiredVisible;
    private RECT _desiredBounds;
    private bool _bringToTopPending;
    private bool _lastGameActive;
    private GameWindowState? _lastReport;

    // 以下字段只在 UI 线程访问
    private MaskWindow? _window;

    private MaskWindowState _state;

    public MaskWindowHost(Func<MaskWindow> windowFactory, IConfigService configService, ILogger<MaskWindowHost> logger)
    {
        _windowFactory = windowFactory;
        _logger = logger;
        _config = configService.Get().MaskWindowConfig;
        _config.PropertyChanged += OnMaskWindowConfigChanged;
        _applyCoalescer = new UiCoalescer(Apply, priority: DispatcherPriority.Normal);
    }

    public MaskWindowState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public event EventHandler<MaskWindowState>? StateChanged;

    public void Attach(nint gameHandle)
    {
        lock (_lock)
        {
            _attached = true;
            _lastReport = null;
            _lastGameActive = true;
            _desiredVisible = true;
            _desiredBounds = SystemControl.GetCaptureRect(gameHandle);
            _bringToTopPending = true;
        }

        RunOnUiThread(Apply);
    }

    public void Detach()
    {
        lock (_lock)
        {
            _attached = false;
            _lastReport = null;
            _desiredVisible = false;
            _bringToTopPending = false;
        }

        RunOnUiThread(Apply);
    }

    public void Close()
    {
        Detach();
        RunOnUiThread(() =>
        {
            var window = _window;
            _window = null;
            window?.Close();
        });
    }

    /// <summary>
    /// 生命周期操作（Attach / Detach / Close）调用方需要等待完成，非 UI 线程时同步切换
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.Invoke(action);
        }
    }

    public void ReportGameWindow(GameWindowState state)
    {
        bool changed;
        lock (_lock)
        {
            if (!_attached)
            {
                return;
            }

            _lastReport = state;
            changed = EvaluateLocked(state);
        }

        if (changed)
        {
            _applyCoalescer.Request();
        }
    }

    private void OnMaskWindowConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaskWindowConfig.OverlayLayoutEditEnabled))
        {
            Reevaluate();
        }
    }

    /// <summary>
    /// 策略输入（布局编辑模式）变化时，用最近一次上报重新计算
    /// </summary>
    private void Reevaluate()
    {
        bool changed;
        lock (_lock)
        {
            if (!_attached)
            {
                return;
            }

            if (_lastReport is { } report)
            {
                changed = EvaluateLocked(report);
            }
            else
            {
                var forced = IsForcedVisibleLocked();
                changed = forced && !_desiredVisible;
                _desiredVisible |= forced;
            }
        }

        if (changed)
        {
            _applyCoalescer.Request();
        }
    }

    private bool IsForcedVisibleLocked()
    {
        return _config.OverlayLayoutEditEnabled;
    }

    /// <summary>
    /// 按优先级从高到低判断，命中一条即停止。返回 null 表示保持不变
    /// </summary>
    private bool? DecideVisibleLocked(GameWindowState state)
    {
        if (IsForcedVisibleLocked())
        {
            return true;
        }

        if (!state.IsCapturing)
        {
            return false;
        }

        if (state.IsMinimized)
        {
            // 最小化时隐藏遮罩，避免游戏进入后台后日志仍然停留在桌面上。
            return false;
        }

        if (state.IsActive)
        {
            return true;
        }

        // 前台属于 BetterGI 自身或游戏进程时保留遮罩；另一个 BetterGI 实例在前台时隐藏，避免多个置顶遮罩叠在一起
        if (state.IsForegroundOwnedByBetterGiOrGame)
        {
            return null;
        }

        return false;
    }

    private bool EvaluateLocked(GameWindowState state)
    {
        var changed = false;
        if (DecideVisibleLocked(state) is { } visible && visible != _desiredVisible)
        {
            _desiredVisible = visible;
            changed = true;
        }

        // 游戏从后台回到前台时置顶一次
        if (state.IsActive && !_lastGameActive && _desiredVisible)
        {
            _bringToTopPending = true;
            changed = true;
        }

        // 最小化也视为失焦，确保还原到前台时重新置顶遮罩。
        _lastGameActive = state.IsActive && !state.IsMinimized;

        if (state.Bounds.Width > 0 && state.Bounds.Height > 0 && state.Bounds != _desiredBounds)
        {
            _desiredBounds = state.Bounds;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// UI 线程：把期望状态应用到窗口
    /// </summary>
    private void Apply()
    {
        bool visible;
        bool bringToTop;
        RECT bounds;
        lock (_lock)
        {
            visible = _attached && _desiredVisible;
            bringToTop = _bringToTopPending;
            bounds = _desiredBounds;
            _bringToTopPending = false;
        }

        try
        {
            if (visible)
            {
                var window = EnsureWindow();
                var handle = new WindowInteropHelper(window).EnsureHandle();
                bringToTop |= AlignBounds(handle, bounds);

                if (!window.IsVisible)
                {
                    window.Show();
                    // 首次显示会完成 WPF 布局，再检查一次实际矩形。
                    bringToTop |= AlignBounds(handle, bounds);
                }

                if (bringToTop)
                {
                    User32.BringWindowToTop(handle);
                }
            }
            else if (_window is { IsVisible: true } window)
            {
                window.Hide();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or TaskCanceledException)
        {
            // 程序退出过程中窗口可能已关闭
            _logger.LogDebug(e, "应用遮罩窗口状态失败");
        }

        PublishState();
    }

    private MaskWindow EnsureWindow()
    {
        if (_window != null)
        {
            return _window;
        }

        var window = _windowFactory();
        window.DpiChanged += OnWindowDpiChanged;
        window.Closed += (_, _) =>
        {
            window.DpiChanged -= OnWindowDpiChanged;
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                PublishState();
            }
        };
        _window = window;
        return window;
    }

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e)
    {
        // WPF 还会应用 DPI 消息建议的矩形，等本次消息处理结束再对齐游戏。
        _applyCoalescer.Request();
    }

    private bool AlignBounds(HWND handle, RECT bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0
            || (User32.GetWindowRect(handle, out var actualBounds) && actualBounds == bounds))
        {
            return false;
        }

        // 有实际父窗口时，SetWindowPos 的位置相对于父窗口客户区。
        var origin = new POINT(bounds.Left, bounds.Top);
        var parent = User32.GetAncestor(handle, User32.GetAncestorFlag.GA_PARENT);
        if (parent != HWND.NULL && parent != User32.GetDesktopWindow() && !User32.ScreenToClient(parent, ref origin))
        {
            return false;
        }

        var flags = User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE;
        if (User32.SetWindowPos(handle, HWND.NULL, origin.X, origin.Y, bounds.Width, bounds.Height, flags))
        {
            return true;
        }

        _logger.LogDebug("遮罩窗口定位失败，Win32 错误码 {Error}", Marshal.GetLastWin32Error());
        return false;
    }

    private void PublishState()
    {
        var window = _window;
        var state = window == null
            ? new MaskWindowState(false, default)
            : new MaskWindowState(window.IsVisible, new Rect(window.Left, window.Top, window.Width, window.Height));

        lock (_lock)
        {
            if (state == _state)
            {
                return;
            }

            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }

}
