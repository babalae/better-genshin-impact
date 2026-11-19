using BetterGenshinImpact.Service.I18n;
using System;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 历史列表和详情共用的中文状态与时间格式，不把调度状态等同于业务核验结果。
/// 返回的展示文字按当前界面语言翻译，只用于界面展示；运行摘要类别（Bucket）保留中文 Key 参与分组排序，展示时再翻译。
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
            return (I18nService.Instance.Translate("未启用"), I18nService.Key("未启用"), PuloniaTaskRunStatusCategory.Neutral);
        if (result.ReasonCode == PuloniaTaskRunClassifier.ResumeSkippedReason)
            return (I18nService.Instance.Translate("之前已完成"), I18nService.Key("之前已完成"), PuloniaTaskRunStatusCategory.Neutral);
        if (PuloniaRouteCooldown.IsCooldownSkip(result))
            return (I18nService.Instance.Translate("路线 CD 中"), I18nService.Key("路线 CD 跳过"), PuloniaTaskRunStatusCategory.Neutral);

        // 2. 调度状态本身已说明结果的情况
        switch (result.Status)
        {
            case PuloniaTaskNodeStatus.Skipped:
                return (I18nService.Instance.Translate("已跳过"), I18nService.Key("已跳过"), PuloniaTaskRunStatusCategory.Neutral);
            case PuloniaTaskNodeStatus.Cancelled:
                return (I18nService.Instance.Translate("已停止"), I18nService.Key("已停止"), PuloniaTaskRunStatusCategory.Neutral);
            case PuloniaTaskNodeStatus.TimedOut:
                return (I18nService.Instance.Translate("超时"), I18nService.Key("失败"), PuloniaTaskRunStatusCategory.Failed);
            case PuloniaTaskNodeStatus.NeedsAttention:
                return (I18nService.Instance.Translate("待核对"), I18nService.Key("待核对"), PuloniaTaskRunStatusCategory.Attention);
        }

        // 3. 执行完成或失败时，再按执行器报告的业务完成程度细分
        return result.OutcomeKind switch
        {
            PuloniaTaskOutcomeKind.ExecutedUnverified => (I18nService.Instance.Translate("已执行，待核对"), I18nService.Key("待核对"), PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.PartiallySucceeded => (I18nService.Instance.Translate("部分完成"), I18nService.Key("待核对"), PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.NeedsAttention => (I18nService.Instance.Translate("待核对"), I18nService.Key("待核对"), PuloniaTaskRunStatusCategory.Attention),
            PuloniaTaskOutcomeKind.Cancelled => (I18nService.Instance.Translate("已停止"), I18nService.Key("已停止"), PuloniaTaskRunStatusCategory.Neutral),
            _ when result.Status == PuloniaTaskNodeStatus.Succeeded => (I18nService.Instance.Translate("成功"), I18nService.Key("成功"), PuloniaTaskRunStatusCategory.Success),
            _ => (I18nService.Instance.Translate("失败"), I18nService.Key("失败"), PuloniaTaskRunStatusCategory.Failed)
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
        "ui" => I18nService.Instance.Translate("手动执行"),
        "schedule" => I18nService.Instance.Translate("定时触发"),
        "hotkey" => I18nService.Instance.Translate("快捷键触发"),
        "resume" => I18nService.Instance.Translate("续跑"),
        "cli" => I18nService.Instance.Translate("命令行启动"),
        "csharp" => I18nService.Instance.Translate("程序调用"),
        PuloniaTaskRequest.DispatchSource => I18nService.Instance.Translate("主实例下发"),
        _ => source
    };

    /// <summary>
    /// 运行状态的中文显示文本；运行列表与主实例显示下发运行时共用。
    /// </summary>
    /// <param name="status">运行状态</param>
    public static string Status(PuloniaTaskRunStatus status) => status switch
    {
        PuloniaTaskRunStatus.Queued => I18nService.Instance.Translate("排队中"),
        PuloniaTaskRunStatus.Running => I18nService.Instance.Translate("运行中"),
        PuloniaTaskRunStatus.Cancelling => I18nService.Instance.Translate("停止中"),
        PuloniaTaskRunStatus.Succeeded => I18nService.Instance.Translate("已完成"),
        PuloniaTaskRunStatus.Failed => I18nService.Instance.Translate("已失败"),
        PuloniaTaskRunStatus.Cancelled => I18nService.Instance.Translate("已停止"),
        PuloniaTaskRunStatus.TimedOut => I18nService.Instance.Translate("已超时"),
        PuloniaTaskRunStatus.Interrupted => I18nService.Instance.Translate("已中断"),
        PuloniaTaskRunStatus.NeedsAttention => I18nService.Instance.Translate("待处理"),
        PuloniaTaskRunStatus.Expired => I18nService.Instance.Translate("已过期（未执行）"),
        _ => status.ToString()
    };

    /// <summary>
    /// 失败后行为的中文说明，与执行时的取值一一对应；未设置按停止整个计划。
    /// </summary>
    public static string FailureBehavior(string? behavior) => behavior switch
    {
        "continue" => I18nService.Instance.Translate("继续执行后续任务"),
        "skip_group" => I18nService.Instance.Translate("跳过所在分组"),
        _ => I18nService.Instance.Translate("停止整个计划")
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
            return string.Format(I18nService.Instance.Translate("今天 {0}"), local.ToString("HH:mm"));
        if (local.Date == today.AddDays(-1))
            return string.Format(I18nService.Instance.Translate("昨天 {0}"), local.ToString("HH:mm"));
        return local.Year == today.Year ? local.ToString("MM-dd HH:mm") : local.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// 显示已经结束的执行耗时，尚未结束不使用随读取时刻变化的伪固定耗时。
    /// </summary>
    public static string Duration(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null)
            return I18nService.Instance.Translate("尚未开始");
        if (end is null)
            return I18nService.Instance.Translate("执行中");
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
            return I18nService.Instance.Translate("不到 1 秒");
        if (elapsed.TotalMinutes < 1)
            return string.Format(I18nService.Instance.Translate("{0} 秒"), (int)elapsed.TotalSeconds);
        if (elapsed.TotalHours < 1)
            return string.Format(I18nService.Instance.Translate("{0} 分 {1} 秒"), elapsed.Minutes, elapsed.Seconds);
        return string.Format(I18nService.Instance.Translate("{0} 小时 {1} 分"), (int)elapsed.TotalHours, elapsed.Minutes);
    }
}
