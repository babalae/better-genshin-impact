using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 自动钓鱼
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
/// <param name="config">配置根</param>
public class FishingHandler(GameRuntime runtime, AllConfig config) : IActionHandler
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config = config;

    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        // 钓鱼：路线中的钓鱼点不保存关键帧截图；失败时只记录原因，继续执行后续路线
        var result = await new AutoFishingTask(_runtime, _config)
            .RunAsync(AutoFishingTaskParam.BuildFromConfig(_config.AutoFishingConfig), ct);
        if (result.Kind == SoloTaskResultKind.Failed)
        {
            Logger.LogWarning("自动钓鱼失败：{Message}", result.Message);
        }

        await _runtime.Delay(1000, ct);
    }
}
