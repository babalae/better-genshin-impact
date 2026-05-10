using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// GearTask 任务树执行器。
/// 只负责遍历节点、执行任务以及向外发布业务事件。
/// </summary>
public interface IGearTaskExecutionRunner
{
    Task RunAsync(BaseGearTask rootTask, GearTaskExecutionRunContext context, CancellationToken ct);
}

/// <summary>
/// 默认 GearTask 执行器实现。
/// </summary>
public sealed class GearTaskExecutionRunner : IGearTaskExecutionRunner
{
    private readonly ILogger<GearTaskExecutionRunner> _logger;
    private readonly IGearTaskEventBus _eventBus;

    public GearTaskExecutionRunner(ILogger<GearTaskExecutionRunner> logger, IGearTaskEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task RunAsync(BaseGearTask rootTask, GearTaskExecutionRunContext context, CancellationToken ct)
    {
        var rootContext = CreateNodeContext(rootTask, context, "0", null, 0, 0);
        var startedEvent = rootContext.CreateEvent<ExecutionStartedEvent>();
        startedEvent.SourceRecordId = context.ResumePlan?.SourceRecordId;
        startedEvent.TotalRunnableNodeCount = CountTotalNodes(rootTask);
        await _eventBus.PublishAsync(startedEvent, ct);

        try
        {
            await ExecuteNodeAsync(rootTask, context, "0", null, 0, 0, ct);

            var completedEvent = rootContext.CreateEvent<ExecutionCompletedEvent>();
            await _eventBus.PublishAsync(completedEvent, ct);
        }
        catch (OperationCanceledException)
        {
            var cancelledEvent = rootContext.CreateEvent<ExecutionCancelledEvent>();
            cancelledEvent.Reason = "用户取消执行";
            await _eventBus.PublishAsync(cancelledEvent, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            var failedEvent = rootContext.CreateEvent<ExecutionFailedEvent>();
            failedEvent.ErrorMessage = ex.Message;
            await _eventBus.PublishAsync(failedEvent, CancellationToken.None);
            throw;
        }
    }

    private async Task ExecuteNodeAsync(
        BaseGearTask task,
        GearTaskExecutionRunContext runContext,
        string nodeId,
        string? parentNodeId,
        int depth,
        int order,
        CancellationToken ct)
    {
        var nodeContext = CreateNodeContext(task, runContext, nodeId, parentNodeId, depth, order);

        task.SetExecutionContext(nodeContext);

        if (!task.Enabled)
        {
            var skippedEvent = nodeContext.CreateEvent<TaskNodeSkippedEvent>();
            skippedEvent.Reason = "任务未启用";
            await nodeContext.PublishAsync(skippedEvent, ct);
            return;
        }

        if (ShouldSkipByResume(runContext.ResumePlan, nodeId))
        {
            var skippedEvent = nodeContext.CreateEvent<TaskNodeSkippedEvent>();
            skippedEvent.Reason = "恢复执行时跳过已完成节点";
            await nodeContext.PublishAsync(skippedEvent, ct);
            return;
        }

        var startedEvent = nodeContext.CreateEvent<TaskNodeStartedEvent>();
        await nodeContext.PublishAsync(startedEvent, ct);

        try
        {
            if (task is IGearTaskResumable resumable)
            {
                await resumable.ApplyResumeTokenAsync(nodeContext.ResumeTokenJson, ct);
            }

            await task.Execute(ct);

            var childOrder = 0;
            foreach (var child in task.Children)
            {
                await ExecuteNodeAsync(child, runContext, $"{nodeId}/{childOrder}", nodeId, depth + 1, childOrder, ct);
                childOrder++;
            }

            var completedEvent = nodeContext.CreateEvent<TaskNodeCompletedEvent>();
            completedEvent.Message = "节点执行完成";
            await nodeContext.PublishAsync(completedEvent, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行节点失败: {TaskName} ({NodeId})", task.Name, nodeId);
            var failedEvent = nodeContext.CreateEvent<TaskNodeFailedEvent>();
            failedEvent.ErrorMessage = ex.Message;
            await nodeContext.PublishAsync(failedEvent, CancellationToken.None);
            throw;
        }
    }

    private static string? GetResumeToken(GearTaskResumePlan? plan, string nodeId)
    {
        if (plan == null)
        {
            return null;
        }

        return plan.NodeResumeTokens.TryGetValue(nodeId, out var token) ? token : null;
    }

    private static bool ShouldSkipByResume(GearTaskResumePlan? plan, string nodeId)
    {
        if (plan == null || string.IsNullOrWhiteSpace(plan.ResumeNodeId))
        {
            return false;
        }

        if (nodeId == plan.ResumeNodeId)
        {
            return false;
        }

        if (plan.ResumeNodeId.StartsWith(nodeId + "/", StringComparison.Ordinal))
        {
            // 恢复节点的祖先链仍然需要执行，否则无法走到真正的恢复节点。
            return false;
        }

        return IsNodeBefore(nodeId, plan.ResumeNodeId);
    }

    /// <summary>
    /// 比较两个节点路径的顺序，供恢复逻辑判断当前节点是否位于恢复点之前。
    /// </summary>
    private static bool IsNodeBefore(string leftNodeId, string rightNodeId)
    {
        var left = leftNodeId.Split('/').Select(int.Parse).ToArray();
        var right = rightNodeId.Split('/').Select(int.Parse).ToArray();
        var minLength = Math.Min(left.Length, right.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (left[i] != right[i])
            {
                return left[i] < right[i];
            }
        }

        return left.Length < right.Length;
    }

    /// <summary>
    /// 统计任务树节点总数，用于生成执行进度的总量基线。
    /// </summary>
    private static int CountTotalNodes(BaseGearTask task)
    {
        return 1 + task.Children.Sum(CountTotalNodes);
    }

    private GearTaskNodeExecutionContext CreateNodeContext(
        BaseGearTask task,
        GearTaskExecutionRunContext runContext,
        string nodeId,
        string? parentNodeId,
        int depth,
        int order)
    {
        return GearTaskNodeExecutionContext.Create(
            _eventBus,
            task,
            runContext,
            nodeId,
            parentNodeId,
            depth,
            order,
            GetResumeToken(runContext.ResumePlan, nodeId));
    }
}
