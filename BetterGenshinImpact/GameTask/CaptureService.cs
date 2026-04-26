using System;
using System.Collections.Generic;
using System.Threading;
using BetterGenshinImpact.Helpers;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask;

public class CaptureService : IDisposable
{
    private readonly ILogger<CaptureService> _logger;
    private readonly object _locker = new();

    private IGameCapture? _gameCapture;
    private IntPtr _hWnd;
    private CaptureModes _mode;
    private bool _hasStartContext;
    private int _consecutiveCaptureFailures;
    private DateTime _firstCaptureFailureTime = DateTime.MinValue;
    private bool _permanentFailureRaised;
    private DateTime _lastRestartAttemptTime = DateTime.MinValue;
    private int _startGate;
    private int _disposeState;

    public IGameCapture? GameCapture
    {
        get => Volatile.Read(ref _gameCapture);
        private set => Volatile.Write(ref _gameCapture, value);
    }

    public int CaptureVersion { get; private set; }

    public bool IsCapturing => GameCapture?.IsCapturing == true;

    internal Func<CaptureModes, IGameCapture> CaptureFactory { get; set; }

    internal Func<Dictionary<string, object>> StartSettingsFactory { get; set; }

    internal TimeProvider Clock { get; set; }

    public event EventHandler? CaptureUnavailable;

    public event EventHandler? CaptureRecovered;

    public event EventHandler? PermanentFailure;

    public CaptureService(ILogger<CaptureService>? logger = null)
    {
        _logger = logger ?? App.GetLogger<CaptureService>();
        CaptureFactory = GameCaptureFactory.Create;
        StartSettingsFactory = CreateDefaultStartSettings;
        Clock = TimeProvider.System;
    }

    public void Start(IntPtr hWnd, CaptureModes mode)
    {
        lock (_locker)
        {
            _hWnd = hWnd;
            _mode = mode;
            _hasStartContext = hWnd != IntPtr.Zero;
        }

        StartCore();
    }

    public bool Restart(string reason)
    {
        lock (_locker)
        {
            if (!_hasStartContext || _hWnd == IntPtr.Zero)
            {
                return false;
            }

            if (_lastRestartAttemptTime != DateTime.MinValue
                && (Now() - _lastRestartAttemptTime).TotalMilliseconds < 300)
            {
                return false;
            }

            _lastRestartAttemptTime = Now();
        }

        _logger.LogWarning("截图器尝试内部重启: {Reason}", reason);
        return StartCore();
    }

    public void Stop()
    {
        StopCore(clearStartContext: true);
        ResetFailureState();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Mat? CaptureNoRetry()
    {
        // Snapshot the current capture instance. Stop/Restart may retire it concurrently,
        // so callers must tolerate transient failures and this method treats them as recoverable.
        var capture = GameCapture;
        if (capture == null || !capture.IsCapturing)
        {
            MarkCaptureFailed();
            return null;
        }

        try
        {
            var image = capture.Capture();
            if (image != null)
            {
                MarkCaptureRecoveredIfNeeded();
                return image;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "截图器取帧失败");
        }

        MarkCaptureFailed();
        return null;
    }

    public Mat? CaptureWithRetry(int retryCount = 3, int retryDelayMs = 100)
    {
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            var image = CaptureNoRetry();
            if (image != null)
            {
                return image;
            }

            var currentCapture = GameCapture;
            if ((currentCapture == null || !currentCapture.IsCapturing || Volatile.Read(ref _consecutiveCaptureFailures) >= 2)
                && attempt < retryCount - 1)
            {
                Restart($"截图失败，正在恢复 ({attempt + 1}/{retryCount})");
            }

            if (attempt < retryCount - 1)
            {
                Thread.Sleep(retryDelayMs);
            }
        }

        return null;
    }

    public IGameCapture RequireGameCapture()
    {
        var capture = GameCapture;
        if (capture == null)
        {
            throw new InvalidOperationException("截图器未初始化!");
        }

        return capture;
    }

    private bool StartCore()
    {
        if (Interlocked.CompareExchange(ref _startGate, 1, 0) != 0)
        {
            _logger.LogDebug("截图器启动或重启已在进行中，跳过本次请求");
            return false;
        }

        IGameCapture? nextCapture = null;
        IGameCapture? previousCapture = null;

        try
        {
            lock (_locker)
            {
                if (!_hasStartContext || _hWnd == IntPtr.Zero)
                {
                    return false;
                }
            }

            IntPtr hWnd;
            CaptureModes mode;
            lock (_locker)
            {
                hWnd = _hWnd;
                mode = _mode;
            }

            nextCapture = CaptureFactory(mode);
            nextCapture.Start(hWnd, StartSettingsFactory());

            var adopted = false;
            lock (_locker)
            {
                if (_hasStartContext && _hWnd == hWnd && _mode == mode)
                {
                    previousCapture = GameCapture;
                    GameCapture = nextCapture;
                    CaptureVersion++;
                    ResetFailureWindowAfterStartSuccessUnderLock();
                    adopted = true;
                }
            }

            if (!adopted)
            {
                _logger.LogDebug("截图器启动完成时上下文已变化，放弃切换到新实例");
                ReleaseCapture(nextCapture);
                nextCapture = null;
                return false;
            }

            ReleaseCapture(previousCapture);
            previousCapture = null;
            nextCapture = null;
            return true;
        }
        catch (Exception ex)
        {
            ReleaseCapture(nextCapture);
            _logger.LogError(ex, "截图器启动失败");
            MarkCaptureFailed();
            return false;
        }
        finally
        {
            Volatile.Write(ref _startGate, 0);
        }
    }

    private void StopCore(bool clearStartContext)
    {
        ReleaseCapture(DetachCapture(clearStartContext));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Stop();

        lock (_locker)
        {
            CaptureUnavailable = null;
            CaptureRecovered = null;
            PermanentFailure = null;
        }
    }

    private IGameCapture? DetachCapture(bool clearStartContext)
    {
        lock (_locker)
        {
            var captureToStop = GameCapture;
            GameCapture = null;
            if (clearStartContext)
            {
                _hWnd = IntPtr.Zero;
                _hasStartContext = false;
            }

            return captureToStop;
        }
    }

    private void ReleaseCapture(IGameCapture? captureToStop)
    {
        try
        {
            captureToStop?.Stop();
            captureToStop?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止截图器时出现异常");
        }
    }

    private void ResetFailureWindowAfterStartSuccessUnderLock()
    {
        if (_consecutiveCaptureFailures > 0)
        {
            _firstCaptureFailureTime = Now();
        }
        else
        {
            _firstCaptureFailureTime = DateTime.MinValue;
        }

        _permanentFailureRaised = false;
    }

    private void MarkCaptureFailed()
    {
        var shouldRaiseUnavailable = false;
        var shouldRaisePermanentFailure = false;

        lock (_locker)
        {
            if (_consecutiveCaptureFailures == 0)
            {
                _firstCaptureFailureTime = Now();
                shouldRaiseUnavailable = true;
            }

            _consecutiveCaptureFailures++;

            if (!_permanentFailureRaised
                && _firstCaptureFailureTime != DateTime.MinValue
                && (Now() - _firstCaptureFailureTime).TotalSeconds >= 5)
            {
                _permanentFailureRaised = true;
                shouldRaisePermanentFailure = true;
            }
        }

        if (shouldRaiseUnavailable)
        {
            _logger.LogWarning("截图暂不可用，进入恢复流程");
            CaptureUnavailable?.Invoke(this, EventArgs.Empty);
        }

        if (shouldRaisePermanentFailure)
        {
            _logger.LogError("截图长时间不可用，需要人工介入");
            PermanentFailure?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MarkCaptureRecoveredIfNeeded()
    {
        var shouldRaiseRecovered = false;

        lock (_locker)
        {
            shouldRaiseRecovered = _consecutiveCaptureFailures > 0;
            ResetFailureStateUnderLock();
        }

        if (shouldRaiseRecovered)
        {
            _logger.LogInformation("截图恢复成功");
            CaptureRecovered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResetFailureState()
    {
        lock (_locker)
        {
            ResetFailureStateUnderLock();
        }
    }

    private void ResetFailureStateUnderLock()
    {
        _consecutiveCaptureFailures = 0;
        _firstCaptureFailureTime = DateTime.MinValue;
        _permanentFailureRaised = false;
    }

    private Dictionary<string, object> CreateDefaultStartSettings()
    {
        return new Dictionary<string, object>
        {
            { "autoFixWin11BitBlt", OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt }
        };
    }

    private DateTime Now()
    {
        return Clock.GetLocalNow().DateTime;
    }
}
