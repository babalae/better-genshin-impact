using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发普通攻击
/// </summary>
public class NormalAttackHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct)
    {
        Logger.LogInformation("执行 {Text}", "普通攻击");
        Simulation.SendInput.Mouse.LeftButtonClick();
        await Delay(1000, ct);
    }
}
