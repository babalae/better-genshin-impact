using System.Diagnostics;
using OpenCvSharp;
using Vanara.PInvoke;
using static System.Console;

namespace Fischless.GameCapture.BitBlt;

public class BitBltCapture : IGameCapture
{
    public CaptureModes Mode => CaptureModes.BitBlt;
    public bool IsCapturing { get; private set; }
    private readonly Stopwatch _sizeCheckTimer = new();
    private readonly ReaderWriterLockSlim _lockSlim = new();
    private volatile nint _hWnd; // 需要加锁
    private BitBltSession? _session; // 需要加锁

    private volatile bool _needResetSession;

    public void Dispose() => Stop();

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
    /// 检查窗口大小，如果改变则更新截图尺寸。返回是否成功。
    /// </summary>
    /// <returns></returns>
    private bool CheckSession()
    {
        if (!_lockSlim.TryEnterWriteLock(TimeSpan.FromSeconds(0.5)))
        {
            return false;
        }

        try
        {
            if (_session is not null && (_session.IsInvalid() || _needResetSession)) // 窗口状态变化可能会导致会话失效
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
                return false;
            }

            var width = windowRect.right - windowRect.left;
            var height = windowRect.bottom - windowRect.top;

            _session ??= new BitBltSession(_hWnd, width, height);
            if (_session.Width == width && _session.Height == height)
            {
                return true;
            }

            // 窗口尺寸被改变，释放资源
            _session.Dispose();
            _session = new BitBltSession(_hWnd, width, height);
            return true;
        }
        catch (Exception e)
        {
            Error.WriteLine("[BitBlt]Failed to create session:{0}", e);
            return false;
        }
        finally
        {
            _lockSlim.ExitWriteLock();
        }
    }

    public Mat? Capture()
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        if (!_sizeCheckTimer.IsRunning)
        {
            _sizeCheckTimer.Start();
        }

        if (_sizeCheckTimer.ElapsedMilliseconds > 1000)
        {
            _sizeCheckTimer.Reset();
            // 不会经常调整窗口尺寸的，所以隔一段时间检查一次就行
            if (!CheckSession())
            {
                return null;
            }
        }

        Mat? mat = null;
        try
        {
            _lockSlim.EnterReadLock();
            if (_session is null)
            {
                // 没有成功创建会话，直接返回空
                return null;
            }
            mat = _session.BitBlt();
            
            if (mat is not null) // 成功执行
            {
                if (!mat.Empty()) // 并且获取到了图
                {
                    _needResetSession = false;
                    return mat;
                }
                else // 成功执行但是没有图，可能是截图过快导致的
                {
                    mat.Dispose();
                    mat = null; // 防止二次释放
                }
            }

            // 无论没有图还是执行失败都会到这里
            if (_session is not null && !_session.IsInvalid())
            {
                // _session正确但是截图失败，如果持续出现这个问题需要重置会话
                _needResetSession = true;
            }

            return null;
        }
        catch (Exception e)
        {
            // 理论这里不应出现异常，除非窗口不存在了或者有什么bug
            // 出现异常的时候释放内存
            mat?.Dispose();
            // 如果持续出现异常则重置会话
            _needResetSession = true;
            Error.WriteLine("[BitBlt]Failed to capture image {0}", e);
            return null;
        }
        finally
        {
            _lockSlim.ExitReadLock();
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