using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发元素战技
/// </summary>
public class ElementalSkillHandler : IActionHandler
{
    public Task RunAsync(CancellationTokenSource cts)
    {
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_E);
        return Task.CompletedTask;
    }
}
