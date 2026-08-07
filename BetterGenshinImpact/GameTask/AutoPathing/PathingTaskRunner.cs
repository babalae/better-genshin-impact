using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.Exceptions;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPathing;

internal enum PathingExternalTaskKind
{
    FetchExpeditionRewards
}

internal sealed record PathingExternalTaskRequest(
    PathingExternalTaskKind Kind,
    string AdventurersGuildCountry,
    PathingTask ResumeTask);

/// <summary>
/// 在完整路线会话结束后调度路线执行过程中发现的外部业务任务。
/// </summary>
internal static class PathingTaskRunner
{
    public static async Task RunAsync(PathExecutor executor, PathingTask task)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(task);

        await executor.ExecutePathingAsync(task);

        var request = executor.TakeExternalTaskRequest();
        if (request == null)
        {
            return;
        }

        await ExecuteExternalTaskAsync(executor, request);

        // 外部任务可能改变角色位置。重新启动原路线，由路线自己的传送锚点恢复上下文。
        await executor.ExecutePathingAsync(request.ResumeTask);
    }

    private static async Task ExecuteExternalTaskAsync(
        PathExecutor executor,
        PathingExternalTaskRequest request)
    {
        if (request.Kind != PathingExternalTaskKind.FetchExpeditionRewards)
        {
            return;
        }

        var runnerContext = RunnerContext.Instance;
        if (!runnerContext.TryBeginAutoFetchDispatch())
        {
            return;
        }

        Common.TaskControl.Logger.LogInformation("当前寻路会话已结束，开始自动领取派遣任务！");
        try
        {
            await new ReturnMainUiTask().Start(executor.ct);
            await runnerContext.StopAutoPickRunTask(
                async () => await new GoToAdventurersGuildTask().Start(
                    request.AdventurersGuildCountry,
                    executor.ct,
                    null,
                    true),
                5);
            await new ReturnMainUiTask().Start(executor.ct);
            Common.TaskControl.Logger.LogInformation("自动领取派遣结束！");
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   and not GameWindowNotFocusedException
                                   and not NormalEndException)
        {
            Common.TaskControl.Logger.LogWarning(ex, "自动派遣领取失败，当前寻路会话已经结束");
            try
            {
                await new ReturnMainUiTask().Start(executor.ct);
            }
            catch (Exception returnEx)
            {
                Common.TaskControl.Logger.LogWarning(returnEx, "自动派遣异常后返回主界面失败");
            }
        }
        finally
        {
            runnerContext.EndAutoFetchDispatch();
        }
    }
}
