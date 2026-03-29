using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class CameraRotateTask(CancellationToken ct)
{
    private readonly double _dpi = TaskContext.Instance().DpiScale;
    
    // 亚像素累加器：用以存储计算过程中的小数部分累加（消除极小角度下的精度丢失）
    private double _fractionalMoveX = 0;

    /// <summary>
    /// 向目标角度旋转，采用带缓动与亚像素精度的平滑处理
    /// </summary>
    public float RotateToApproach(float targetOrientation, ImageRegion imageRegion)
    {
        var cao = CameraOrientation.Compute(imageRegion.SrcMat);
        
        if (float.IsNaN(cao))
        {
            Logger.LogWarning("无法识别当前相机朝向，跳过此帧");
            return 360f; 
        }

        var diff = (cao - targetOrientation + 180) % 360 - 180;
        diff += diff < -180 ? 360 : 0;
        
        if (Math.Abs(diff) < 0.5f)
        {
            _fractionalMoveX = 0; // 达到极小容差时，清空残差池并停止计算
            return diff;
        }

        // 重新回归连续P控制，抹平阶跃的同时保持与原版(1、2、3、4倍)一致的基础物理量级
        // 原版通过 if-else 做到的最大4倍，这里用 1.0 + (\diff\ / 60) 平滑实现（最大约4倍）
        double controlRatio = 1.0 + (Math.Abs(diff) / 60.0);

        // 【致命修正】：原版本的推力是自带“-”号的，用于反向闭环修正。
        // 上个版本重构时漏掉了这个最为关键的“-”，导致推力同向，程序一直在“背道而驰”，
        // 从而引起疯狂转圈、高速越过目标点导致超出误差而失败！
        double actualSpeed = -controlRatio * diff * _dpi;

        // 亚像素累加机制（保留精度）
        _fractionalMoveX += actualSpeed;
        int moveX = (int)Math.Truncate(_fractionalMoveX);
        _fractionalMoveX -= moveX;

        // 死区逃逸
        if (moveX == 0 && Math.Abs(diff) > 1.0f)
        {
            moveX = -Math.Sign(diff);
        }

        // 物理防撕裂：根据实际情况拉伸裁剪至400
        moveX = Math.Clamp(moveX, -400, 400);

        Simulation.SendInput.Mouse.MoveMouseBy(moveX, 0);
        return diff;
    }

    /// <summary>
    /// 转动视角到目标角度，具备死锁自愈和动态帧回调能力
    /// </summary>
    public async Task<bool> WaitUntilRotatedTo(int targetOrientation, int maxDiff, int maxTryTimes = 50)
    {
        bool isSuccessful = false;
        int count = 0;
        int stuckFrames = 0;
        float lastDiff = 360f;

        while (!ct.IsCancellationRequested)
        {
            var screen = CaptureToRectArea();
            float diff = RotateToApproach(targetOrientation, screen);
            
            if (Math.Abs(diff) <= maxDiff && Math.Abs(diff) != 360f)
            {
                isSuccessful = true;
                break;
            }

            // 放宽停滞侦测到 0.05 容差，防止正常防卡抖动被误判为停滞
            if (Math.Abs(diff - lastDiff) < 0.05f && Math.Abs(diff) != 360f)
            {
                stuckFrames++;
                if (stuckFrames > 8)
                {
                    Logger.LogWarning("视角处于停滞状态（连续 8 帧角度几无变化），强制脱离防止无限干等！");
                    break;
                }
            }
            else
            {
                stuckFrames = 0; // 一旦有实质性转动，重置计数
            }

            lastDiff = diff;

            if (count > maxTryTimes)
            {
                Logger.LogWarning("视角转动到目标角度因尝试次数耗尽而停止！");
                break;
            }

            // 彻底回归稳定的 50ms。极速 30ms 虽然刷新快，但 Windows 底层消息队列 + 游戏内引擎延迟
            // 极可能导致输入指令堆叠 (Input Buffer 迟滞溢出)，在达到目标后释放，从而引起无法挽回的超调震荡。
            await Delay(50, ct);
            count++;
        }
        
        // 任务结束后永远保证环境净空
        _fractionalMoveX = 0;
        return isSuccessful;
    }
}
