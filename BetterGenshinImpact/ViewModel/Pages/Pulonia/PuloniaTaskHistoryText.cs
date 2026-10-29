using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 历史列表和详情共用的中文状态与时间格式，不把调度状态等同于业务核验结果。
/// </summary>
public static class PuloniaTaskHistoryText
{
    /// <summary>
    /// 把一次节点尝试的调度状态与业务完成程度合并为一个展示状态，界面不再并列显示两套状态。
    /// </summary>
    /// <param name="result">节点尝试结果。</param>
    /// <returns>
    /// Text：节点行显示的状态；Bucket：运行摘要中归入的类别；Category：着色分类。
    /// 执行结束但无法确认游戏结果、部分完成和待处理都归入“待核对”。
    /// </returns>
    public static (string Text, string Bucket, PuloniaTaskRunStatusCategory Category) AttemptStatus(PuloniaTaskNodeResult result)
    {
        // 1. 编排层直接给出的跳过：未启用、续跑沿用、路线 CD
        if (result.ReasonCode == PuloniaTaskRunClassifier.DisabledReason)
            return ("未启用", "未启用", PuloniaTaskRunStatusCategory.Neutral);
        if (result.ReasonCode == PuloniaTaskRunClassifier.ResumeSkippedReason)
            return ("之前已完成", "之前已完成", PuloniaTaskRunStatusCategory.Neutral);
        if (PuloniaCollectionCooldown.IsCooldownSkip(result))
            return ("路线 CD 中", "路线 CD 跳过", PuloniaTaskRunStatusCategory.Neutral);

        // 2. 调度状态本身已说明结果的情况
        switch (result.Status)
        {
            case PuloniaTaskNodeStatus.Skipped:
                return ("已跳过", "已跳过", PuloniaTaskRunStatusCategory.Neutral);
            case PuloniaTaskNodeStatus.Cancelled:
                return ("已停止", "已停止", PuloniaTaskRunStatusCategory.Neutral);
            case PuloniaTaskNodeStatus.TimedOut:
                return ("超时", "失败", PuloniaTaskRunStatusCategory.Failed);
            case PuloniaTaskNodeStatus.NeedsAttention:
                return ("待核对", "待核对", PuloniaTaskRunStatusCategory.Attention);
        }

        // 3. 执行完成或失败时，再按执行器报告的业务完成程度细分
        return result.OutcomeKind switch
        {
            PuloniaTaskOutcomeKind.ExecutedUnverified => ("已执行，待核对", "待核对", PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.PartiallySucceeded => ("部分完成", "待核对", PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.NeedsAttention => ("待核对", "待核对", PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.Cancelled => ("已停止", "已停止", PuloniaTaskRunStatusCategory.Neutral),
            _ when result.Status == PuloniaTaskNodeStatus.Succeeded => ("成功", "成功", PuloniaTaskRunStatusCategory.Success),
            _ => ("失败", "失败", PuloniaTaskRunStatusCategory.Failed)
        };
    }

    /// <summary>
    /// 运行摘要中各类别的显示顺序，未列出的类别排在最后。
    /// </summary>
    public static readonly string[] BucketOrder = ["成功", "待核对", "失败", "已停止", "路线 CD 跳过", "已跳过", "之前已完成"];

    /// <summary>
    /// 着色分类对应的视图触发器键：running、success、failed、attention，其余为 neutral。
    /// </summary>
    public static string CategoryKey(PuloniaTaskRunStatusCategory category) => category switch
    {
        PuloniaTaskRunStatusCategory.Running => "running",
        PuloniaTaskRunStatusCategory.Success => "success",
        PuloniaTaskRunStatusCategory.Failed => "failed",
        PuloniaTaskRunStatusCategory.Attention => "attention",
        _ => "neutral"
    };

    /// <summary>
    /// 着色分类对应的状态图标；中性状态由调用方区分排队与跳过。
    /// </summary>
    /// <param name="category">着色分类。</param>
    /// <param name="neutral">中性状态使用的图标。</param>
    public static SymbolRegular CategoryIcon(PuloniaTaskRunStatusCategory category, SymbolRegular neutral) => category switch
    {
        PuloniaTaskRunStatusCategory.Running => SymbolRegular.ArrowSync24,
        PuloniaTaskRunStatusCategory.Success => SymbolRegular.CheckmarkCircle24,
        PuloniaTaskRunStatusCategory.Failed => SymbolRegular.DismissCircle24,
        PuloniaTaskRunStatusCategory.Attention => SymbolRegular.Warning24,
        _ => neutral
    };

    /// <summary>
    /// 调用来源的中文说明；未知来源原样显示。
    /// </summary>
    public static string Source(string source) => source switch
    {
        "ui" => "手动执行",
        "schedule" => "定时触发",
        "hotkey" => "快捷键触发",
        "resume" => "续跑",
        "cli" => "命令行启动",
        "csharp" => "程序调用",
        _ => source
    };

    /// <summary>
    /// 失败后行为的中文说明，与执行时的取值一一对应；未设置按停止整个计划。
    /// </summary>
    public static string FailureBehavior(string? behavior) => behavior switch
    {
        "continue" => "继续执行后续任务",
        "skip_group" => "跳过所在分组",
        _ => "停止整个计划"
    };

    /// <summary>
    /// 按本地时区显示时间，缺失时间不推算。
    /// </summary>
    public static string Time(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    /// <summary>
    /// 列表中使用的短时间：今天只显示时刻，今年省略年份。
    /// </summary>
    public static string ShortTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today)
            return "今天 " + local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1))
            return "昨天 " + local.ToString("HH:mm");
        return local.Year == today.Year ? local.ToString("MM-dd HH:mm") : local.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// 显示已经结束的执行耗时，尚未结束不使用随读取时刻变化的伪固定耗时。
    /// </summary>
    public static string Duration(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null)
            return "尚未开始";
        if (end is null)
            return "执行中";
        return Duration(end.Value - start.Value);
    }

    /// <summary>
    /// 把时长格式化为秒、分或小时级别的短文字，小时级别省略秒。
    /// </summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed.TotalSeconds < 1)
            return "不到 1 秒";
        if (elapsed.TotalMinutes < 1)
            return $"{(int)elapsed.TotalSeconds} 秒";
        if (elapsed.TotalHours < 1)
            return $"{elapsed.Minutes} 分 {elapsed.Seconds} 秒";
        return $"{(int)elapsed.TotalHours} 小时 {elapsed.Minutes} 分";
    }
}
