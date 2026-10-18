using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 调整游戏时间
/// </summary>
public class SetTimeHandler : IActionHandler
{
    /// <summary>
    /// 调整时间任务，绑定本次运行使用的运行环境
    /// </summary>
    private readonly SetTimeTask _setTimeTask;

    /// <summary>
    /// 创建调整时间处理器
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public SetTimeHandler(GameRuntime runtime)
    {
        _setTimeTask = new SetTimeTask(runtime);
    }

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack == null || string.IsNullOrEmpty(waypointForTrack.ActionParams)) return;
        
        string[] timeParts = waypointForTrack.ActionParams.Split(':');
        int hour = int.Parse(timeParts[0]);
        int minute = int.Parse(timeParts[1]);
        
        bool skipAnimation = timeParts.Length < 3 || bool.Parse(timeParts[2]);
        await _setTimeTask.DoOnce(hour, minute, ct, skipAnimation);
    }
}
