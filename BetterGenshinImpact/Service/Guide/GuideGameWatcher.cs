using System;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 监听游戏窗口的前台、最小化与位置变化，供小窗自动显示隐藏和跟随（见设计文档 5.4 节）。
/// 用系统事件钩子，不依赖调度器 Tick；必须在界面线程挂接和解除，事件也在界面线程触发
/// </summary>
public sealed class GuideGameWatcher : IDisposable
{
    /// <summary>
    /// 前台窗口变化
    /// </summary>
    private const uint EventSystemForeground = 0x0003;

    /// <summary>
    /// 窗口开始最小化
    /// </summary>
    private const uint EventSystemMinimizeStart = 0x0016;

    /// <summary>
    /// 窗口结束最小化
    /// </summary>
    private const uint EventSystemMinimizeEnd = 0x0017;

    /// <summary>
    /// 事件经安装线程的消息循环异步投递
    /// </summary>
    private const User32.WINEVENT OutOfContext = 0;

    /// <summary>
    /// 持有委托引用，防止被回收后钩子回调到已释放的函数指针
    /// </summary>
    private readonly User32.WinEventProc _winEventProc;

    /// <summary>
    /// 前台变化钩子（全部进程）
    /// </summary>
    private User32.HWINEVENTHOOK _foregroundHook;

    /// <summary>
    /// 最小化钩子（只看游戏进程）
    /// </summary>
    private User32.HWINEVENTHOOK _minimizeHook;

    /// <summary>
    /// 当前挂接的游戏窗口
    /// </summary>
    private IGameWindow? _window;

    /// <summary>
    /// 最近一次 BetterGI 以外的前台窗口是否是游戏。
    /// 小窗进入交互模式或意外获得焦点时，据此判断结束后是否把焦点还给游戏
    /// </summary>
    public bool LastExternalForegroundWasGame { get; private set; }

    public GuideGameWatcher()
    {
        _winEventProc = OnWinEvent;
    }

    /// <summary>
    /// 是否已挂接游戏窗口
    /// </summary>
    public bool IsAttached => _window is not null;

    /// <summary>
    /// 游戏窗口句柄，未挂接时为 0
    /// </summary>
    public nint GameHandle => _window?.Handle ?? 0;

    /// <summary>
    /// 游戏是否在前台
    /// </summary>
    public bool IsGameForeground => _window is { } window && window.IsForeground;

    /// <summary>
    /// 游戏是否最小化
    /// </summary>
    public bool IsGameMinimized => _window is { } window && window.IsMinimized;

    /// <summary>
    /// 游戏画面区域（物理像素），未挂接或无法获取时返回 false
    /// </summary>
    public bool TryGetGameRect(out RECT rect)
    {
        rect = default;
        if (_window is not { } window || !User32.IsWindow(window.Handle))
        {
            return false;
        }

        rect = SystemControl.GetCaptureRect(window.Handle);
        return rect.Width > 0 && rect.Height > 0;
    }

    /// <summary>
    /// 前台窗口或游戏最小化状态变化
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// 游戏窗口移动或缩放
    /// </summary>
    public event EventHandler? GameMoved;

    /// <summary>
    /// 挂接游戏窗口。在界面线程调用
    /// </summary>
    public void Attach(IGameWindow window)
    {
        Detach();
        _window = window;
        LastExternalForegroundWasGame = window.IsForeground;
        window.ViewportChanged += OnViewportChanged;
        _foregroundHook = User32.SetWinEventHook(EventSystemForeground, EventSystemForeground, default,
            _winEventProc, 0, 0, OutOfContext);
        _minimizeHook = User32.SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeEnd, default,
            _winEventProc, (uint)window.ProcessId, 0, OutOfContext);
    }

    /// <summary>
    /// 解除挂接。在界面线程调用，重复调用无副作用
    /// </summary>
    public void Detach()
    {
        if (_window is { } window)
        {
            window.ViewportChanged -= OnViewportChanged;
            _window = null;
        }

        if (_foregroundHook != default)
        {
            User32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = default;
        }

        if (_minimizeHook != default)
        {
            User32.UnhookWinEvent(_minimizeHook);
            _minimizeHook = default;
        }
    }

    /// <summary>
    /// 前台窗口是否属于 BetterGI 自身（主窗口、交互模式下的小窗等）
    /// </summary>
    public static bool IsForegroundOwnedByBetterGi()
    {
        var foreground = User32.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return false;
        }

        _ = User32.GetWindowThreadProcessId(foreground, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// 把焦点还给游戏窗口。游戏已最小化或窗口已不存在时不处理，避免把最小化的游戏强行弹出
    /// </summary>
    public void FocusGame()
    {
        var handle = GameHandle;
        if (handle == 0 || !User32.IsWindow(handle) || User32.IsIconic(handle))
        {
            return;
        }

        _ = User32.SetForegroundWindow(handle);
        _ = User32.BringWindowToTop(handle);
    }

    /// <summary>
    /// 两个窗口是否在同一显示器上
    /// </summary>
    public static bool IsOnSameMonitor(nint first, nint second)
    {
        if (first == 0 || second == 0)
        {
            return true;
        }

        var a = User32.MonitorFromWindow(first, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        var b = User32.MonitorFromWindow(second, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        return a == b;
    }

    /// <summary>
    /// 系统事件回调：只关心窗口本身的事件
    /// </summary>
    private void OnWinEvent(User32.HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0 || _window is null)
        {
            return;
        }

        if (@event is EventSystemMinimizeStart or EventSystemMinimizeEnd
            && hwnd.DangerousGetHandle() != _window.Handle)
        {
            return;
        }

        if (@event == EventSystemForeground && !hwnd.IsNull)
        {
            _ = User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)Environment.ProcessId)
            {
                LastExternalForegroundWasGame = hwnd.DangerousGetHandle() == _window.Handle || pid == (uint)_window.ProcessId;
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 游戏窗口移动或缩放
    /// </summary>
    private void OnViewportChanged(object? sender, EventArgs e)
    {
        GameMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Detach();
    }
}
