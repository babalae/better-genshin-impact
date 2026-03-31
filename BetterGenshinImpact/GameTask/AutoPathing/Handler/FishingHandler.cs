using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 处理自动钓鱼动作的抛出及执行逻辑 / Handles the execution logic for the auto-fishing action.
/// 协调注入钓鱼配置和执行流程 / Coordinates fishing configurations and execution loops.
/// </summary>
public class FishingHandler : IActionHandler
{
    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        Logger.LogInformation("执行动作: 【自动钓鱼】 / Executing action: [Auto Fishing]");
        
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new InvalidOperationException("内部视图模型对象未初始化为空 / Core view model is null.");
        }

        var fishingParam = AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig);
        AutoFishingTask autoFishingTask = new(fishingParam);

        await autoFishingTask.Start(ct);
        await Delay(1000, ct);
    }
}
