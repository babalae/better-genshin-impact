using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Model.OverlayMetric;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BetterGenshinImpact.GameTask;

internal sealed class CaptureTriggerScheduler : IDisposable
{
    private readonly ILogger<TaskTriggerDispatcher> _logger;
    private readonly OverlayMetricsService? _metricsService;
    private readonly Func<IGameCapture?> _gameCaptureProvider;

    private readonly System.Timers.Timer _captureTimer = new();
    private readonly object _captureLifecycleLocker = new();
    private List<ITaskTrigger>? _triggers;

    private static readonly object _captureLocker = new();
    private static readonly object _triggerListLocker = new();
    private int _frameIndex;
    private int _skipNextFrame;
    private bool _acceptCaptureFrames;
    private int _activeCaptureFrames;

    private DateTime _prevManualGc = DateTime.MinValue;

    private GameUiCategory PrevGameUiCategory = GameUiCategory.Unknown; // 上一个UI类别
    private DateTime PrevGameUiChangeTime = DateTime.Now; // 上一次UI变化时间

    private CaptureAvailabilityState _availabilityState = CaptureAvailabilityState.Unavailable;

    public CaptureTriggerScheduler(
        ILogger<TaskTriggerDispatcher> logger,
        OverlayMetricsService? metricsService,
        Func<IGameCapture?> gameCaptureProvider)
    {
        _logger = logger;
        _metricsService = metricsService;
        _gameCaptureProvider = gameCaptureProvider;
        _captureTimer.Elapsed += OnCaptureTimerElapsed;
        //_timer.Tick += Tick;
    }

    public void ClearTriggers()
    {
        lock (_triggerListLocker)
        {
            GameTaskManager.ClearTriggers();
            _triggers?.Clear();
        }
    }

    public void SetTriggers(List<ITaskTrigger> list)
    {
        lock (_triggerListLocker)
        {
            _triggers = list;
        }
    }

    public bool AddTrigger(string name, object? externalConfig)
    {
        lock (_triggerListLocker)
        {
            if (GameTaskManager.AddTrigger(name, externalConfig))
            {
                SetTriggers(GameTaskManager.ConvertToTriggerList(true));
                return true;
            }

            return false;
        }
    }

    public TriggerActivityState GetTriggerActivityState()
    {
        lock (_triggerListLocker)
        {
            if (_triggers == null)
            {
                return default;
            }

            var exclusive = _triggers.FirstOrDefault(t => t is { IsEnabled: true, IsExclusive: true });
            var hasBackgroundTriggerToRun = exclusive != null
                ? exclusive.IsBackgroundRunning
                : _triggers.Any(t => t is { IsEnabled: true, IsBackgroundRunning: true });

            return new TriggerActivityState(
                _triggers.Exists(t => t.IsEnabled),
                hasBackgroundTriggerToRun);
        }
    }

    public void UpdateAvailabilityState(CaptureAvailabilityState state)
    {
        Volatile.Write(ref _availabilityState, state);
    }

    public void RequestSkipNextFrame()
    {
        Interlocked.Exchange(ref _skipNextFrame, 1);
    }

    public void Start(int interval)
    {
        // 启动定时器
        _frameIndex = 0;
        _captureTimer.Interval = interval;
        StartTimer();
    }

    public void Stop()
    {
        lock (_captureLifecycleLocker)
        {
            // 先禁止新帧进入，再等待已经进入的帧完整退出，避免截图资源被提前释放。
            _acceptCaptureFrames = false;
            _captureTimer.Stop();
            while (_activeCaptureFrames > 0)
            {
                Monitor.Wait(_captureLifecycleLocker);
            }
        }
    }

    public void StartTimer()
    {
        lock (_captureLifecycleLocker)
        {
            _acceptCaptureFrames = true;
            if (!_captureTimer.Enabled)
            {
                _captureTimer.Start();
            }
        }
    }

    public void StopTimer()
    {
        if (_captureTimer.Enabled)
        {
            _captureTimer.Stop();
        }
    }

    private void OnCaptureTimerElapsed(object? sender, EventArgs e)
    {
        ProcessCaptureFrame(sender, e);
    }

    public void ProcessCaptureFrame(object? sender, EventArgs e)
    {
        lock (_captureLifecycleLocker)
        {
            if (!_acceptCaptureFrames)
            {
                return;
            }

            _activeCaptureFrames++;
        }

        try
        {
            ProcessCaptureFrameCore(sender, e);
        }
        finally
        {
            lock (_captureLifecycleLocker)
            {
                _activeCaptureFrames--;
                if (_activeCaptureFrames == 0)
                {
                    Monitor.PulseAll(_captureLifecycleLocker);
                }
            }
        }
    }

    private void ProcessCaptureFrameCore(object? sender, EventArgs e)
    {
        var hasLock = false;
        var tickMetrics = new DispatcherTickMetrics();
        try
        {
            // 上一帧还没处理完时只记录跳过次数，不等待锁；等待时间不应混入本轮处理耗时。
            Monitor.TryEnter(_captureLocker, ref hasLock);
            if (!hasLock)
            {
                _metricsService?.RecordSkippedTick();
                // 正在执行时跳过
                return;
            }

            var availabilityState = Volatile.Read(ref _availabilityState);
            if (!availabilityState.CanProcessFrame)
            {
                return;
            }

            if (Interlocked.Exchange(ref _skipNextFrame, 0) != 0)
            {
                return;
            }

            var gameCapture = _gameCaptureProvider();
            if (gameCapture == null || !gameCapture.IsCapturing)
            {
                return;
            }

            var triggerActivityState = GetTriggerActivityState();
            var hasEnabledTriggers = triggerActivityState.HasEnabledTriggers;
            if (!hasEnabledTriggers && !availabilityState.IsGameActive)
            {
                // Debug.WriteLine("没有可用的触发器且不处于仅截屏状态, 不再进行截屏");
                return;
            }

            // 帧序号自增 1分钟后归零(MaxFrameIndexSecond)
            _frameIndex = (_frameIndex + 1) % (int)(CaptureContent.MaxFrameIndexSecond * 1000d / _captureTimer.Interval);

            var speedTimer = new SpeedTimer();
            // 从真正开始截图处计时，前面的窗口状态检查不计入 BetterGI 本轮处理耗时。
            tickMetrics.Begin();
            // 捕获游戏画面
            var captureFrame = gameCapture.Capture();
            var bitmap = captureFrame?.Frame;
            tickMetrics.EndCapture();
            speedTimer.Record("截图");

            if (bitmap == null)
            {
                _logger.LogWarning("截图失败!");
                return;
            }

            if (availabilityState.ShouldUpdatePictureInPicture)
            {
                PictureInPictureService.Update(bitmap);
            }
            else
            {
                PictureInPictureService.Hide();
            }

            // 循环执行所有触发器 有独占状态的触发器的时候只执行独占触发器
            using var content = new CaptureContent(bitmap, _frameIndex, _captureTimer.Interval);
            ChatUiHotkeyGuard.UpdateVisualState(Bv.DetectChatUi(content.CaptureRectArea));

            if (!hasEnabledTriggers)
            {
                return;
            }

            lock (_triggerListLocker)
            {
                // 遮罩调度器发布的状态只用于帧处理门控；窗口可能在两个定时器之间切换，
                // 因此选择触发器时必须重新检查当前窗口状态，避免向其他前台程序发送输入。
                if (SystemControl.IsGenshinImpactMinimized())
                {
                    return;
                }

                var backgroundTriggersOnly = availabilityState.BackgroundTriggersOnly
                                             || !SystemControl.IsGenshinImpactActive();
                var needRunTriggers = new List<ITaskTrigger>(); // 最终要执行的触发器列表
                var exclusiveTrigger = _triggers!.FirstOrDefault(t => t is { IsEnabled: true, IsExclusive: true });
                if (exclusiveTrigger != null)
                {
                    if (!backgroundTriggersOnly || exclusiveTrigger.IsBackgroundRunning)
                    {
                        needRunTriggers.Add(exclusiveTrigger);
                    }
                }
                else
                {
                    var runningTriggers = _triggers!.Where(t => t.IsEnabled);
                    if (backgroundTriggersOnly)
                    {
                        runningTriggers = runningTriggers.Where(t => t.IsBackgroundRunning);
                    }

                    needRunTriggers.AddRange(runningTriggers);
                }

                if (needRunTriggers.Count > 0)
                {
                    // 判断当前UI
                    content.CurrentGameUiCategory = Bv.WhichGameUiForTriggers(content.CaptureRectArea);

                    if (content.CurrentGameUiCategory != PrevGameUiCategory)
                    {
                        PrevGameUiChangeTime = DateTime.Now;
                    }

                    foreach (var trigger in needRunTriggers)
                    {
                        if ((PrevGameUiCategory != content.CurrentGameUiCategory || (DateTime.Now - PrevGameUiChangeTime).TotalSeconds <= 30) // UI变化了后的30s内则所有触发器执行一遍
                            || trigger.SupportedGameUiCategory == content.CurrentGameUiCategory)
                        {
                            // 非后台触发器在真正执行前再次校验前台状态，缩短检查与输入之间的竞态窗口。
                            // 游戏最小化时不执行任何触发器，保持原 Tick 的最小化处理语义。
                            if (!CanRunTriggerNow(trigger))
                            {
                                continue;
                            }

                            // 触发器耗时只累计触发器执行本体，便于和截图耗时、总处理耗时拆开观察。
                            var triggerStart = Stopwatch.GetTimestamp();
                            trigger.OnCapture(content);
                            tickMetrics.AddTriggerCost(triggerStart);
                            speedTimer.Record(trigger.Name);
                        }
                    }

                    PrevGameUiCategory = content.CurrentGameUiCategory;
                }
            }

            speedTimer.DebugPrint();
        }
        finally
        {
            tickMetrics.EndProcessing();

            if ((DateTime.Now - _prevManualGc).TotalSeconds > 2)
            {
                GC.Collect();
                _prevManualGc = DateTime.Now;
            }

            if (hasLock)
            {
                Monitor.Exit(_captureLocker);
            }

            if (tickMetrics.IsEnabled)
            {
                // 释放调度锁后再发布指标，避免 UI 订阅回调参与实时触发器锁竞争。
                tickMetrics.Publish(_metricsService);
            }
        }
    }

    private static bool CanRunTriggerNow(ITaskTrigger trigger)
    {
        if (SystemControl.IsGenshinImpactMinimized())
        {
            return false;
        }

        return trigger.IsBackgroundRunning || SystemControl.IsGenshinImpactActive();
    }

    public void Dispose()
    {
        _captureTimer.Elapsed -= OnCaptureTimerElapsed;
        _captureTimer.Dispose();
    }
}
