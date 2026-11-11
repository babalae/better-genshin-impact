using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// CD 总览中的一条完成记录。总览没有计划上下文，状态与剩余时间按记录时规则（完成记录中的 <c>rule</c>）和账户的服务器时区推算；
/// 计划中的节点按当前规则判定，以任务树标记为准。
/// </summary>
public sealed class PuloniaRouteCooldownEntryViewModel
{
    /// <summary>原始完成记录。</summary>
    public PuloniaRouteCompletion Completion { get; }

    /// <summary>记录身份（账户与路线），供清除使用。</summary>
    public PuloniaCooldownRecordKey Key { get; }

    /// <summary>所属的材料分组；列表按它分组显示。</summary>
    public PuloniaRouteCooldownGroupViewModel Group { get; internal set; } = null!;

    /// <summary>按记录时规则的状态。</summary>
    public PuloniaRouteCooldownState State { get; }

    /// <summary>按记录时规则推算的下次可执行时间；一次性或不检查时为空。</summary>
    public DateTimeOffset? NextEligibleAt { get; }

    /// <summary>路线文件名（不含扩展名）。</summary>
    public string RouteName { get; }

    /// <summary>路线所在目录，仓库路线为仓库内相对目录。</summary>
    public string Location { get; }

    /// <summary>记录时识别的材料或类别；未识别时为“未识别”。</summary>
    public string Material { get; }

    /// <summary>记录时的规则说明。</summary>
    public string RuleText { get; }

    /// <summary>上次成功时间。</summary>
    public string LastSuccessText { get; }

    /// <summary>下次可执行说明：剩余时间与绝对时间。</summary>
    public string NextText { get; }

    /// <summary>状态文字。</summary>
    public string StateText => PuloniaCooldownText.State(State);

    /// <summary>记录所属账户的显示名。</summary>
    public string ScopeText { get; }

    /// <summary>悬停说明：来源运行、执行实例与路线身份。</summary>
    public string ToolTip { get; }

    /// <summary>本次运行是否会跳过（按记录时规则）。</summary>
    public bool IsSkipped => State is PuloniaRouteCooldownState.CoolingDown or PuloniaRouteCooldownState.Completed;

    /// <summary>用于搜索的组合文字。</summary>
    internal string SearchText => RouteName + "\n" + Location + "\n" + Material;

    /// <summary>建立一条记录。</summary>
    /// <param name="completion">完成记录。</param>
    /// <param name="accountId">记录所属账户 ID。</param>
    /// <param name="accountName">记录所属账户名称。</param>
    /// <param name="serverUtcOffsetMinutes">账户服务器时区的分钟偏移。</param>
    /// <param name="now">当前时间。</param>
    public PuloniaRouteCooldownEntryViewModel(PuloniaRouteCompletion completion, string accountId, string accountName,
        int serverUtcOffsetMinutes, DateTimeOffset now)
    {
        Completion = completion;
        Key = new PuloniaCooldownRecordKey(accountId, completion.Route);
        var path = DescribePath(completion.Route);
        RouteName = Path.GetFileNameWithoutExtension(path);
        Location = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        // 记录时规则：从完成记录的规则摘要还原，状态与下次可执行时间都按它推算
        var rule = completion.Rule.ToRefreshRule(completion.Route);
        Material = rule.MaterialName ?? "未识别";
        RuleText = rule.Description;
        LastSuccessText = PuloniaCooldownText.Time(completion.CompletedAt);
        var status = PuloniaRouteCooldown.GetStatus(rule, completion, now, serverUtcOffsetMinutes);
        State = status.State;
        NextEligibleAt = status.NextEligibleAt;
        NextText = State switch
        {
            PuloniaRouteCooldownState.CoolingDown => PuloniaCooldownText.Next(status.NextEligibleAt!.Value, now),
            PuloniaRouteCooldownState.Completed => "不再自动执行",
            PuloniaRouteCooldownState.NotChecked or PuloniaRouteCooldownState.Unmatched => "记录时不限制",
            _ => "可执行"
        };
        ScopeText = accountName;
        ToolTip = $"来源运行：{completion.RunId}\n执行实例：{PuloniaInstanceKey.DescribeDirectory(completion.Instance.Replace(':', '-'))}\n"
                  + $"路线：{completion.Route}\n记录时规则：{RuleText}";
    }

    /// <summary>把路线身份转换为可读路径：仓库路线去掉仓库前缀，本地路线显示完整路径。</summary>
    private static string DescribePath(string identity)
    {
        if (identity.StartsWith("repository:", StringComparison.Ordinal))
        {
            var slash = identity.IndexOf('/');
            return slash >= 0 ? identity[(slash + 1)..] : identity;
        }
        return identity.StartsWith("local:", StringComparison.Ordinal) ? identity["local:".Length..] : identity;
    }
}

/// <summary>
/// CD 总览中按材料分组的组头：记录数、仍在 CD 的数量与最早可执行时间。
/// </summary>
public sealed class PuloniaRouteCooldownGroupViewModel
{
    /// <summary>材料或类别名称。</summary>
    public string Material { get; }

    /// <summary>组头文字，例如“落落莓 · 12 条 · 8 条 CD 中 · 最早明天 04:00”。</summary>
    public string HeaderText { get; }

    /// <summary>按组内当前显示的记录建立组头。</summary>
    /// <param name="material">材料或类别名称。</param>
    /// <param name="entries">组内记录。</param>
    public PuloniaRouteCooldownGroupViewModel(string material, IReadOnlyCollection<PuloniaRouteCooldownEntryViewModel> entries)
    {
        Material = material;
        var cooling = entries.Where(entry => entry.State == PuloniaRouteCooldownState.CoolingDown).ToArray();
        var completed = entries.Count(entry => entry.State == PuloniaRouteCooldownState.Completed);
        var parts = new List<string> { material, $"{entries.Count} 条" };
        if (cooling.Length > 0) parts.Add($"{cooling.Length} 条 CD 中");
        if (completed > 0) parts.Add($"{completed} 条已完成");
        if (cooling.Select(entry => entry.NextEligibleAt).Min() is { } earliest)
            parts.Add($"最早 {earliest.ToLocalTime():MM-dd HH:mm} 可执行");
        HeaderText = string.Join(" · ", parts);
    }
}
