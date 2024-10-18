using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发普通攻击
/// </summary>
public class NormalAttackHandler : IActionHandler
{
    public Task RunAsync(CancellationToken ct)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "普通攻击");

        Simulation.SendInput.Mouse.LeftButtonClick();
        return Task.CompletedTask;
    }
}
