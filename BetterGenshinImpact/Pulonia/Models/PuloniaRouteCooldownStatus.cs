using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一份路线在某个账户下的路线 CD 判定结果，由 PuloniaRouteCooldown.GetStatus 计算，不含界面文字。
/// </summary>
/// <param name="State">路线状态。</param>
/// <param name="LastSuccessAt">最近一次整份路线执行成功的时间；没有完成记录时为空。</param>
/// <param name="NextEligibleAt">按当前规则从上次成功时间计算的下次可执行时间；没有记录或不限制时为空。</param>
/// <param name="Completion">对应的完成记录；没有记录时为空。</param>
public sealed record PuloniaRouteCooldownStatus(PuloniaRouteCooldownState State, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextEligibleAt, PuloniaRouteCompletion? Completion)
{
    /// <summary>本次运行到该节点时是否会跳过。</summary>
    public bool IsSkipped => State is PuloniaRouteCooldownState.CoolingDown or PuloniaRouteCooldownState.Completed;
}
