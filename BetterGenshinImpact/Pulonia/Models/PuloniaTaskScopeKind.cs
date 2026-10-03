namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// CD 与额度归属的游戏身份范围。
/// </summary>
public enum PuloniaTaskScopeKind
{
    /// <summary>
    /// 跟随实际领取奖励的账号。
    /// </summary>
    Account,

    /// <summary>
    /// 跟随资源所在世界的拥有者。
    /// </summary>
    World
}
