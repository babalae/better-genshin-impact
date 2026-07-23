using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// GearTask 历史记录器。
/// 通过订阅执行事件把事件写入 Channel，再由单消费者后台循环聚合并异步落盘。
/// </summary>
public sealed class GearTaskHistoryRecorder : BackgroundService, IGearTaskEventConsumer
{
    private static readonly TimeSpan FlushDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<GearTaskHistoryRecorder> _logger;
    private readonly IGearTaskHistoryStore _historyStore;
    private readonly IDisposable _subscription;
    private readonly Channel<GearTaskExecutionEvent> _eventChannel;
    private readonly Dictionary<string, GearTaskExecutionRecord> _records = [];
    private readonly Dictionary<string, DateTime> _dirtyRecords = [];
    private readonly HashSet<string> _priorityFlushRecords = [];
    private bool _disposed;

    public GearTaskHistoryRecorder(
        ILogger<GearTaskHistoryRecorder> logger,
        IGearTaskHistoryStore historyStore,
        IGearTaskEventBus eventBus)
    {
        _logger = logger;
        _historyStore = historyStore;
        _subscription = eventBus.Subscribe(this);
        _eventChannel = Channel.CreateUnbounded<GearTaskExecutionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public ValueTask ConsumeAsync(GearTaskExecutionEvent evt, CancellationToken ct = default)
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        if (!_eventChannel.Writer.TryWrite(evt))
        {
            _logger.LogDebug("GearTask 历史记录事件被忽略，记录器可能正在关闭: {RecordId}", evt.RecordId);
        }

        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var waitForEventTask = _eventChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var waitForFlushDelayTask = Task.Delay(FlushDebounce, stoppingToken);
                var completedTask = await Task.WhenAny(waitForEventTask, waitForFlushDelayTask);

                if (completedTask == waitForEventTask)
                {
                    if (!await waitForEventTask)
                    {
                        break;
                    }

                    while (_eventChannel.Reader.TryRead(out var evt))
                    {
                        ProcessEvent(evt);
                    }
                }

                await FlushDirtyRecordsAsync(force: false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GearTask 历史记录后台消费者异常退出");
        }
        finally
        {
            try
            {
                while (_eventChannel.Reader.TryRead(out var evt))
                {
                    ProcessEvent(evt);
                }

                await FlushDirtyRecordsAsync(force: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GearTask 历史记录器在退出时落盘失败");
            }
        }
    }

    private void ProcessEvent(GearTaskExecutionEvent evt)
    {
        var record = GetOrAddRecord(evt);
        ApplyEvent(record, evt);

        _dirtyRecords[record.RecordId] = DateTime.UtcNow;
        if (IsTerminalEvent(evt))
        {
            // 终态记录在当前批次处理完后优先刷盘，尽量避免任务刚结束但历史还没落地。
            _priorityFlushRecords.Add(record.RecordId);
        }
    }

    private GearTaskExecutionRecord GetOrAddRecord(GearTaskExecutionEvent evt)
    {
        if (_records.TryGetValue(evt.RecordId, out var record))
        {
            return record;
        }

        record = new GearTaskExecutionRecord
        {
            RecordId = evt.RecordId,
            TaskDefinitionName = evt.TaskDefinitionName,
            TaskDefinitionFileKey = evt.TaskDefinitionFileKey,
        };
        _records[evt.RecordId] = record;
        return record;
    }

    private async Task FlushDirtyRecordsAsync(bool force)
    {
        if (_dirtyRecords.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var recordIds = _dirtyRecords.Keys.ToList();
        foreach (var recordId in recordIds)
        {
            if (!_records.TryGetValue(recordId, out var record))
            {
                _dirtyRecords.Remove(recordId);
                _priorityFlushRecords.Remove(recordId);
                continue;
            }

            var shouldFlush = force
                              || _priorityFlushRecords.Contains(recordId)
                              || now - _dirtyRecords[recordId] >= FlushDebounce;

            if (!shouldFlush)
            {
                continue;
            }

            try
            {
                await _historyStore.SaveAsync(record);
                await _historyStore.TrimAsync(record.TaskDefinitionFileKey, 30);

                _dirtyRecords.Remove(recordId);
                _priorityFlushRecords.Remove(recordId);

                if (IsTerminalStatus(record.Status))
                {
                    // 历史记录已经落盘，终态记录不再保留在内存里，避免长期运行时内存持续增长。
                    _records.Remove(recordId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GearTask 历史记录落盘失败，将在后续批次重试。RecordId: {RecordId}", recordId);
            }
        }
    }

    private static bool IsTerminalEvent(GearTaskExecutionEvent evt)
    {
        return evt is ExecutionCompletedEvent or ExecutionFailedEvent or ExecutionCancelledEvent or ExecutionInterruptedEvent;
    }

    private static bool IsTerminalStatus(GearTaskExecutionRecordStatus status)
    {
        return status is GearTaskExecutionRecordStatus.Succeeded
            or GearTaskExecutionRecordStatus.Failed
            or GearTaskExecutionRecordStatus.Cancelled
            or GearTaskExecutionRecordStatus.Interrupted;
    }

    private static void ApplyEvent(GearTaskExecutionRecord record, GearTaskExecutionEvent evt)
    {
        record.TaskDefinitionName = evt.TaskDefinitionName;
        record.TaskDefinitionFileKey = evt.TaskDefinitionFileKey;
        record.CurrentNodeId = evt.NodeId;

        switch (evt)
        {
            case ExecutionStartedEvent started:
                record.SourceRecordId = started.SourceRecordId;
                record.StartTime = started.Timestamp;
                record.Status = GearTaskExecutionRecordStatus.Running;
                record.TotalRunnableNodeCount = started.TotalRunnableNodeCount;
                break;
            case ExecutionCompletedEvent completed:
                record.Status = GearTaskExecutionRecordStatus.Succeeded;
                record.EndTime = completed.Timestamp;
                break;
            case ExecutionFailedEvent failed:
                record.Status = GearTaskExecutionRecordStatus.Failed;
                record.ErrorMessage = failed.ErrorMessage;
                record.EndTime = failed.Timestamp;
                break;
            case ExecutionCancelledEvent cancelled:
                record.Status = GearTaskExecutionRecordStatus.Cancelled;
                record.InterruptReason = cancelled.Reason;
                record.EndTime = cancelled.Timestamp;
                break;
            case ExecutionInterruptedEvent interrupted:
                record.Status = GearTaskExecutionRecordStatus.Interrupted;
                record.InterruptReason = interrupted.Reason;
                record.EndTime = interrupted.Timestamp;
                break;
            case TaskNodeStartedEvent:
            {
                var node = GetOrAddNode(record, evt);
                node.Status = GearTaskExecutionNodeStatus.Running;
                node.StartTime ??= evt.Timestamp;
                break;
            }
            case TaskNodeCompletedEvent completed:
            {
                var node = GetOrAddNode(record, evt);
                node.Status = GearTaskExecutionNodeStatus.Succeeded;
                node.EndTime = evt.Timestamp;
                node.StatusMessage = completed.Message;
                break;
            }
            case TaskNodeFailedEvent failed:
            {
                var node = GetOrAddNode(record, evt);
                node.Status = GearTaskExecutionNodeStatus.Failed;
                node.EndTime = evt.Timestamp;
                node.ErrorMessage = failed.ErrorMessage;
                break;
            }
            case TaskNodeSkippedEvent skipped:
            {
                var node = GetOrAddNode(record, evt);
                node.Status = GearTaskExecutionNodeStatus.Skipped;
                node.EndTime = evt.Timestamp;
                node.StatusMessage = skipped.Reason;
                break;
            }
            case ResumePointUpdatedEvent resume:
            {
                var node = GetOrAddNode(record, evt);
                node.CanResumeInsideTask = resume.CanResumeInsideTask;
                node.ResumeTokenJson = resume.ResumeTokenJson;
                node.StatusMessage = resume.Message ?? node.StatusMessage;
                record.ResumeNodeId = evt.NodeId;
                record.CanResume = resume.CanResumeInsideTask || !string.IsNullOrWhiteSpace(resume.ResumeTokenJson);
                break;
            }
        }

        record.CompletedNodeCount = record.Nodes.Count(n => n.Status == GearTaskExecutionNodeStatus.Succeeded);
        record.FailedNodeCount = record.Nodes.Count(n => n.Status == GearTaskExecutionNodeStatus.Failed);
        record.SkippedNodeCount = record.Nodes.Count(n => n.Status == GearTaskExecutionNodeStatus.Skipped);

        record.Timeline.Add(new GearTaskExecutionTimelineItem
        {
            Timestamp = evt.Timestamp,
            NodeId = evt.NodeId,
            EventType = evt.GetType().Name,
            Message = BuildTimelineMessage(evt),
            Extra = BuildTimelineExtra(evt),
        });
    }

    private static string BuildTimelineMessage(GearTaskExecutionEvent evt)
    {
        return evt switch
        {
            ExecutionStartedEvent => "执行开始",
            ExecutionCompletedEvent => "执行完成",
            ExecutionFailedEvent failed => $"执行失败: {failed.ErrorMessage}",
            ExecutionCancelledEvent cancelled => $"执行取消: {cancelled.Reason}",
            ExecutionInterruptedEvent interrupted => $"执行中断: {interrupted.Reason}",
            TaskNodeStartedEvent => "节点开始执行",
            TaskNodeCompletedEvent completed => completed.Message ?? "节点执行完成",
            TaskNodeFailedEvent failed => $"节点执行失败: {failed.ErrorMessage}",
            TaskNodeSkippedEvent skipped => $"节点跳过: {skipped.Reason}",
            ResumePointUpdatedEvent resume => resume.Message ?? "更新恢复点",
            PathingWaypointEnteredEvent entered => $"进入路径点 {entered.WaypointGroupIndex}/{entered.WaypointIndex} ({entered.PositionX:F2}, {entered.PositionY:F2})",
            PathingWaypointCompletedEvent completed => $"完成路径点 {completed.WaypointGroupIndex}/{completed.WaypointIndex} ({completed.PositionX:F2}, {completed.PositionY:F2})",
            PathingTeleportCompletedEvent teleport => $"完成传送 {teleport.WaypointGroupIndex}/{teleport.WaypointIndex} ({teleport.PositionX:F2}, {teleport.PositionY:F2})",
            PathingActionCompletedEvent action => $"完成动作 {action.Action} ({action.PositionX:F2}, {action.PositionY:F2})",
            ScriptCheckpointReachedEvent script => $"脚本检查点 {script.CheckpointName}",
            _ => evt.GetType().Name,
        };
    }

    private static Dictionary<string, object?> BuildTimelineExtra(GearTaskExecutionEvent evt)
    {
        var extra = new Dictionary<string, object?>(evt.Extra);

        switch (evt)
        {
            case PathingWaypointEnteredEvent entered:
                FillPathingExtra(extra, entered.WaypointGroupIndex, entered.WaypointIndex, entered.PositionX, entered.PositionY, entered.Action);
                break;
            case PathingWaypointCompletedEvent completed:
                FillPathingExtra(extra, completed.WaypointGroupIndex, completed.WaypointIndex, completed.PositionX, completed.PositionY, completed.Action);
                break;
            case PathingTeleportCompletedEvent teleport:
                FillPathingExtra(extra, teleport.WaypointGroupIndex, teleport.WaypointIndex, teleport.PositionX, teleport.PositionY);
                break;
            case PathingActionCompletedEvent action:
                FillPathingExtra(extra, action.WaypointGroupIndex, action.WaypointIndex, action.PositionX, action.PositionY, action.Action);
                break;
        }

        return extra;
    }

    private static void FillPathingExtra(
        Dictionary<string, object?> extra,
        int waypointGroupIndex,
        int waypointIndex,
        double positionX,
        double positionY,
        string? action = null)
    {
        extra["waypointGroupIndex"] = waypointGroupIndex;
        extra["waypointIndex"] = waypointIndex;
        extra["positionX"] = positionX;
        extra["positionY"] = positionY;

        if (!string.IsNullOrWhiteSpace(action))
        {
            extra["action"] = action;
        }
    }

    private static GearTaskExecutionNodeRecord GetOrAddNode(GearTaskExecutionRecord record, GearTaskExecutionEvent evt)
    {
        var node = record.Nodes.FirstOrDefault(n => n.NodeId == evt.NodeId);
        if (node != null)
        {
            return node;
        }

        node = new GearTaskExecutionNodeRecord
        {
            NodeId = evt.NodeId,
            ParentNodeId = evt.ParentNodeId,
            TaskName = evt.TaskName,
            TaskType = evt.TaskType,
            TaskPath = evt.TaskPath,
            Depth = evt.NodeDepth,
            Order = evt.NodeOrder,
            Status = GearTaskExecutionNodeStatus.Pending,
        };
        record.Nodes.Add(node);
        return node;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_disposed)
        {
            _disposed = true;
            _subscription.Dispose();
            _eventChannel.Writer.TryComplete();
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _subscription.Dispose();
            _eventChannel.Writer.TryComplete();
        }

        base.Dispose();
    }
}
