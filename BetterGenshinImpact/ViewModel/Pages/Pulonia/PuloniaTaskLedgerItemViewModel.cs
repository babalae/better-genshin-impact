using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 把当前 CD 与额度账本项整理为运行页可直接展示的只读条目。
/// </summary>
public sealed class PuloniaTaskLedgerItemViewModel
{
    /// <summary>
    /// 原始账本项。
    /// </summary>
    public PuloniaTaskLedgerEntry Entry { get; }

    /// <summary>
    /// 稳定资源或活动 ID。
    /// </summary>
    public string EffectKey => Entry.EffectKey;

    /// <summary>
    /// 账号或世界作用域。
    /// </summary>
    public string ScopeKey => Entry.ScopeKey;

    /// <summary>
    /// 最近确认时间。
    /// </summary>
    public string OccurredAtText => Entry.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// 下次可执行时间；周期额度显示窗口键。
    /// </summary>
    public string EligibilityText => Entry.NextEligibleAt is { } next
        ? "下次可执行：" + next.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : $"周期：{Entry.WindowKey} · 已用 {Entry.Units}";

    /// <summary>
    /// 证据来源。
    /// </summary>
    public string EvidenceText => $"证据：{Entry.Evidence.Kind} / {Entry.Evidence.Source}";

    /// <summary>
    /// 建立只读账本条目。
    /// </summary>
    public PuloniaTaskLedgerItemViewModel(PuloniaTaskLedgerEntry entry)
    {
        Entry = entry;
    }
}
