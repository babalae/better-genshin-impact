using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;

using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理退出大世界并重新登录动作的执行逻辑 / Handles the setup logic for exiting the world and relogging interactively.
/// 用于脱战或重置世界状态 / Used to disengage from combat or reset world state.
/// </summary>
public class ExitAndReloginHandler : IActionHandler
{
    private readonly ExitAndReloginJob _exitAndReloginJob = new();

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【退出重登】");
        await _exitAndReloginJob.Start(ct);
    }
}
