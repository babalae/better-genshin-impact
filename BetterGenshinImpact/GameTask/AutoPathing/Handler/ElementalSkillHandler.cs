using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发元素战技
/// </summary>
public class ElementalSkillHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "释放元素战技");

        // 切人
        Simulation.SendInput.Keyboard.KeyPress(User32Helper.ToVk(TaskContext.Instance().Config.PathingConfig.NahidaAvatarIndex.ToString()));
        await TaskControl.Delay(300, ct);

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
    }
}
