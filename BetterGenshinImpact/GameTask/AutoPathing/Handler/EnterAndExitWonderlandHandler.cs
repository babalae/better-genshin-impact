using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 进出千星奇域
/// </summary>
public class EnterAndExitWonderlandHandler : IActionHandler
{
    /// <summary>
    /// 进出千星奇域任务，绑定本次运行使用的运行环境
    /// </summary>
    private readonly EnterAndExitWonderlandJob _enterAndExitWonderlandJob;

    /// <summary>
    /// 创建进出千星奇域处理器
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public EnterAndExitWonderlandHandler(GameRuntime runtime)
    {
        _enterAndExitWonderlandJob = new EnterAndExitWonderlandJob(runtime);
    }

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await _enterAndExitWonderlandJob.Start(ct);
    }
}
