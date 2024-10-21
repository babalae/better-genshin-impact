using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 使用纳西妲长按E技能进行收集，360°球形无死角扫码，`type = target`的情况才有效
/// </summary>
public class NahidaCollectHandler : IActionHandler
{
    private DateTime lastETime = DateTime.MinValue;

    public async Task RunAsync(CancellationToken ct)
    {
        Logger.LogInformation("执行 {Nhd} 长按E转圈拾取", "纳西妲");

        // 切人
        Simulation.SendInput.Keyboard.KeyPress(User32Helper.ToVk(TaskContext.Instance().Config.PathingConfig.NahidaAvatarIndex.ToString()));
        await Delay(300, ct);

        var cd = DateTime.Now - lastETime;
        if (cd < TimeSpan.FromSeconds(10))
        {
            await Delay((int)((6 - cd.TotalSeconds + 0.5) * 1000), ct);
        }

        var dpi = TaskContext.Instance().DpiScale;

        int x = (int)(250 * dpi), y = (int)(-30 * dpi);
        int i = 60;
        // 视角拉到最下面
        Simulation.SendInput.Mouse.MoveMouseBy(0, 10000);
        await Delay(200, ct);

        // 按住E技能 无死角扫码
        Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_E);
        await Delay(200, ct);

        // 先地面来一圈
        for (int j = 0; j < 15; j++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(x, 500);
            await Delay(30, ct);
        }

        // 然后抬斜向转圈
        while (!ct.IsCancellationRequested && i > 0)
        {
            i--;
            if (i == 40)
            {
                y -= (int)(20 * dpi);
            }

            Simulation.SendInput.Mouse.MoveMouseBy(x, y);
            await Delay(30, ct);
        }
        Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_E);

        lastETime = DateTime.Now;

        await Delay(1000, ct);

        // 恢复视角
        Simulation.SendInput.Mouse.MiddleButtonClick();

        await Delay(1000, ct);
    }
}
