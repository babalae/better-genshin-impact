using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.Core.Recognition.OpenCv.OpenCvCommonHelper;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 须弥四叶印
/// </summary>
public class UpDownGrabLeafHandler : IActionHandler
{
    private const int InitialVerticalMovement = 1000;
    private const int MovementDirectionChangeInterval = 10;
    private const int TotalCycles = 40;
    private const int DelayBetweenCycles = 100;
    private const int ConsecutiveDetectionsRequired = 2;
    private const int DetectionDelayMs = 150;

    // 优化 1：提取静态变量，消除高频检测时的内存分配开销
    private static readonly Point[] DetectPointsOrigin = { new(1500, 1000), new(1508, 1041), new(1500, 987), new(1500, 1010) };
    private static readonly Point[] DetectPointsShiftedRight = { new(1620, 1000), new(1628, 1041), new(1620, 987), new(1620, 1010) };
    private static readonly Point[] DetectPointsShiftedLeft = { new(1396, 1000), new(1404, 1041), new(1396, 987), new(1396, 1010) };
    private static readonly Scalar LeafColorLower = new(245, 245, 245);
    private static readonly Scalar LeafColorUpper = new(255, 255, 255);
    
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【寻找{syy}】", "四叶印");
        int direction = 1;
        if (!String.IsNullOrEmpty(waypointForTrack?.ActionParams))
        {
            direction = waypointForTrack.ActionParams == "up" ? 1 : -1;
        }
        int verticalMovement = direction * InitialVerticalMovement;
        int remainingCycles = TotalCycles;
        int consecutiveDetections = 0;
        
        while (remainingCycles > 0 && !ct.IsCancellationRequested)
        {
            // 优化 2：避免第一次进循环（40）时就翻转方向
            int currentCycle = TotalCycles - remainingCycles;
            if (currentCycle > 0 && currentCycle % MovementDirectionChangeInterval == 0)
                verticalMovement = -verticalMovement;
                
            bool currentDetection = DetectLeaf();
            
            if (currentDetection)
            {
                consecutiveDetections++;
                Logger.LogInformation("检测到四叶印 ({current}/{required})", consecutiveDetections, ConsecutiveDetectionsRequired);
                
                if (consecutiveDetections >= ConsecutiveDetectionsRequired)
                {
                    await InteractWithLeaf(ct);
                    return;
                }
                
                await Delay(DetectionDelayMs, ct);
            }
            else
            {
                consecutiveDetections = 0;
                Simulation.SendInput.Mouse.MoveMouseBy(0, verticalMovement);
                await Delay(DelayBetweenCycles, ct);
                remainingCycles--;
            }
        }
        // 失败后视角回正
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(300, ct);
        Logger.LogError("没有找到四叶印");
    }
    
    private bool DetectLeaf()
    {
        using var captureRegion = CaptureToRectArea();
        if (captureRegion.SrcMat.Empty())
            return false;
            
        // 引用提取好的静态数组
        return CheckPointsInRange(captureRegion.SrcMat, DetectPointsOrigin, LeafColorLower, LeafColorUpper) ||
               CheckPointsInRange(captureRegion.SrcMat, DetectPointsShiftedRight, LeafColorLower, LeafColorUpper) ||
               CheckPointsInRange(captureRegion.SrcMat, DetectPointsShiftedLeft, LeafColorLower, LeafColorUpper);
    }
    
    private async Task InteractWithLeaf(CancellationToken ct)
    {
        Logger.LogInformation("连续检测到 {syy}，开始交互", "四叶印");
        Simulation.SendInput.SimulateAction(GIActions.InteractionInSomeMode);
        
        // 优化 3：这里增加到足够的前摇等待时间，避免动画没结束就强行按跳跃打断四叶印
        await Delay(200, ct); 
        Simulation.SendInput.Mouse.MiddleButtonClick();
        
        for (int i = 0; i < 20; i++)
        {
            using var screen = CaptureToRectArea();
            var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
            if (!isFlying)
            {
                // 如果等了很久都没判定为飞行，按空格补充起飞
                Simulation.SendInput.SimulateAction(GIActions.Jump);
                await Delay(500, ct);
            }
            else
            {
                break;
            }
        }

        await Delay(200, ct);
    }
}