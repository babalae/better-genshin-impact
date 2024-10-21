using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发普通攻击
/// </summary>
public class NormalAttackHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct)
    {
        TaskControl.Logger.LogInformation("执行 {Text}", "普通攻击");

        // 切人
        Simulation.SendInput.Keyboard.KeyPress(User32Helper.ToVk(TaskContext.Instance().Config.PathingConfig.NahidaAvatarIndex.ToString()));
        await TaskControl.Delay(300, ct);

        Simulation.SendInput.Mouse.LeftButtonClick();
    }
}
