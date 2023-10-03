using Fischless.WindowCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using BetterGenshinImpact.View;
using Vanara.PInvoke;
using System.Windows.Threading;

namespace BetterGenshinImpact.GameTask
{
    public class TaskDispatcher
    {
        private readonly ILogger<TaskDispatcher> _logger = App.GetLogger<TaskDispatcher>();

        private readonly System.Timers.Timer _timer = new();
        private List<ITaskTrigger>? _triggers;

        private IWindowCapture? _capture;

        private static readonly object _locker = new();
        private int _frameIndex = 0;

        private RECT _gameRect = RECT.Empty;
        private bool _prevGameActive;


        public TaskDispatcher()
        {
            _timer.Elapsed += Tick;
            //_timer.Tick += Tick;
        }

        public void Start(IntPtr hWnd, CaptureModes mode, int interval = 50)
        {
            // 初始化任务上下文(一定要在初始化触发器前完成)
            TaskContext.Instance().Init(hWnd);
            // 初始化触发器
            _triggers = GameTaskManager.LoadTriggers();

            // 初始化截图器
            _capture = WindowCaptureFactory.Create(mode);
            _capture.IsClientEnabled = true;
            _capture.Start(hWnd);

            // 启动定时器
            _frameIndex = 0;
            _timer.Interval = interval;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _capture?.Stop();
            _gameRect = RECT.Empty;
            _prevGameActive = false;
        }

        public void Tick(object? sender, EventArgs e)
        {
            var hasLock = false;
            try
            {
                Monitor.TryEnter(_locker, ref hasLock);
                if (!hasLock)
                {
                    // 正在执行时跳过
                    return;
                }

                // 检查截图器是否初始化
                if (_capture == null || !_capture.IsCapturing)
                {
                    _logger.LogError("截图器未初始化!");
                    Stop();
                    return;
                }

                // 检查游戏是否在前台
                var active = SystemControl.IsGenshinImpactActive();
                var maskWindow = MaskWindow.Instance();
                if (!active)
                {
                    if (_prevGameActive)
                    {
                        maskWindow.Invoke(() => { maskWindow.Hide(); });
                        Debug.WriteLine("游戏窗口不在前台, 不再进行截屏");
                    }

                    _prevGameActive = active;
                    return;
                }
                else
                {
                    if (!_prevGameActive)
                    {
                        maskWindow.Invoke(() => { maskWindow.Show(); });
                    }
                    _prevGameActive = active;
                    // 移动游戏窗口的时候同步遮罩窗口的位置,此时不进行捕获
                    if (SyncMaskWindowPosition())
                    {
                        return;
                    }
                }

                // 帧序号自增 1分钟后归零(MaxFrameIndexSecond)
                _frameIndex = (_frameIndex + 1) % (int)(CaptureContent.MaxFrameIndexSecond * 1000d / _timer.Interval);

                if (_triggers == null || !_triggers.Exists(t => t.IsEnabled))
                {
                    Debug.WriteLine("没有可用的触发器, 不再进行截屏");
                    return;
                }

                // 捕获游戏画面
                var bitmap = _capture.Capture();
                if (bitmap == null)
                {
                    _logger.LogWarning("截图失败!");
                    return;
                }

                // 循环执行所有触发器 有独占状态的触发器的时候只执行独占触发器
                var content = new CaptureContent(bitmap, _frameIndex, _timer.Interval);
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
            finally
            {
                if (hasLock)
                {
                    Monitor.Exit(_locker);
                }
            }
        }

        /// <summary>
        /// / 移动游戏窗口的时候同步遮罩窗口的位置
        /// </summary>
        /// <returns></returns>
        private bool SyncMaskWindowPosition()
        {
            var currentRect = SystemControl.GetWindowRect(TaskContext.Instance().GameHandle);
            if (_gameRect == RECT.Empty)
            {
                _gameRect = new RECT(currentRect);
            }
            else if (_gameRect != currentRect)
            {
                _gameRect = new RECT(currentRect);
                var maskWindow = MaskWindow.Instance();
                maskWindow.Invoke(() =>
                {
                    maskWindow.Left = currentRect.left;
                    maskWindow.Top = currentRect.top;
                    maskWindow.Width = currentRect.Width;
                    maskWindow.Height = currentRect.Height;
                });
                return true;
            }

            return false;
        }
    }
}