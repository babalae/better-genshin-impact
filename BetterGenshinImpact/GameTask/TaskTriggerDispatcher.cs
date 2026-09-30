using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.Runtime;
using Fischless.GameCapture.Graphics;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Model;
using BetterGenshinImpact.Service.Model.OverlayMetric;
using Vanara.PInvoke;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask
{
    public class TaskTriggerDispatcher : IDisposable
    {
        private readonly ILogger<TaskTriggerDispatcher> _logger = App.GetLogger<TaskTriggerDispatcher>();
        private readonly OverlayMetricsService? _metricsService = App.GetService<OverlayMetricsService>();
        private readonly CustomHtmlMaskService? _customHtmlMaskService = App.GetService<CustomHtmlMaskService>();

        private static TaskTriggerDispatcher? _instance;

        private readonly System.Timers.Timer _timer = new();
        private List<ITaskTrigger>? _triggers;

        /// <summary>
        /// 当前绑定的运行环境，由 GameRuntimeService 通过 Start / Stop 设置。Tick 线程只读
        /// </summary>
        private volatile GameRuntime? _runtime;

        /// <summary>
        /// 当前运行环境的截图器，未启动时为 null
        /// </summary>
        public IGameCapture? GameCapture => _runtime?.Capture;

        private static readonly object _locker = new();
        private int _frameIndex = 0;

        private RECT _gameRect = RECT.Empty;
        private bool _prevGameActive;

        private DateTime _prevManualGc = DateTime.MinValue;

        private static readonly object _triggerListLocker = new();

        /// <summary>
        /// 截图器停止或游戏已退出。由 GameRuntimeService 订阅并停止运行环境
        /// </summary>
        public event EventHandler? UiTaskStopTickEvent;

        private GameUiCategory PrevGameUiCategory = GameUiCategory.Unknown; // 上一个UI类别
        private DateTime PrevGameUiChangeTime = DateTime.Now; // 上一次UI变化时间
        

        public TaskTriggerDispatcher()
        {
            _instance = this;
            _timer.Elapsed += Tick;
            //_timer.Tick += Tick;
        }

        public static TaskTriggerDispatcher Instance()
        {
            if (_instance == null)
            {
                throw new Exception("请先在启动页启动BetterGI，如果已经启动请重启");
            }

            return _instance;
        }

        public static IGameCapture GlobalGameCapture
        {
            get
            {
                _instance = Instance();

                if (_instance.GameCapture == null)
                {
                    throw new Exception("截图器未初始化!");
                }

                return _instance.GameCapture;
            }
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

        /// <summary>
        /// 开始调度。运行环境的截图器、输入和 TaskContext 已由 GameRuntimeService 准备好
        /// </summary>
        public void Start(GameRuntime runtime, int interval = 50)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            ChatUiHotkeyGuard.Reset();
            _runtime = runtime;

            // 初始化触发器(一定要在任务上下文初始化完毕后使用)
            _triggers = GameTaskManager.LoadInitialTriggers();
            GameLoadingTrigger.GlobalEnabled = TaskContext.Instance().Config.GenshinStartConfig.AutoEnterGameEnabled;

            // 窗口移动、缩放时同步遮罩位置（Tick 中也会轮询）
            runtime.Window.ViewportChanged += OnViewportChanged;

            // 启动定时器
            _frameIndex = 0;
            _timer.Interval = interval;
            if (!_timer.Enabled)
            {
                _timer.Start();
            }
        }

        /// <summary>
        /// 停止调度。截图器和窗口监听随运行环境一起由 GameRuntimeService 释放
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            ChatUiHotkeyGuard.Reset();
            var runtime = _runtime;
            if (runtime != null)
            {
                runtime.Window.ViewportChanged -= OnViewportChanged;
                _runtime = null;
            }

            _gameRect = RECT.Empty;
            _prevGameActive = false;
            PictureInPictureService.Hide(resetManual: true);
            HtmlMaskWindow.CloseAll();
        }

        public void StartTimer()
        {
            if (!_timer.Enabled)
            {
                _timer.Start();
            }
        }

        public void StopTimer()
        {
            if (_timer.Enabled)
            {
                _timer.Stop();
            }

            ChatUiHotkeyGuard.Reset();
        }

        public void Dispose()
        {
            Stop();
        }

        public void Tick(object? sender, EventArgs e)
        {
            var hasLock = false;
            var tickMetrics = new DispatcherTickMetrics();
            try
            {
                // 上一帧还没处理完时只记录跳过次数，不等待锁；等待时间不应混入本轮处理耗时。
                Monitor.TryEnter(_locker, ref hasLock);
                if (!hasLock)
                {
                    _metricsService?.RecordSkippedTick();
                    // 正在执行时跳过
                    return;
                }

                var runtime = _runtime;
                if (runtime == null)
                {
                    // Stop 之后残留的一次调度
                    return;
                }

                var window = runtime.Window;
                var gameCapture = runtime.Capture;

                // 检查截图器是否在运行、游戏是否已退出
                var maskWindow = MaskWindow.Instance();
                var alive = window.IsAlive;
                if (!gameCapture.IsCapturing || !alive)
                {
                    if (!ReferenceEquals(_runtime, runtime))
                    {
                        // 本轮调度期间运行环境已被主动停止并释放，不是游戏退出
                        return;
                    }

                    ChatUiHotkeyGuard.Reset();
                    if (alive)
                    {
                        _logger.LogError("截图器未初始化!");
                    }
                    else
                    {
                        _logger.LogInformation("游戏已退出，BetterGI 自动停止截图器");
                    }

                    PictureInPictureService.Hide(resetManual: true);
                    UiTaskStopTickEvent?.Invoke(sender, e);
                    maskWindow.Invoke(maskWindow.HideSelf);
                    HtmlMaskWindow.HideAll();
                    return;
                }
                
                // 如果是最小化状态，直接不进行截图
                if (window.IsMinimized)
                {
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
                var active = window.IsForeground;
                if (!active)
                {
                    ChatUiHotkeyGuard.Reset();

                    if (_prevGameActive)
                    {
                        Debug.WriteLine("游戏窗口不在前台, 不再进行截屏");
                    }

                    if (!IsForegroundOwnedByBetterGiOrGame(window))
                    {
                        maskWindow.Invoke(() => { maskWindow.HideSelf(); });
                        HtmlMaskWindow.HideAll();
                    }

                    _prevGameActive = active;

                    // 输入依赖前台时，失焦后只执行后台触发器；
                    // 输入不依赖前台的运行环境（网页版）失焦后照常执行全部触发器
                    if (window.RequiresForeground)
                    {
                        if (_triggers != null)
                        {
                            lock (_triggerListLocker)
                            {
                                var exclusive = _triggers.FirstOrDefault(t => t is { IsEnabled: true, IsExclusive: true });
                                if (exclusive != null)
                                {
                                    hasBackgroundTriggerToRun = exclusive.IsBackgroundRunning;
                                }
                                else
                                {
                                    hasBackgroundTriggerToRun = _triggers.Any(t => t is { IsEnabled: true, IsBackgroundRunning: true });
                                }
                            }
                        }

                        if (!hasBackgroundTriggerToRun && shouldShowPictureInPicture)
                        {
                            hasBackgroundTriggerToRun = true;
                        }

                        if (!hasBackgroundTriggerToRun)
                        {
                            // 没有后台运行的触发器，这次不再进行截图
                            PictureInPictureService.Hide();
                            return;
                        }
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
                    _customHtmlMaskService?.ShowIfEnabled();
                    HtmlMaskWindow.ShowAll();
                    // }

                    _prevGameActive = active;
                    // // 移动游戏窗口的时候同步遮罩窗口的位置,此时不进行捕获
                    if (SyncMaskWindowPosition())
                    {
                        return;
                    }
                }

                var hasEnabledTriggers = _triggers != null && _triggers.Exists(t => t.IsEnabled);
                if (!hasEnabledTriggers && !active)
                {
                    // Debug.WriteLine("没有可用的触发器且不处于仅截屏状态, 不再进行截屏");
                    return;
                }

                // 帧序号自增 1分钟后归零(MaxFrameIndexSecond)
                _frameIndex = (_frameIndex + 1) % (int)(CaptureContent.MaxFrameIndexSecond * 1000d / _timer.Interval);

                var speedTimer = new SpeedTimer();
                // 从真正开始截图处计时，前面的窗口状态检查不计入 BetterGI 本轮处理耗时。
                // 仅在遮罩指标开启时启动采样：未 Begin 时 EndCapture/AddTriggerCost/Publish 均按设计空转。
                if (_metricsService is { IsEnabled: true })
                {
                    tickMetrics.Begin();
                }
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

                if (shouldShowPictureInPicture && !active)
                {
                    PictureInPictureService.Update(bitmap);
                }
                else
                {
                    PictureInPictureService.Hide();
                }

                // 循环执行所有触发器 有独占状态的触发器的时候只执行独占触发器
                using var content = new CaptureContent(bitmap, _frameIndex, _timer.Interval);
                ChatUiHotkeyGuard.UpdateVisualState(Bv.DetectChatUi(content.CaptureRectArea));

                if (!hasEnabledTriggers)
                {
                    return;
                }

                lock (_triggerListLocker)
                {
                    var needRunTriggers = new List<ITaskTrigger>(); // 最终要执行的触发器列表
                    var exclusiveTrigger = _triggers!.FirstOrDefault(t => t is { IsEnabled: true, IsExclusive: true });
                    if (exclusiveTrigger != null)
                    {
                        needRunTriggers.Add(exclusiveTrigger);
                    }
                    else
                    {
                        var runningTriggers = _triggers!.Where(t => t.IsEnabled);
                        if (hasBackgroundTriggerToRun)
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
                    Monitor.Exit(_locker);
                }

                if (tickMetrics.IsEnabled)
                {
                    // 释放调度锁后再发布指标，避免 UI 订阅回调参与实时触发器锁竞争。
                    tickMetrics.Publish(_metricsService);
                }
            }
        }

        /// <summary>
        /// / 移动游戏窗口的时候同步遮罩窗口的位置
        /// </summary>
        /// <returns></returns>
        private bool SyncMaskWindowPosition()
        {
            var runtime = _runtime;
            if (runtime == null)
            {
                return false;
            }

            var currentRect = runtime.Window.Viewport.ScreenRect;
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

        /// <summary>
        /// 游戏窗口移动或缩放（由 IGameWindow 在 UI 线程上通知）
        /// </summary>
        private void OnViewportChanged(object? sender, EventArgs e)
        {
            SyncMaskWindowPosition();
        }

        /// <summary>
        /// 游戏不在前台时，前台窗口属于 BetterGI 自身或游戏进程（或者没有前台窗口）就保留遮罩。
        /// 按进程 ID 判断，另一个 BetterGI 实例在前台时本实例的遮罩会隐藏，避免多个置顶遮罩叠在一起
        /// </summary>
        private static bool IsForegroundOwnedByBetterGiOrGame(IGameWindow window)
        {
            var foreground = User32.GetForegroundWindow();
            if (foreground.IsNull)
            {
                return true;
            }

            _ = User32.GetWindowThreadProcessId(foreground, out var pid);
            return pid == 0 || pid == (uint)Environment.ProcessId || pid == (uint)window.ProcessId;
        }

        public void TakeScreenshot()
        {
            try
            {
                var path = Global.Absolute($@"log\screenshot\");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                Mat mat;
                try
                {
                    mat = TaskControl.CaptureGameImage(GameCapture);
                }
                catch (Exception)
                {
                    _logger.LogInformation("截图失败，未获取到图像");
                    return;
                }

                var name = $@"{DateTime.Now:yyyyMMddHHmmssffff}.png";
                var savePath = Global.Absolute($@"log\screenshot\{name}");
                if (TaskContext.Instance().Config.CommonConfig.ScreenshotUidCoverEnabled)
                {
                    var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
                    var rect = new Rect((int)(mat.Width - MaskWindowConfig.UidCoverRightBottomRect.X * assetScale),
                        (int)(mat.Height - MaskWindowConfig.UidCoverRightBottomRect.Y * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Width * assetScale),
                        (int)(MaskWindowConfig.UidCoverRightBottomRect.Height * assetScale));
                    mat.Rectangle(rect, Scalar.White, -1);
                    Cv2.ImWrite(savePath, mat);
                }
                else
                {
                    Cv2.ImWrite(savePath, mat);
                }

                mat.Dispose();

                _logger.LogInformation("截图已保存: {Name}", name);
            }
            catch (Exception e)
            {
                _logger.LogError("截图保存失败: {Message}", e.Message);
                _logger.LogDebug("截图保存失败: {StackTrace}", e.StackTrace);
            }
        }
    }
}
