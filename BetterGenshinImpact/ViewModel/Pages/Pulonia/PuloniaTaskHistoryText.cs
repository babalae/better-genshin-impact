using System;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 历史列表和详情共用的中文状态与时间格式，不把调度状态等同于业务核验结果。
/// </summary>
public static class PuloniaTaskHistoryText
{
    /// <summary>
    /// 显示节点调度状态。
    /// </summary>
    public static string NodeStatus(PuloniaTaskNodeStatus status) => status switch
    {
        PuloniaTaskNodeStatus.Succeeded => "执行完成",
        PuloniaTaskNodeStatus.Failed => "失败",
        PuloniaTaskNodeStatus.Cancelled => "取消",
        PuloniaTaskNodeStatus.TimedOut => "超时",
        PuloniaTaskNodeStatus.NeedsAttention => "待处理",
        PuloniaTaskNodeStatus.Skipped => "跳过",
        _ => status.ToString()
    };

    /// <summary>
    /// 显示执行器实际报告的业务完成程度。
    /// </summary>
    public static string Outcome(PuloniaTaskOutcomeKind kind) => kind switch
    {
        PuloniaTaskOutcomeKind.Succeeded => "已确认成功",
        PuloniaTaskOutcomeKind.ExecutedUnverified => "已执行，未核验",
        PuloniaTaskOutcomeKind.PartiallySucceeded => "部分成功",
        PuloniaTaskOutcomeKind.Failed => "失败",
        PuloniaTaskOutcomeKind.Cancelled => "取消",
        PuloniaTaskOutcomeKind.Skipped => "跳过",
        PuloniaTaskOutcomeKind.NeedsAttention => "待人工核对",
        _ => kind.ToString()
    };

    /// <summary>
    /// 按本地时区显示时间，缺失时间不推算。
    /// </summary>
    public static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    /// <summary>
    /// 显示已经结束的执行耗时，尚未结束不使用随读取时刻变化的伪固定耗时。
    /// </summary>
    public static string Duration(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null)
            return "尚未开始";
        if (end is null)
            return "执行中";
        var elapsed = end.Value - start.Value;
        return elapsed.TotalMinutes < 1
            ? $"{Math.Max(0, elapsed.TotalSeconds):0.###} 秒"
            : $"{Math.Max(0, Math.Floor(elapsed.TotalHours)):0} 小时 {elapsed.Minutes} 分 {elapsed.Seconds} 秒";
    }
}
