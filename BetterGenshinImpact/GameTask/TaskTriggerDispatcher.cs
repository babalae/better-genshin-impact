﻿using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fischless.GameCapture;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask
{
    public class TaskTriggerDispatcher : IDisposable
    {
        private readonly ILogger<TaskTriggerDispatcher> _logger = App.GetLogger<TaskTriggerDispatcher>();

        private readonly System.Timers.Timer _timer = new();
        private List<ITaskTrigger>? _triggers;

        private IGameCapture? _capture;

        private static readonly object _locker = new();
        private int _frameIndex = 0;

        private RECT _gameRect = RECT.Empty;
        private bool _prevGameActive;

        public bool Enabled => _timer.Enabled;


        public TaskTriggerDispatcher()
        {
            _timer.Elapsed += Tick;
            //_timer.Tick += Tick;
        }

        public void Start(IntPtr hWnd, CaptureModes mode, int interval = 50)
        {
            // 初始化截图器
            _capture = GameCaptureFactory.Create(mode);
            // 激活窗口 保证后面能够正常获取窗口信息
            SystemControl.ActivateWindow(hWnd);

            // 初始化任务上下文(一定要在初始化触发器前完成)
            TaskContext.Instance().Init(hWnd);
            PrintSystemInfo();

            // 初始化触发器(一定要在任务上下文初始化完毕后使用)
            _triggers = GameTaskManager.LoadTriggers();

            // 启动截图
            _capture.Start(hWnd);

            // 启动定时器
            _frameIndex = 0;
            _timer.Interval = interval;
            if (!_timer.Enabled)
            {
                _timer.Start();
            }
        }

        private void PrintSystemInfo()
        {
            var systemInfo = TaskContext.Instance().SystemInfo;
            var width = systemInfo.GameScreenSize.Width;
            var height = systemInfo.GameScreenSize.Height;
            _logger.LogInformation("当前游戏分辨率{Width}x{Height}，素材缩放比率{Scale}",
                width, height, systemInfo.AssetScale.ToString("F"));

            if (width * 9 != height * 16)
            {
                _logger.LogWarning("当前游戏分辨率不是16:9，部分功能可能无法正常使用");
            }
        }

        public void Stop()
        {
            _timer.Stop();
            _capture?.Stop();
            _gameRect = RECT.Empty;
            _prevGameActive = false;
        }

        public void Dispose() => Stop();

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
                        Debug.WriteLine("游戏窗口不在前台, 不再进行截屏");
                    }

                    var pName = SystemControl.GetActiveProcessName();
                    if (pName != "BetterGenshinImpact" && pName != "YuanShen" && pName != "GenshinImpact" && pName != "Genshin Impact Cloud Game")
                    {
                        maskWindow.Invoke(() => { maskWindow.Hide(); });
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

                var sw = new Stopwatch();
                sw.Start();
                // 捕获游戏画面
                var bitmap = _capture.Capture();
                sw.Stop();
                //Debug.WriteLine("截图耗时:" + sw.ElapsedMilliseconds);
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
                        sw.Reset();
                        sw.Start();
                        trigger.OnCapture(content);
                        sw.Stop();
                        if (sw.ElapsedMilliseconds > 0)
                        {
                            Debug.WriteLine($"{trigger.Name}耗时:" + sw.ElapsedMilliseconds);
                        }
                    }
                }
                //if (_frameIndex / content.FrameRate % 2 == 0)
                //{
                //    GC.Collect();
                //}
                content.Dispose();
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
            var hWnd = TaskContext.Instance().GameHandle;
            var currentRect = SystemControl.GetCaptureRect(hWnd);
            if (_gameRect == RECT.Empty)
            {
                _gameRect = new RECT(currentRect);
            }
            else if (_gameRect != currentRect)
            {
                // 后面大概可以取消掉这个判断，支持随意移动变化窗口 —— 不支持 需要考虑的问题太多了
                if (_gameRect.Width != currentRect.Width || _gameRect.Height != currentRect.Height)
                {
                    _logger.LogError("游戏窗口大小发生变化, 请重新启动捕获程序!");
                }

                _gameRect = new RECT(currentRect);
                double scale = TaskContext.Instance().DpiScale;
                MaskWindow.Instance().RefreshPosition(currentRect, scale);
                return true;
            }

            return false;
        }
    }
}