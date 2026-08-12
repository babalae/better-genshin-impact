using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View;
using Fischless.GameCapture;
using Fischless.GameCapture.Graphics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using Rect = OpenCvSharp.Rect;

namespace BetterGenshinImpact.GameTask
{
    public class TaskTriggerDispatcher : IDisposable
    {
        private readonly ILogger<TaskTriggerDispatcher> _logger = App.GetLogger<TaskTriggerDispatcher>();
        private readonly OverlayMetricsService? _metricsService = App.GetService<OverlayMetricsService>();
        private readonly CustomHtmlMaskService? _customHtmlMaskService = App.GetService<CustomHtmlMaskService>();

        private static TaskTriggerDispatcher? _instance;

        private readonly CaptureTriggerScheduler _captureTriggerScheduler;
        private readonly OverlayWindowScheduler _overlayWindowScheduler;

        public IGameCapture? GameCapture { get; private set; }

        public event EventHandler? UiTaskStopTickEvent;

        public event EventHandler? UiTaskStartTickEvent;

        public TaskTriggerDispatcher()
        {
            _instance = this;
            _captureTriggerScheduler = new CaptureTriggerScheduler(
                _logger,
                _metricsService,
                () => GameCapture);
            _overlayWindowScheduler = new OverlayWindowScheduler(
                _logger,
                _customHtmlMaskService,
                () => GameCapture,
                _captureTriggerScheduler.GetTriggerActivityState,
                _captureTriggerScheduler.UpdateAvailabilityState,
                _captureTriggerScheduler.RequestSkipNextFrame,
                OnUiTaskStopTick);
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
            _captureTriggerScheduler.ClearTriggers();
        }

        public void SetTriggers(List<ITaskTrigger> list)
        {
            _captureTriggerScheduler.SetTriggers(list);
        }

        public bool AddTrigger(string name, object? externalConfig)
        {
            return _captureTriggerScheduler.AddTrigger(name, externalConfig);
        }

        public void Start(IntPtr hWnd, CaptureModes mode, int interval = 50)
        {
            // 初始化截图器
            ChatUiHotkeyGuard.Reset();
            GameCapture = GameCaptureFactory.Create(mode);
            // 激活窗口 保证后面能够正常获取窗口信息
            SystemControl.ActivateWindow(hWnd);

            // 初始化任务上下文(一定要在初始化触发器前完成)
            TaskContext.Instance().Init(hWnd);

            // 初始化触发器(一定要在任务上下文初始化完毕后使用)
            _captureTriggerScheduler.SetTriggers(GameTaskManager.LoadInitialTriggers());
            GameLoadingTrigger.GlobalEnabled = TaskContext.Instance().Config.GenshinStartConfig.AutoEnterGameEnabled;

            // if (GraphicsCapture.IsHdrEnabled(hWnd))
            // {
            //     _logger.LogError("游戏窗口在HDR模式下无法获取正常颜色的截图，请关闭HDR模式！");
            // }

            // 启动截图
            GameCapture.Start(hWnd,
                new Dictionary<string, object>()
                {
                    { "autoFixWin11BitBlt", OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt }
                }
            );

            _overlayWindowScheduler.Start(interval);
            _captureTriggerScheduler.Start(interval);
        }

        public void Stop()
        {
            // 等待所有在途帧退出后，才能安全释放截图资源。
            _captureTriggerScheduler.Stop();
            _overlayWindowScheduler.StopTimer();
            ChatUiHotkeyGuard.Reset();
            GameCapture?.Stop();
            _overlayWindowScheduler.Stop();
        }

        public void StartTimer()
        {
            _overlayWindowScheduler.StartTimer();
            _captureTriggerScheduler.StartTimer();
        }

        public void StopTimer()
        {
            _captureTriggerScheduler.StopTimer();
            _overlayWindowScheduler.StopTimer();

            ChatUiHotkeyGuard.Reset();
        }

        public void Dispose()
        {
            Stop();
            _captureTriggerScheduler.Dispose();
            _overlayWindowScheduler.Dispose();
        }

        public void Tick(object? sender, EventArgs e)
        {
            _captureTriggerScheduler.ProcessCaptureFrame(sender, e);
        }

        private void OnUiTaskStopTick(object? sender, EventArgs e)
        {
            UiTaskStopTickEvent?.Invoke(sender, e);
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
