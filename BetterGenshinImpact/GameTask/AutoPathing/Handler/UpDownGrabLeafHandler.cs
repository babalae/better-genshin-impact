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
    
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("尝试寻找 {syy}", "四叶印");
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
            if (remainingCycles % MovementDirectionChangeInterval == 0)
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
            
        // 第一组检测点 (原始位置)
        Point[] detectPoints = { new(1500, 1000), new( 1508, 1041), new(1500, 987), new(1500, 1010) };
        // 第二组检测点 (右移120像素)
        Point[] detectPointsShifted1 = { new(1620, 1000), new(1628, 1041), new(1620, 987), new(1620, 1010)};
        // 第二组检测点 (左移104像素)
        Point[] detectPointsShifted2 = { new(1396, 1000), new(1404, 1041), new(1396, 987), new(1396, 1010)};
        
        var lower = new Scalar(245, 245, 245);
        var upper = new Scalar(255, 255, 255);
        return CheckPointsInRange(captureRegion.SrcMat, detectPoints, lower, upper) ||
               CheckPointsInRange(captureRegion.SrcMat, detectPointsShifted1, lower, upper) ||
               CheckPointsInRange(captureRegion.SrcMat, detectPointsShifted2, lower, upper);
    }
    
    private async Task InteractWithLeaf(CancellationToken ct)
    {
        Logger.LogInformation("连续检测到 {syy}，开始交互", "四叶印");
        Simulation.SendInput.SimulateAction(GIActions.InteractionInSomeMode);
        await Delay(200, ct);
        Simulation.SendInput.Mouse.MiddleButtonClick();
        
        for (int i = 0; i < 20; i++)
        {
            using var screen = CaptureToRectArea();
            var isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
            if (!isFlying)
            {
                // 能按空格起飞说明到终点了
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