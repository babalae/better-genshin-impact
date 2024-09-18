using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 使用纳西妲长按E技能进行收集，360°球形无死角扫码，`type = target`的情况才有效
/// </summary>
public class NahidaCollectHandler : IActionHandler
{
    private DateTime lastETime = DateTime.MinValue;

    public async Task RunAsync(CancellationTokenSource cts)
    {
        var cd = DateTime.Now - lastETime;
        if (cd < TimeSpan.FromSeconds(10))
        {
            await Delay((int)((6 - cd.TotalSeconds + 0.5) * 1000), cts);
        }

        var dpi = TaskContext.Instance().DpiScale;

        int x = (int)(200 * dpi), y = (int)(-30 * dpi);
        int i = (int)(140 * dpi);
        // 视角拉到最下面
        Simulation.SendInput.Mouse.MoveMouseBy(0, 10000);

        // 按住E技能 无死角扫码
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_E);
        while (!cts.IsCancellationRequested && i > 0)
        {
            i--;
            Simulation.SendInput.Mouse.MoveMouseBy(x, y);
            await Delay(40, cts);
        }
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_E);

        lastETime = DateTime.Now;

        await Delay(300, cts);

        // 恢复视角
        Simulation.SendInput.Mouse.MiddleButtonClick();
    }
}
