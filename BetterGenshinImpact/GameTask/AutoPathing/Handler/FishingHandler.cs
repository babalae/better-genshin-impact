using System;
using System.Diagnostics;
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
/// 自动钓鱼
/// </summary>
public class FishingHandler : IActionHandler
{
    /// <summary>
    /// 运行 <c>RunAsync</c> 对应的任务流程。
    /// </summary>
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        // 钓鱼
        var taskSettingsPageViewModel = App.GetService<TaskSettingsPageViewModel>();
        if (taskSettingsPageViewModel == null)
        {
            throw new ArgumentNullException(nameof(taskSettingsPageViewModel), "内部视图模型对象为空");
        }
        using AutoFishingTask autoFishingTask = new(AutoFishingTaskParam.BuildFromConfig(TaskContext.Instance().Config.AutoFishingConfig));

        await autoFishingTask.Start(ct);

        await Delay(1000, ct);
    }
}
