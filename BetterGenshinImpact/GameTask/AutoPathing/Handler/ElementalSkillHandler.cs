using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发元素战技
/// </summary>
public class ElementalSkillHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct)
    {
        Logger.LogInformation("执行 {Text}", "释放元素战技");
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
        await Delay(1000, ct);
    }
}
