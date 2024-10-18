using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 采集任务到达点位后执行拾取操作
/// </summary>
public class PickAroundHandler : IActionHandler
{
    public async Task RunAsync(CancellationTokenSource cts)
    {
        var screen = CaptureToRectArea();
        var angle = 0;
        CameraRotateTask rotateTask = new(cts);
        TaskControl.Logger.LogInformation("执行 {Text}", "小范围内自动拾取");
        await rotateTask.WaitUntilRotatedTo(angle, 5);
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        var startTime = DateTime.UtcNow;
        while (!cts.IsCancellationRequested)
        {
            angle = (5+angle)%360;
            rotateTask.RotateToApproach(angle, screen);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_F);
            await Delay(100, cts);
            if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(5))
                break;
        }
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
    }
}
