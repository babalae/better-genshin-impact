using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using System.Windows;
using Vision.Recognition.Task;
using Vision.WindowCapture;
using Windows.Win32.Foundation;

namespace BetterGenshinImpact.GameTask
{
    public class TaskDispatcher
    {
        private readonly ILogger<TaskDispatcher> _logger = App.GetLogger<TaskDispatcher>();

        private readonly Timer _timer = new();
        private readonly List<ITaskTrigger> _triggers;

        private IWindowCapture? _capture;


        private int _frameIndex = 0;
        private int _frameRate = 30;



        public TaskDispatcher()
        {
            _triggers = GameTaskManager.LoadTriggers();

            _timer.Elapsed += Tick;
            //_timer.Tick += Tick;
        }

        public void Start(CaptureMode mode, int frameRate = 30)
        {
            HWND hWnd = (HWND)SystemControl.FindGenshinImpactHandle();
            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Show("未找到原神窗口");
                return;
            }
            TaskContext.Instance().GameHandle = hWnd;

            _frameRate = frameRate;

            _capture = WindowCaptureFactory.Create(mode);
            _capture.Start(hWnd);

            _frameIndex = 0;
            _timer.Interval = Convert.ToInt32(1000d / frameRate);
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _capture?.Stop();
        }

        public void Tick(object? sender, EventArgs e)
        {
            // 检查截图器是否初始化
            if (_capture == null || !_capture.IsCapturing)
            {
                _logger.LogError("截图器未初始化!");
                Stop();
                return;
            }
            
            // 检查游戏是否在前台
            if (!SystemControl.IsGenshinImpactActive())
            {
                return;
            }

            // 帧序号自增 1分钟后归零(MaxFrameIndexSecond)
            _frameIndex = (_frameIndex + 1) % (_frameRate * CaptureContent.MaxFrameIndexSecond);

            // 捕获游戏画面
            //var sw = new Stopwatch();
            //sw.Start();
            var bitmap = _capture.Capture();
            //sw.Stop();
            //Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);

            if (bitmap == null)
            {
                _logger.LogWarning("截图失败!");
                return;
            }

            // 循环执行所有触发器 有独占状态的触发器的时候只执行独占触发器
            var content = new CaptureContent(bitmap, _frameIndex, _frameRate);
            var exclusiveTrigger = _triggers.FirstOrDefault(t => t is { IsEnabled: true, IsExclusive: true });
            if (exclusiveTrigger != null)
            {
                exclusiveTrigger.OnCapture(content);
            }
            else
            {
                foreach (var trigger in _triggers.Where(trigger => trigger.IsEnabled))
                {
                    trigger.OnCapture(content);
                }
            }
        }
    }
}