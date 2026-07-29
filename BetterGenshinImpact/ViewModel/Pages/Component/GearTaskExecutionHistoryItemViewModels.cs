using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.GearTask.Execution;

namespace BetterGenshinImpact.ViewModel.Pages.Component;

/// <summary>
/// GearTask 执行记录的展示辅助方法。
/// </summary>
internal static class GearTaskExecutionDisplayHelper
{
    public static string GetRecordStatusText(GearTaskExecutionRecordStatus status)
    {
        return status switch
        {
            GearTaskExecutionRecordStatus.Pending => "待执行",
            GearTaskExecutionRecordStatus.Running => "执行中",
            GearTaskExecutionRecordStatus.Succeeded => "成功完成",
            GearTaskExecutionRecordStatus.Failed => "执行失败",
            GearTaskExecutionRecordStatus.Cancelled => "已取消",
            GearTaskExecutionRecordStatus.Interrupted => "中断",
            _ => status.ToString(),
        };
    }

    public static string GetRecordStatusTone(GearTaskExecutionRecordStatus status)
    {
        return status switch
        {
            GearTaskExecutionRecordStatus.Succeeded => "Success",
            GearTaskExecutionRecordStatus.Failed => "Danger",
            GearTaskExecutionRecordStatus.Interrupted => "Warning",
            GearTaskExecutionRecordStatus.Cancelled => "Warning",
            GearTaskExecutionRecordStatus.Running => "Info",
            _ => "Neutral",
        };
    }

    public static string GetNodeStatusText(GearTaskExecutionNodeStatus status)
    {
        return status switch
        {
            GearTaskExecutionNodeStatus.Pending => "未执行到",
            GearTaskExecutionNodeStatus.Running => "执行中",
            GearTaskExecutionNodeStatus.Succeeded => "已完成",
            GearTaskExecutionNodeStatus.Failed => "失败",
            GearTaskExecutionNodeStatus.Skipped => "已跳过",
            GearTaskExecutionNodeStatus.Cancelled => "已取消",
            GearTaskExecutionNodeStatus.Interrupted => "中断",
            _ => status.ToString(),
        };
    }

    public static string GetNodeStatusTone(GearTaskExecutionNodeStatus status)
    {
        return status switch
        {
            GearTaskExecutionNodeStatus.Succeeded => "Success",
            GearTaskExecutionNodeStatus.Failed => "Danger",
            GearTaskExecutionNodeStatus.Interrupted => "Warning",
            GearTaskExecutionNodeStatus.Cancelled => "Warning",
            GearTaskExecutionNodeStatus.Running => "Info",
            GearTaskExecutionNodeStatus.Skipped => "Neutral",
            _ => "Neutral",
        };
    }

    public static string FormatDuration(DateTime? startTime, DateTime? endTime)
    {
        if (startTime == null)
        {
            return "-";
        }

        var actualEnd = endTime ?? DateTime.Now;
        return FormatDuration(actualEnd - startTime.Value);
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} 分 {duration.Seconds} 秒";
        }

        return $"{Math.Max(duration.Seconds, 0)} 秒";
    }

    public static string BuildRecordProgressText(GearTaskExecutionRecord record)
    {
        return $"{record.CompletedNodeCount}/{Math.Max(record.TotalRunnableNodeCount, 0)}";
    }

    public static string BuildResumeNodeText(GearTaskExecutionRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ResumeNodeId))
        {
            return "-";
        }

        var node = record.Nodes.FirstOrDefault(n => n.NodeId == record.ResumeNodeId);
        return node == null ? record.ResumeNodeId! : node.TaskName;
    }

    public static string BuildRecordSummaryText(GearTaskExecutionRecord record)
    {
        var status = GetRecordStatusText(record.Status);
        var progress = BuildRecordProgressText(record);
        return $"{status} · {progress}";
    }

    public static int CompareNodeId(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return -1;
        }

        if (right == null)
        {
            return 1;
        }

        var leftParts = ParseNodeId(left);
        var rightParts = ParseNodeId(right);
        var minLength = Math.Min(leftParts.Count, rightParts.Count);
        for (var i = 0; i < minLength; i++)
        {
            var compare = leftParts[i].CompareTo(rightParts[i]);
            if (compare != 0)
            {
                return compare;
            }
        }

        return leftParts.Count.CompareTo(rightParts.Count);
    }

    private static IReadOnlyList<int> ParseNodeId(string nodeId)
    {
        return nodeId.Split('/')
            .Select(part => int.TryParse(part, out var value) ? value : int.MaxValue)
            .ToArray();
    }
}

/// <summary>
/// 单条任务组执行记录的界面展示模型。
/// </summary>
public sealed class GearTaskExecutionRecordItemViewModel
{
    public GearTaskExecutionRecordItemViewModel(GearTaskExecutionRecord record)
    {
        Record = record;
    }

    public GearTaskExecutionRecord Record { get; }

    public string RecordId => Record.RecordId;

    public string StatusText => GearTaskExecutionDisplayHelper.GetRecordStatusText(Record.Status);

    public string StatusTone => GearTaskExecutionDisplayHelper.GetRecordStatusTone(Record.Status);

    public string StartTimeText => Record.StartTime.ToString("MM-dd HH:mm");

    public string EndTimeText => Record.EndTime?.ToString("MM-dd HH:mm") ?? "-";

    public string DurationText => GearTaskExecutionDisplayHelper.FormatDuration(Record.StartTime, Record.EndTime);

    public string ProgressText => GearTaskExecutionDisplayHelper.BuildRecordProgressText(Record);

    public string ResumeNodeText => GearTaskExecutionDisplayHelper.BuildResumeNodeText(Record);

    public string SummaryText => GearTaskExecutionDisplayHelper.BuildRecordSummaryText(Record);

    public string InterruptReasonText => string.IsNullOrWhiteSpace(Record.InterruptReason) ? "-" : Record.InterruptReason!;

    public string ErrorMessageText => string.IsNullOrWhiteSpace(Record.ErrorMessage) ? "-" : Record.ErrorMessage!;
}

/// <summary>
/// 单条任务节点执行状态的界面展示模型。
/// </summary>
public sealed class GearTaskExecutionNodeItemViewModel
{
    public GearTaskExecutionNodeItemViewModel(GearTaskExecutionNodeRecord record)
    {
        Record = record;
    }

    public GearTaskExecutionNodeRecord Record { get; }

    public string NodeId => Record.NodeId;

    public string TaskName => $"{new string(' ', Math.Max(Record.Depth - 1, 0) * 2)}{Record.TaskName}";

    public string TaskType => string.IsNullOrWhiteSpace(Record.TaskType) ? "任务组" : Record.TaskType;

    public string TaskPath => string.IsNullOrWhiteSpace(Record.TaskPath) ? "-" : Record.TaskPath;

    public string StatusText => GearTaskExecutionDisplayHelper.GetNodeStatusText(Record.Status);

    public string StatusTone => GearTaskExecutionDisplayHelper.GetNodeStatusTone(Record.Status);

    public string TimeRangeText
    {
        get
        {
            if (Record.StartTime == null)
            {
                return "-";
            }

            var start = Record.StartTime.Value.ToString("HH:mm:ss");
            var end = Record.EndTime?.ToString("HH:mm:ss") ?? "--:--:--";
            return $"{start} - {end}";
        }
    }

    public string DurationText => GearTaskExecutionDisplayHelper.FormatDuration(Record.StartTime, Record.EndTime);

    public string MessageText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Record.ErrorMessage))
            {
                return Record.ErrorMessage!;
            }

            if (!string.IsNullOrWhiteSpace(Record.StatusMessage))
            {
                return Record.StatusMessage!;
            }

            return "-";
        }
    }

    public string ResumeCapabilityText => Record.CanResumeInsideTask ? "支持任务内恢复" : "-";
}
