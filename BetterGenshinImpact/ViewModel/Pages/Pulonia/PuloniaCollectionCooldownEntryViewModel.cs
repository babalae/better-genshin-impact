using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// CD 总览中的一条路线记录。总览没有计划上下文，状态与剩余时间按上次成功时的规则（账本中存的下次可执行时间）计算；
/// 计划中的节点按当前规则判定，以任务树标记为准。
/// </summary>
public sealed class PuloniaCollectionCooldownEntryViewModel
{
    /// <summary>原始账本条目。</summary>
    public PuloniaTaskLedgerEntry Entry { get; }

    /// <summary>记录身份，供清除使用。</summary>
    public PuloniaCollectionCooldownKey Key { get; }

    /// <summary>所属的材料分组；列表按它分组显示。</summary>
    public PuloniaCollectionCooldownGroupViewModel Group { get; internal set; } = null!;

    /// <summary>按记录时规则的状态。</summary>
    public PuloniaCollectionCooldownState State { get; }

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

    /// <summary>作用域显示名。</summary>
    public string ScopeText { get; }

    /// <summary>悬停说明：来源运行与文件身份。</summary>
    public string ToolTip { get; }

    /// <summary>本次运行是否会跳过（按记录时规则）。</summary>
    public bool IsSkipped => State is PuloniaCollectionCooldownState.CoolingDown or PuloniaCollectionCooldownState.Completed;

    /// <summary>用于搜索的组合文字。</summary>
    internal string SearchText => RouteName + "\n" + Location + "\n" + Material;

    /// <summary>建立一条记录。</summary>
    /// <param name="entry">账本条目。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="accountNamesByUid">UID → 账号资料名称。</param>
    public PuloniaCollectionCooldownEntryViewModel(PuloniaTaskLedgerEntry entry, DateTimeOffset now,
        IReadOnlyDictionary<string, string> accountNamesByUid)
    {
        Entry = entry;
        Key = new PuloniaCollectionCooldownKey(entry.ScopeKey, entry.EffectKey);
        var data = entry.Evidence.Data;
        var identity = PuloniaCollectionCooldown.TryGetFileIdentity(entry.EffectKey, out var fileIdentity) ? fileIdentity : entry.EffectKey;
        var path = DescribePath(identity);
        RouteName = Path.GetFileNameWithoutExtension(path);
        Location = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        Material = data.Value<string>("collection_material") ?? "未识别";
        RuleText = data.Value<string>("collection_rule") ?? "未记录规则";
        LastSuccessText = PuloniaCooldownText.Time(entry.OccurredAt);
        // 记录时规则：一次性没有下次时间；其余按存下的下次可执行时间
        var policy = data["policy"] as JObject;
        var mode = policy?.Value<string>("Mode") ?? policy?.Value<string>("mode");
        State = mode == "once" ? PuloniaCollectionCooldownState.Completed
            : entry.NextEligibleAt > now ? PuloniaCollectionCooldownState.CoolingDown
            : entry.NextEligibleAt is null ? PuloniaCollectionCooldownState.NotChecked
            : PuloniaCollectionCooldownState.Ready;
        NextText = State switch
        {
            PuloniaCollectionCooldownState.CoolingDown => PuloniaCooldownText.Next(entry.NextEligibleAt!.Value, now),
            PuloniaCollectionCooldownState.Completed => "不再自动执行",
            PuloniaCollectionCooldownState.NotChecked => "记录时不限制",
            _ => "可执行"
        };
        ScopeText = PuloniaCooldownText.Scope(entry.ScopeKey, accountNamesByUid);
        var runId = entry.TaskRunId.Split(':')[0];
        ToolTip = $"来源运行：{runId}\n文件：{identity}\n规则：{RuleText}";
    }

    /// <summary>把文件身份转换为可读路径：仓库路线去掉仓库前缀，本地路线显示完整路径。</summary>
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
public sealed class PuloniaCollectionCooldownGroupViewModel
{
    /// <summary>材料或类别名称。</summary>
    public string Material { get; }

    /// <summary>组头文字，例如“落落莓 · 12 条 · 8 条 CD 中 · 最早明天 04:00”。</summary>
    public string HeaderText { get; }

    /// <summary>按组内当前显示的记录建立组头。</summary>
    /// <param name="material">材料或类别名称。</param>
    /// <param name="entries">组内记录。</param>
    public PuloniaCollectionCooldownGroupViewModel(string material, IReadOnlyCollection<PuloniaCollectionCooldownEntryViewModel> entries)
    {
        Material = material;
        var cooling = entries.Where(entry => entry.State == PuloniaCollectionCooldownState.CoolingDown).ToArray();
        var completed = entries.Count(entry => entry.State == PuloniaCollectionCooldownState.Completed);
        var parts = new List<string> { material, $"{entries.Count} 条" };
        if (cooling.Length > 0) parts.Add($"{cooling.Length} 条 CD 中");
        if (completed > 0) parts.Add($"{completed} 条已完成");
        if (cooling.Select(entry => entry.Entry.NextEligibleAt).Min() is { } earliest)
            parts.Add($"最早 {earliest.ToLocalTime():MM-dd HH:mm} 可执行");
        HeaderText = string.Join(" · ", parts);
    }
}
