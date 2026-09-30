using BetterGenshinImpact.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 本机 Win32 游戏窗口。
/// <para>
/// 必须在 UI 线程创建和释放：WinEventHook 需要安装线程的消息循环来投递事件，解除钩子也要在同一线程上进行。
/// </para>
/// </summary>
public class Win32GameWindow : IGameWindow
{
    private const uint EventSystemMoveSizeStart = 0x000A;
    private const uint EventSystemMoveSizeEnd = 0x000B;
    private const uint EventObjectLocationChange = 0x800B;

    /// <summary>
    /// WINEVENT_OUTOFCONTEXT：事件经安装线程的消息循环异步投递，回调不会在 SetWindowPos 等调用中同步重入
    /// </summary>
    private const User32.WINEVENT OutOfContext = 0;

    private readonly Process _process;

    /// <summary>
    /// 持有委托引用，防止被 GC 回收后钩子回调到已释放的函数指针
    /// </summary>
    private readonly User32.WinEventProc _winEventProc;

    private User32.HWINEVENTHOOK _hookMoveSize;
    private User32.HWINEVENTHOOK _hookLocation;
    private bool _disposed;

    public Win32GameWindow(nint hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            throw new ArgumentException("游戏窗口句柄为空", nameof(hWnd));
        }

        Handle = hWnd;
        _process = SystemControl.GetProcessByHandle(hWnd) ?? throw new ArgumentException("通过句柄获取游戏进程失败");
        ProcessId = _process.Id;
        ProcessName = _process.ProcessName;

        // 只订阅窗口所属进程的事件：不再接收全系统的 LOCATIONCHANGE（光标、插入符移动也会触发），
        // 也不需要 SKIPOWNPROCESS / SKIPOWNTHREAD，窗口与 BetterGI 同进程时（网页版宿主）同样能收到
        _winEventProc = OnWinEvent;
        _hookMoveSize = User32.SetWinEventHook(EventSystemMoveSizeStart, EventSystemMoveSizeEnd, default,
            _winEventProc, (uint)ProcessId, 0, OutOfContext);
        _hookLocation = User32.SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, default,
            _winEventProc, (uint)ProcessId, 0, OutOfContext);
    }

    public nint Handle { get; }

    public int ProcessId { get; }

    /// <summary>
    /// 游戏进程名，用于按进程判断前台、查找和关闭游戏
    /// </summary>
    public string ProcessName { get; }

    public GameViewport Viewport => new(SystemControl.GetCaptureRect(Handle), DpiHelper.GetScale(Handle).Y);

    public virtual bool IsAlive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                // 无权查询退出状态时按存活处理，避免误停截图器
                return true;
            }
        }
    }

    public bool IsForeground => (nint)User32.GetForegroundWindow() == Handle;

    public bool IsMinimized => User32.IsIconic(Handle);

    public virtual bool RequiresForeground => true;

    public virtual void Activate() => SystemControl.ActivateWindow(Handle);

    public event EventHandler? ViewportChanged;

    private void OnWinEvent(User32.HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        // idObject == 0 (OBJID_WINDOW)：窗口本身，排除光标、插入符等子对象
        if (idObject != 0 || hwnd.DangerousGetHandle() != Handle)
        {
            return;
        }

        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookMoveSize != default)
        {
            User32.UnhookWinEvent(_hookMoveSize);
            _hookMoveSize = default;
        }

        if (_hookLocation != default)
        {
            User32.UnhookWinEvent(_hookLocation);
            _hookLocation = default;
        }

        ViewportChanged = null;
        if (disposing)
        {
            _process.Dispose();
        }
    }
}
