namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送冻结 CD 补偿决策（纯函数）。
/// 传送/加载期间游戏世界冻结、技能 CD 不流逝，但 CD 计算基于挂钟时间外推会把这段误算成流逝。
/// 仅单人世界需要补偿：联机（多人世界）传送/加载期间游戏世界不暂停、CD 正常流逝，不能把这段当冻结时间。
/// </summary>
public static class TeleportCdCompensationDecisions
{
    /// <summary>
    /// 是否需要补偿一段传送/加载冻结时间对队伍 CD 的影响。
    /// </summary>
    /// <param name="isSoloWorld">是否处于单人世界（联机锄地运行时为 false）。</param>
    /// <returns>单人世界才补偿。</returns>
    public static bool ShouldCompensate(bool isSoloWorld)
    {
        return isSoloWorld;
    }
}