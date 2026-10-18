using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 退出并重新登录
/// </summary>
public class ExitAndReloginHandler : IActionHandler
{
    /// <summary>
    /// 退出重登任务，绑定本次运行使用的运行环境
    /// </summary>
    private readonly ExitAndReloginJob _exitAndReloginJob;

    /// <summary>
    /// 创建退出重登处理器
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public ExitAndReloginHandler(GameRuntime runtime)
    {
        _exitAndReloginJob = new ExitAndReloginJob(runtime);
    }

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        await _exitAndReloginJob.Start(ct);
    }
}
