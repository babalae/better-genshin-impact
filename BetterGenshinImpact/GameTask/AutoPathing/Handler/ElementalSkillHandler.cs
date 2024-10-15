using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发元素战技
/// </summary>
public class ElementalSkillHandler : IActionHandler
{
    public Task RunAsync(CancellationTokenSource cts)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "释放元素战技");
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
        return Task.CompletedTask;
    }
}
