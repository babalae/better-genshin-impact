using System;
using System.Windows.Forms;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Platform.Wine;

namespace BetterGenshinImpact.Core.Monitor
{
    public partial class MouseKeyMonitor
    {
        private WinePlatformAddon _wineAddon;

        /// <summary>
        /// [Wine专用] 尝试初始化轮询机制
        /// </summary>
        private void TrySubscribeWinePolling()
        {
            // 如果不是 Wine，直接返回，确保安全
            if (!WinePlatformAddon.IsRunningOnWine)
                return;

            // 初始化 Addon 工具
            _wineAddon = new WinePlatformAddon(null);

            // 启动轮询，指向下方的 PollingLoop
            _wineAddon.StartPolling(PollingLoop, 15);
        }

        /// <summary>
        /// [Wine专用] 轮询循环逻辑
        /// </summary>
        private void PollingLoop()
        {
            // 1. 处理 F 键 (交互键)
            bool isFDown = WinePlatformAddon.IsKeyDown(_pickUpKey);
            if (isFDown)
            {
                if (_firstFKeyDownTime == DateTime.MaxValue)
                {
                    _firstFKeyDownTime = DateTime.Now; // 刚按下
                }
                else
                {
                    // 按住中：检查是否达到连发阈值
                    var timeSpan = DateTime.Now - _firstFKeyDownTime;
                    if (
                        timeSpan.TotalMilliseconds > 200
                        && TaskContext.Instance().Config.MacroConfig.FPressHoldToContinuationEnabled
                        && !_fTimer.Enabled
                    )
                    {
                        _fTimer.Start();
                    }
                }
            }
            else
            {
                // 松开
                if (_firstFKeyDownTime != DateTime.MaxValue)
                {
                    _firstFKeyDownTime = DateTime.MaxValue;
                    _fTimer.Stop();
                }
            }

            // 2. 处理 Space 键 (跳跃键)
            bool isSpaceDown = WinePlatformAddon.IsKeyDown(_releaseControlKey);
            if (isSpaceDown)
            {
                if (_firstSpaceKeyDownTime == DateTime.MaxValue)
                {
                    _firstSpaceKeyDownTime = DateTime.Now;
                }
                else
                {
                    var timeSpan = DateTime.Now - _firstSpaceKeyDownTime;
                    if (
                        timeSpan.TotalMilliseconds > 300
                        && TaskContext
                            .Instance()
                            .Config.MacroConfig.SpacePressHoldToContinuationEnabled
                        && !_spaceTimer.Enabled
                    )
                    {
                        _spaceTimer.Start();
                    }
                }
            }
            else
            {
                if (_firstSpaceKeyDownTime != DateTime.MaxValue)
                {
                    _firstSpaceKeyDownTime = DateTime.MaxValue;
                    _spaceTimer.Stop();
                }
            }
        }

        private void DisposeWineAddon()
        {
            _wineAddon?.Dispose();
        }
    }
}
