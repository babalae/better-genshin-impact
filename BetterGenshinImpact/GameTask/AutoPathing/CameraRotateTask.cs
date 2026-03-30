using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Handles camera rotation mechanics with dynamic P-control and sub-pixel precision accumulation.
/// 处理相机旋转机制的类，采用动态P控制和亚像素精度累加。
/// </summary>
public class CameraRotateTask
{
    private readonly CancellationToken _ct;
    private readonly double _dpi;
    
    // Sub-pixel accumulator to store fractional remainders 亚像素累加器
    private double _fractionalMoveX = 0.0;

    /// <summary>
    /// Initializes a new instance of the CameraRotateTask.
    /// 初始化 CameraRotateTask 的新实例。
    /// </summary>
    /// <param name="ct">The cancellation token. 取消令牌。</param>
    public CameraRotateTask(CancellationToken ct)
    {
        _ct = ct;
        _dpi = TaskContext.Instance()?.DpiScale ?? 1.0;
    }

    /// <summary>
    /// Rotates the camera towards the target orientation using smooth damping and sub-pixel accuracy.
    /// 向目标角度旋转，采用带缓动与亚像素精度的平滑处理。
    /// </summary>
    /// <param name="targetOrientation">The desired target azimuth angle in degrees. 期望的目标方位角（度）。</param>
    /// <param name="imageRegion">The visual surface capture region. 视觉表面捕获区域。</param>
    /// <returns>The calculated minimal angular difference. 计算得到的最小角度差。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="imageRegion"/> is null. 当 <paramref name="imageRegion"/> 为 null 时抛出。</exception>
    public float RotateToApproach(float targetOrientation, ImageRegion imageRegion)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        
        if (imageRegion.SrcMat == null || imageRegion.SrcMat.IsDisposed)
        {
            return 360f;
        }

        var cao = CameraOrientation.Compute(imageRegion.SrcMat);
        
        if (float.IsNaN(cao))
        {
            Logger?.LogWarning("无法识别当前相机朝向，跳过此帧");
            return 360f; 
        }

        var diff = (cao - targetOrientation + 180f) % 360f - 180f;
        if (diff < -180f)
        {
            diff += 360f;
        }
        
        if (Math.Abs(diff) < 0.5f)
        {
            _fractionalMoveX = 0.0; 
            return diff;
        }

        double controlRatio = 1.0 + (Math.Abs(diff) / 60.0);
        double actualSpeed = -controlRatio * diff * _dpi;

        _fractionalMoveX += actualSpeed;
        int moveX = (int)Math.Truncate(_fractionalMoveX);
        _fractionalMoveX -= moveX;

        // Escape deadzone 死区逃逸
        if (moveX == 0 && Math.Abs(diff) > 1.0f)
        {
            moveX = -Math.Sign(diff);
        }

        moveX = Math.Clamp(moveX, -400, 400);

        Simulation.SendInput?.Mouse?.MoveMouseBy(moveX, 0);
        return diff;
    }

    /// <summary>
    /// Asynchronously drives the viewpoint to the target angle, featuring deadlock recovery and frame polling.
    /// 异步转动视角到目标角度，具备死锁自愈和动态帧回调能力。
    /// </summary>
    /// <param name="targetOrientation">The desired target angle. 期望的目标角度。</param>
    /// <param name="maxDiff">Maximum acceptable angular tolerance. 最大可接受的角度容差。</param>
    /// <param name="maxTryTimes">Maximum frame iterations before giving up. 放弃前的最大帧迭代尝试次数。</param>
    /// <returns>True if successfully reached within tolerance, false otherwise. 若在此容差内成功到达目标返回 true，否则返回 false。</returns>
    public async Task<bool> WaitUntilRotatedTo(int targetOrientation, int maxDiff, int maxTryTimes = 50)
    {
        bool isSuccessful = false;
        int count = 0;
        int stuckFrames = 0;
        float lastDiff = 360f;

        while (!_ct.IsCancellationRequested)
        {
            var screen = CaptureToRectArea();
            if (screen == null) break;

            float diff = RotateToApproach(targetOrientation, screen);
            
            if (Math.Abs(diff) <= maxDiff && Math.Abs(diff) < 360f)
            {
                isSuccessful = true;
                break;
            }

            if (Math.Abs(diff - lastDiff) < 0.05f && Math.Abs(diff) < 360f)
            {
                stuckFrames++;
                if (stuckFrames > 8)
                {
                    Logger?.LogWarning("视角处于停滞状态（连续 8 帧角度几无变化），强制脱离防止无限干等！");
                    break;
                }
            }
            else
            {
                stuckFrames = 0; 
            }

            lastDiff = diff;

            if (count > maxTryTimes)
            {
                Logger?.LogWarning("视角转动到目标角度因尝试次数耗尽而停止！");
                break;
            }

            await Delay(50, _ct).ConfigureAwait(false);
            count++;
        }
        
        _fractionalMoveX = 0.0;
        return isSuccessful;
    }
}
