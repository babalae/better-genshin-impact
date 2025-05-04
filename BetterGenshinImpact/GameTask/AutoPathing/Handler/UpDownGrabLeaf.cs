using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 须弥四叶印
/// </summary>
public class UpDownGrabLeaf : IActionHandler
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
        
        Logger.LogError("没有找到四叶印");
    }
    
    private bool DetectLeaf()
    {
        using var captureRegion = CaptureToRectArea();
        if (captureRegion.SrcMat.Empty())
            return false;
            
        // 第一组检测点 (原始位置)
        var centerColor1 = captureRegion.SrcMat.At<Vec3b>(1000, 1500);
        var tPoint1 = captureRegion.SrcMat.At<Vec3b>(1041, 1508);
        var point987 = captureRegion.SrcMat.At<Vec3b>(987, 1500);
        var point1010 = captureRegion.SrcMat.At<Vec3b>(1010, 1500);
        
        // 第二组检测点 (平移120像素)
        var centerColor2 = captureRegion.SrcMat.At<Vec3b>(1000, 1620);
        var tPoint2 = captureRegion.SrcMat.At<Vec3b>(1041, 1628);
        var point987Shifted = captureRegion.SrcMat.At<Vec3b>(987, 1620);
        var point1010Shifted = captureRegion.SrcMat.At<Vec3b>(1010, 1620);
        
        // 检测是否找到四叶印
        var foundLeaf1 = IsWhite(centerColor1) && IsWhite(tPoint1) && IsWhite(point987) && IsWhite(point1010);
        var foundLeaf2 = IsWhite(centerColor2) && IsWhite(tPoint2) && IsWhite(point987Shifted) && IsWhite(point1010Shifted);
        
        return foundLeaf1 || foundLeaf2;
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

    bool IsWhite(int b, int g, int r)
    {
        return r is >= 245 and <= 255 &&
               g is >= 245 and <= 255 &&
               b is >= 245 and <= 255;
    }

    bool IsWhite(Vec3b centerColor)
    {
        return IsWhite(centerColor[0], centerColor[1], centerColor[2]);
    }
}