using System.Diagnostics;
using OpenCvSharp;
using Vanara.PInvoke;
using static System.Console;

namespace Fischless.GameCapture.BitBlt;

public class BitBltCapture : IGameCapture
{
    public bool IsCapturing { get; private set; }
    private readonly Stopwatch _sizeCheckTimer = new();
    private readonly ReaderWriterLockSlim _lockSlim = new();
    private volatile nint _hWnd; // 需要加锁
    private BitBltSession? _session; // 需要加锁

    private volatile bool _lastCaptureFailed;

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public Mat? Capture() => Capture(false);

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        if (settings == null || !settings.TryGetValue("autoFixWin11BitBlt", out var value)) return;
        if (value is true)
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }

        _lockSlim.EnterWriteLock();
        try
        {
            _hWnd = hWnd;
            if (_hWnd == IntPtr.Zero)
            {
                return;
            }

            _session?.Dispose();
            _session = null;
            IsCapturing = true;
        }
        finally
        {
            _lockSlim.ExitWriteLock();
        }

        CheckSession();
    }


    /// <summary>
    /// 检查窗口大小，如果改变则更新截图尺寸。
    /// </summary>
    private void CheckSession()
    {
        if (_lockSlim.WaitingWriteCount > 0 || !_lockSlim.TryEnterWriteLock(TimeSpan.FromSeconds(0.5)))
        {
            // 写锁持有只会有两种情况:start和CheckSession。无论哪一种都会检查&更新session。
            // 所以当有线程等待更新时，将直接返回
            return;
        }

        try
        {
            // 窗口状态变化可能会导致会话失效
            // 上次截图失败则重置会话，避免一直截图失败
            if (_session is not null && (_session.Invalid || _lastCaptureFailed))
            {
                _session.Dispose();
                _session = null;
            }

            if (!User32.GetClientRect(_hWnd, out var windowRect) || windowRect == default)
            {
                //    Debug.Fail("Failed to get client rectangle");
                // 窗口获取不到或者最小化
                _session?.Dispose();
                _session = null;
                return;
            }

            var width = windowRect.right - windowRect.left;
            var height = windowRect.bottom - windowRect.top;

            if (_session != null)
            {
                if (_session.Width == width && _session.Height == height)
                {
                    // 窗口大小没有改变
                    return;
                }

                // 窗口尺寸被改变，释放资源，然后重新创建会话
                _session.Dispose();
            }

            _session = new BitBltSession(_hWnd, width, height);
        }
        catch (Exception e)
        {
            Error.WriteLine("[BitBlt]Failed to create session:{0}", e);
        }
        finally
        {
            _lockSlim.ExitWriteLock();
        }
    }

    /// <summary>
    /// 递归只尝试一次，会设置标志，正常调用置假即可
    /// </summary>
    /// <param name="recursive">递归标志</param>
    /// <returns>截图</returns>
    private Mat? Capture(bool recursive)
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        if (!_sizeCheckTimer.IsRunning)
        {
            _sizeCheckTimer.Start();
        }

        // 不会经常调整窗口尺寸的，所以隔一段时间检查一次就行
        // 上次如果截图失败的话忽略计时器，避免重复截图失败
        // 递归标志也说明上次截图失败
        if (_lastCaptureFailed || recursive || _sizeCheckTimer.ElapsedMilliseconds > 1000)
        {
            _sizeCheckTimer.Reset();
            CheckSession();
        }

        try
        {
            _lockSlim.EnterReadLock();
            var result = Capture0();
            if (result is not null)
            {
                // 成功截图
                _lastCaptureFailed = false;
                return result;
            }
            else
            {
                if (_lastCaptureFailed) return result; // 这不是首次失败,不再进行尝试
                _lastCaptureFailed = true; // 设置失败标志
                if (recursive) return result; // 已设置递归标志，说明也不是首次失败
            }
        }
        finally
        {
            if (_lockSlim.IsReadLockHeld)
            {
                _lockSlim.ExitReadLock();
            }
        }

        // 首次出现截图异常会跳到这里
        // 首次出现错误重试截图，尽可能不出现截图失败(递归)
        return Capture(true);
    }

    /// <summary>
    /// 截图功能的实现。需要加锁后调用，一般只由 Capture 方法调用。
    /// </summary>
    /// <returns></returns>
    private Mat? Capture0()
    {
        try
        {
            return _session?.GetImage();
        }
        catch (Exception e)
        {
            // 理论这里不应出现异常，除非窗口不存在了或者有什么bug
            Error.WriteLine("[BitBlt]Failed to capture image {0}", e);
            return null;
        }
    }

    public void Stop()
    {
        _lockSlim.EnterWriteLock();
        try
        {
            _hWnd = IntPtr.Zero;
            _sizeCheckTimer.Stop();
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
        finally
        {
            _lockSlim.ExitWriteLock();
        }

        IsCapturing = false;
    }
}
