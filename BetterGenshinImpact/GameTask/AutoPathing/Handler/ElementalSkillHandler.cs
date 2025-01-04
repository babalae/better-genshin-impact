using BetterGenshinImpact.Core.Simulator;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 触发元素战技
/// </summary>
[Obsolete]
public class ElementalSkillHandler : IActionHandler
{
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行 {Text}", "释放元素战技");
        Simulation.SendInput.Keyboard.KeyPress(TaskContext.Instance().Config.KeyBindingsConfig.ElementalSkill.ToVK()) ;
        await Delay(1000, ct);
    }
}
