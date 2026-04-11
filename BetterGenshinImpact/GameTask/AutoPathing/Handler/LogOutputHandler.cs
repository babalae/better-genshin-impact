using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 输出日志的动作处理器
/// </summary>
public class LogOutputHandler : IActionHandler
{
    public Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        if (waypointForTrack != null && !string.IsNullOrEmpty(waypointForTrack.LogInfo))
        {
            Logger.LogInformation(waypointForTrack.LogInfo);
        }
        return Task.CompletedTask;
    }
}
