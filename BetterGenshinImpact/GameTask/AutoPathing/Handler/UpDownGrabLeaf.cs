using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 须弥四叶印
/// </summary>
public class UpDownGrabLeaf : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await Delay(500, ct);
        Logger.LogInformation("开始上下晃动视角抓{Syy}", "四叶印");

        int x = 0, y = -1000; // y代表垂直方向上的视角移动, x为水平方向
        int i = 40;
        // kbPress('w');  // 飞行
        while (i > 0 && !ct.IsCancellationRequested)
        {
            Simulation.SendInput.Keyboard.KeyDown(TaskContext.Instance().Config.KeyBindingsConfig.InteractionInSomeMode.ToVK());
            if (i % 10 == 0)
            {
                y = -y;
            }

            i--;
            Debug.WriteLine("上下晃动视角中");
            Simulation.SendInput.Mouse.MoveMouseBy(0, y);
            await Delay(40, ct);
        }

        await Delay(500, ct);
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(500, ct);
    }
}
