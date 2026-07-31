using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask;

internal sealed class OverlayWindowScheduler : IDisposable
{
    private readonly ILogger<TaskTriggerDispatcher> _logger;
    private readonly Func<IGameCapture?> _gameCaptureProvider;
    private readonly Func<TriggerActivityState> _triggerActivityStateProvider;
    private readonly Action<CaptureAvailabilityState> _availabilityStatePublisher;
    private readonly Action _skipNextFrameRequester;
    private readonly EventHandler _uiTaskStopTickEventHandler;

    private readonly System.Timers.Timer _overlayTimer = new();
    private readonly object _overlayLocker = new();

    private RECT _gameRect = RECT.Empty;
    private bool _prevGameActive;

    private User32.HWINEVENTHOOK _winEventHookMoveSize;
    private User32.HWINEVENTHOOK _winEventHookLocation;
    private User32.WinEventProc _winEventProc = null!;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public OverlayWindowScheduler(
        ILogger<TaskTriggerDispatcher> logger,
        Func<IGameCapture?> gameCaptureProvider,
        Func<TriggerActivityState> triggerActivityStateProvider,
        Action<CaptureAvailabilityState> availabilityStatePublisher,
        Action skipNextFrameRequester,
        EventHandler uiTaskStopTickEventHandler)
    {
        _logger = logger;
        _gameCaptureProvider = gameCaptureProvider;
        _triggerActivityStateProvider = triggerActivityStateProvider;
        _availabilityStatePublisher = availabilityStatePublisher;
        _skipNextFrameRequester = skipNextFrameRequester;
        _uiTaskStopTickEventHandler = uiTaskStopTickEventHandler;
        _overlayTimer.Elapsed += OnOverlayTimerElapsed;
    }

    public void Start(int interval)
    {
        _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);

        // 使用 SetWinEventHook 监听窗口移动和大小变化事件
        _winEventProc = WinEventCallback;
        var flags = (User32.WINEVENT)(WINEVENT_SKIPOWNPROCESS | WINEVENT_SKIPOWNTHREAD);
        _winEventHookMoveSize = User32.SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, default, _winEventProc, 0, 0, flags);
        _winEventHookLocation = User32.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, default, _winEventProc, 0, 0, flags);

        _overlayTimer.Interval = interval;
        StartTimer();
    }

    public void Stop()
    {
        StopTimer();
        _gameRect = RECT.Empty;
        _prevGameActive = false;
        PictureInPictureService.Hide(resetManual: true);
        HtmlMaskWindow.CloseAll();
        if (_winEventHookMoveSize != default)
        {
            User32.UnhookWinEvent(_winEventHookMoveSize);
            _winEventHookMoveSize = default;
        }

        if (_winEventHookLocation != default)
        {
            User32.UnhookWinEvent(_winEventHookLocation);
            _winEventHookLocation = default;
        }
    }

    public void StartTimer()
    {
        if (!_overlayTimer.Enabled)
        {
            _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
            _overlayTimer.Start();
        }
    }

    public void StopTimer()
    {
        if (_overlayTimer.Enabled)
        {
            _overlayTimer.Stop();
        }

        _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
    }

    private void OnOverlayTimerElapsed(object? sender, EventArgs e)
    {
        UpdateOverlayWindows(sender, e);
    }

    public void UpdateOverlayWindows(object? sender, EventArgs e)
    {
        var hasLock = false;
        try
        {
            Monitor.TryEnter(_overlayLocker, ref hasLock);
            if (!hasLock)
            {
                return;
            }

            // 检查截图器是否初始化
            var maskWindow = MaskWindow.Instance();
            var gameCapture = _gameCaptureProvider();
            if (gameCapture == null || !gameCapture.IsCapturing)
            {
                _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
                ChatUiHotkeyGuard.Reset();
                if (!TaskContext.Instance().SystemInfo.GameProcess.HasExited)
                {
                    _logger.LogError("截图器未初始化!");
                }
                else
                {
                    _logger.LogInformation("游戏已退出，BetterGI 自动停止截图器");
                }

                PictureInPictureService.Hide(resetManual: true);
                _uiTaskStopTickEventHandler.Invoke(sender, e);
                maskWindow.Invoke(maskWindow.HideSelf);
                HtmlMaskWindow.HideAll();
                return;
            }

            // 如果是最小化状态，直接不进行截图
            if (SystemControl.IsGenshinImpactMinimized())
            {
                _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
                ChatUiHotkeyGuard.Reset();
                PictureInPictureService.Hide();
                return;
            }

            // 检查游戏是否在前台
            var hasBackgroundTriggerToRun = false;
            var autoSkipConfig = TaskContext.Instance().Config.AutoSkipConfig;
            var shouldShowPictureInPicture = autoSkipConfig.Enabled
                                                 && autoSkipConfig.PictureInPictureEnabled
                                                 && !PictureInPictureService.IsManuallyClosed
                                                 && TaskControl.TaskSemaphore.CurrentCount == 1; // 没有任务持有锁（也就是没有任务正在运行）
            var active = SystemControl.IsGenshinImpactActive();
            var triggerActivityState = default(TriggerActivityState);
            if (!active)
            {
                ChatUiHotkeyGuard.Reset();
                // 检查游戏是否已结束
                if (TaskContext.Instance().SystemInfo.GameProcess.HasExited)
                {
                    _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
                    _logger.LogInformation("游戏已退出，BetterGI 自动停止截图器");
                    _uiTaskStopTickEventHandler.Invoke(sender, e);
                    return;
                }

                if (_prevGameActive)
                {
                    Debug.WriteLine("游戏窗口不在前台, 不再进行截屏");
                }

                var pName = SystemControl.GetActiveProcessName();
                if (pName != "Idle" && pName != "BetterGI" && pName != "YuanShen" && pName != "GenshinImpact" && pName != "Genshin Impact Cloud Game")
                {
                    // Debug.WriteLine(pName + "：hide mask window");
                    maskWindow.Invoke(() => { maskWindow.HideSelf(); });
                    HtmlMaskWindow.HideAll();
                }

                _prevGameActive = active;

                triggerActivityState = _triggerActivityStateProvider();
                hasBackgroundTriggerToRun = triggerActivityState.HasBackgroundTriggerToRun;

                if (!hasBackgroundTriggerToRun && shouldShowPictureInPicture)
                {
                    hasBackgroundTriggerToRun = true;
                }

                if (!hasBackgroundTriggerToRun)
                {
                    _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
                    // 没有后台运行的触发器，这次不再进行截图
                    PictureInPictureService.Hide();
                    return;
                }
            }
            else
            {
                PictureInPictureService.Hide(resetManual: true);
                // if (!_prevGameActive)
                // {
                maskWindow.BeginInvoke(() =>
                {
                    if (maskWindow.IsExist())
                    {
                        maskWindow.Show();
                        if (!_prevGameActive)
                        {
                            maskWindow.BringToTop();
                        }
                    }
                });
                HtmlMaskWindow.ShowAll();
                // }

                _prevGameActive = active;
                // // 移动游戏窗口的时候同步遮罩窗口的位置,此时不进行捕获
                if (SyncMaskWindowPosition())
                {
                    _skipNextFrameRequester();
                    _availabilityStatePublisher(new CaptureAvailabilityState(true, true, false, false));
                    return;
                }
            }

            if (active)
            {
                triggerActivityState = _triggerActivityStateProvider();
            }

            var hasEnabledTriggers = triggerActivityState.HasEnabledTriggers;
            if (!hasEnabledTriggers && !active)
            {
                _availabilityStatePublisher(CaptureAvailabilityState.Unavailable);
                // Debug.WriteLine("没有可用的触发器且不处于仅截屏状态, 不再进行截屏");
                return;
            }

            _availabilityStatePublisher(new CaptureAvailabilityState(
                true,
                active,
                hasBackgroundTriggerToRun,
                shouldShowPictureInPicture && !active));
        }
        finally
        {
            if (hasLock)
            {
                Monitor.Exit(_overlayLocker);
            }
        }
    }

    /// <summary>
    /// / 移动游戏窗口的时候同步遮罩窗口的位置
    /// </summary>
    /// <returns></returns>
    private bool SyncMaskWindowPosition()
    {
        var hWnd = TaskContext.Instance().GameHandle;
        var currentRect = SystemControl.GetCaptureRect(hWnd);
        if (_gameRect == RECT.Empty)
        {
            _gameRect = new RECT(currentRect);
        }
        else if (_gameRect != currentRect)
        {
            // // 后面大概可以取消掉这个判断，支持随意移动变化窗口 —— 现在已经可以取消了，但是一些Assets要重新加载
            // if ((_gameRect.Width != currentRect.Width || _gameRect.Height != currentRect.Height)
            //     && !SizeIsZero(_gameRect) && !SizeIsZero(currentRect))
            // {
            //     _logger.LogError("► 游戏窗口大小发生变化 {W}x{H}->{CW}x{CH}, 自动重启截图器中...", _gameRect.Width, _gameRect.Height, currentRect.Width, currentRect.Height);
            //     UiTaskStopTickEvent?.Invoke(null, EventArgs.Empty);
            //     UiTaskStartTickEvent?.Invoke(null, EventArgs.Empty);
            //     _logger.LogInformation("► 游戏窗口大小发生变化，截图器重启完成！");
            // }

            if ((_gameRect.Width != currentRect.Width || _gameRect.Height != currentRect.Height) && !SizeIsZero(_gameRect) && !SizeIsZero(currentRect))
            {
                _logger.LogError("► 游戏窗口大小发生变化 {W}x{H}->{CW}x{CH}, 无需重新启动截图器。", _gameRect.Width, _gameRect.Height, currentRect.Width, currentRect.Height);
            }

            _gameRect = new RECT(currentRect);
            TaskContext.Instance().SystemInfo.CaptureAreaRect = currentRect;
            MaskWindow.Instance().RefreshPosition();
            HtmlMaskWindow.UpdateAllPositions();
            return true;
        }

        return false;
    }

    private bool SizeIsZero(RECT rect)
    {
        return rect.Width == 0 || rect.Height == 0;
    }

    private void WinEventCallback(User32.HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        var target = TaskContext.Instance().GameHandle;
        if (target == IntPtr.Zero)
        {
            return;
        }

        if (idObject != 0)
        {
            return;
        }

        var hwndPtr = hwnd.DangerousGetHandle();
        if (hwndPtr == target)
        {
            SyncMaskWindowPosition();
        }
    }

    public void Dispose()
    {
        _overlayTimer.Elapsed -= OnOverlayTimerElapsed;
        _overlayTimer.Dispose();
    }
}
