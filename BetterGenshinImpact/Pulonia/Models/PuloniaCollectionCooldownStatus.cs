using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一份路线文件在某个账号下的 CD 判定结果，由 PuloniaCollectionCooldown.GetStatus 计算，不含界面文字。
/// </summary>
/// <param name="State">文件状态。</param>
/// <param name="LastSuccessAt">最近一次整份文件执行成功的时间；没有记录时为空。</param>
/// <param name="NextEligibleAt">按当前规则从上次成功时间计算的下次可执行时间；没有记录或不限制时为空。</param>
/// <param name="Entry">对应的账本条目；没有记录时为空。</param>
public sealed record PuloniaCollectionCooldownStatus(PuloniaCollectionCooldownState State, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextEligibleAt, PuloniaTaskLedgerEntry? Entry)
{
    /// <summary>本次运行到该节点时是否会跳过。</summary>
    public bool IsSkipped => State is PuloniaCollectionCooldownState.CoolingDown or PuloniaCollectionCooldownState.Completed;
}

/// <summary>
/// 账本中一条记录的身份：作用域与资源键，每个身份只保留最新一条记录。
/// </summary>
/// <param name="ScopeKey">作用域键，形如 <c>cn/world/{身份}</c>。</param>
/// <param name="EffectKey">资源键，形如 <c>pathing.file:{文件身份}</c>。</param>
public sealed record PuloniaCollectionCooldownKey(string ScopeKey, string EffectKey);
