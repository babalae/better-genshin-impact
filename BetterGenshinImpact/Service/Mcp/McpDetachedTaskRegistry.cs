using System.Collections.Concurrent;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 让 MCP/Agent 在确认 BetterGI 任务已经取得独立任务锁后结束调用，后台任务继续运行。
/// </summary>
public sealed class McpDetachedTaskRegistry(ILogger<McpDetachedTaskRegistry> logger)
{
    private readonly ConcurrentDictionary<string, McpDetachedTaskEntry> _entries = new();

    public async Task<McpDetachedTaskLaunchResult> LaunchAsync(
        string name,
        Func<Task> taskFactory,
        bool waitForCompletion,
        int startupTimeoutSeconds,
        CancellationToken requestCancellationToken)
    {
        if (startupTimeoutSeconds is < 5 or > 600) throw new ArgumentOutOfRangeException(nameof(startupTimeoutSeconds));
        if (TaskControl.TaskSemaphore.CurrentCount == 0)
            throw new InvalidOperationException("当前已有独立任务运行，请先等待或停止当前任务。");

        var id = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.Now;
        var entry = new McpDetachedTaskEntry(id, name, "starting", startedAt, null, null);
        _entries[id] = entry;
        var runningSignal = TaskExecutionSignalHub.Register(id);
        var execution = Task.Run(async () =>
        {
            using var scope = TaskExecutionSignalHub.Enter(id);
            await taskFactory();
        }, CancellationToken.None);
        Observe(id, execution);

        if (waitForCompletion)
        {
            await execution.WaitAsync(requestCancellationToken);
            return new McpDetachedTaskLaunchResult(id, name, true, false, true, startedAt, DateTimeOffset.Now, null);
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(startupTimeoutSeconds), requestCancellationToken);
        var winner = await Task.WhenAny(runningSignal, execution, timeout);
        if (winner == runningSignal)
        {
            var runningAt = await runningSignal;
            if (execution.IsCompleted)
            {
                await execution;
                return new McpDetachedTaskLaunchResult(id, name, true, false, true, startedAt, DateTimeOffset.Now,
                    "任务已成功启动并快速完成。 ");
            }
            _entries[id] = entry with { Status = "running" };
            return new McpDetachedTaskLaunchResult(id, name, true, true, false, startedAt, null,
                $"任务已在 {runningAt:O} 完成初始化并取得 BetterGI 独立任务锁；Agent 调用现在结束，后台任务继续运行。 ");
        }
        if (winner == execution)
        {
            await execution;
            return new McpDetachedTaskLaunchResult(id, name, false, false, true, startedAt, DateTimeOffset.Now,
                "命令在 TaskRunner 发出 Running 信号前已经结束；通常表示前置配置、策略或游戏状态不满足。 ");
        }

        requestCancellationToken.ThrowIfCancellationRequested();

        _entries[id] = entry with { Status = "startup_pending" };
        return new McpDetachedTaskLaunchResult(id, name, true, false, false, startedAt, null,
            $"等待 {startupTimeoutSeconds} 秒仍未观察到独立任务锁；后台启动流程仍在继续，可用 bgi_get_detached_task_status 或 bgi_get_execution_status 检查。 ");
    }

    public IReadOnlyList<McpDetachedTaskEntry> GetEntries(int limit = 20) => _entries.Values
        .OrderByDescending(x => x.StartedAt)
        .Take(Math.Clamp(limit, 1, 100))
        .ToArray();

    private void Observe(string id, Task execution)
    {
        _ = execution.ContinueWith(task =>
        {
            var completedAt = DateTimeOffset.Now;
            if (task.IsFaulted)
            {
                var error = task.Exception?.GetBaseException().Message ?? "未知错误";
                _entries.AddOrUpdate(id,
                    _ => new McpDetachedTaskEntry(id, "unknown", "failed", completedAt, completedAt, error),
                    (_, old) => old with { Status = "failed", CompletedAt = completedAt, Error = error });
                logger.LogError(task.Exception, "后台 MCP 任务 {TaskId} 执行失败", id);
            }
            else if (task.IsCanceled)
            {
                _entries.AddOrUpdate(id,
                    _ => new McpDetachedTaskEntry(id, "unknown", "cancelled", completedAt, completedAt, null),
                    (_, old) => old with { Status = "cancelled", CompletedAt = completedAt });
            }
            else
            {
                _entries.AddOrUpdate(id,
                    _ => new McpDetachedTaskEntry(id, "unknown", "completed", completedAt, completedAt, null),
                    (_, old) => old with { Status = "completed", CompletedAt = completedAt });
            }
            Prune();
            TaskExecutionSignalHub.Unregister(id);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Prune()
    {
        foreach (var stale in _entries.Values.OrderByDescending(x => x.StartedAt).Skip(100))
            _entries.TryRemove(stale.Id, out _);
    }
}

public sealed record McpDetachedTaskEntry(
    string Id,
    string Name,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public sealed record McpDetachedTaskLaunchResult(
    string Id,
    string Name,
    bool Accepted,
    bool Running,
    bool Completed,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Message);
