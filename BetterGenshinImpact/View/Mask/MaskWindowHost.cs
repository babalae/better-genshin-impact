using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
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
    private nint _gameHandle;
    private bool _desiredVisible;
    private RECT _desiredBounds;
    private bool _bringToTopPending;
    private bool _lastGameActive;
    private GameWindowState? _lastReport;

    // 以下字段只在 UI 线程访问
    private MaskWindow? _window;
    private RECT _appliedBounds;

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
            _gameHandle = gameHandle;
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
            _appliedBounds = default;
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
            return null;
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

        if (!state.IsMinimized)
        {
            _lastGameActive = state.IsActive;
        }

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
        nint gameHandle;
        lock (_lock)
        {
            visible = _attached && _desiredVisible;
            bringToTop = _bringToTopPending;
            bounds = _desiredBounds;
            gameHandle = _gameHandle;
            _bringToTopPending = false;
        }

        try
        {
            if (visible)
            {
                var window = EnsureWindow();
                if (bounds.Width > 0 && bounds.Height > 0 && bounds != _appliedBounds)
                {
                    Position(window, gameHandle, bounds);
                    _appliedBounds = bounds;
                    bringToTop = true;
                }

                if (!window.IsVisible)
                {
                    window.Show();
                }

                if (bringToTop)
                {
                    BringToTop(window);
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
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _appliedBounds = default;
                PublishState();
            }
        };
        _window = window;
        return window;
    }

    private static void Position(Window window, nint gameHandle, RECT bounds)
    {
        var dpi = DpiHelper.GetScale(gameHandle);
        window.Left = bounds.Left / dpi.X;
        window.Top = bounds.Top / dpi.Y;
        window.Width = bounds.Width / dpi.X;
        window.Height = bounds.Height / dpi.Y;
    }

    private static void BringToTop(Window window)
    {
        var hWnd = new WindowInteropHelper(window).Handle;
        if (hWnd != IntPtr.Zero)
        {
            User32.BringWindowToTop(hWnd);
        }
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
