using System;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// 跨战斗持久化的 E 技能冷却跟踪器。
/// 释放 E 技能时通过 OCR 读取剩余 CD，记录到全局静态字典中。
/// 此后可通过 <see cref="IsReady"/> / <see cref="GetRemainingCd"/> 查询任意角色的 E 技能状态，
/// 即使跨越多场战斗和地图追踪简易策略节点。
/// </summary>
public static class ESkillCdTracker
{
    /// <summary>角色名 → E 技能就绪的时间戳（UTC）</summary>
    private static readonly ConcurrentDictionary<string, DateTime> EReadyAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 记录某角色 E 技能的冷却时间。
    /// </summary>
    /// <param name="characterName">角色名</param>
    /// <param name="cdSeconds">OCR 读取到的剩余冷却秒数</param>
    public static void RecordUse(string characterName, double cdSeconds)
    {
        if (string.IsNullOrEmpty(characterName) || cdSeconds <= 0) return;
        EReadyAt[characterName] = DateTime.UtcNow.AddSeconds(cdSeconds);
    }

    /// <summary>
    /// 获取指定角色 E 技能的剩余冷却秒数。
    /// 无记录（从未释放过或已过期）时返回 0（表示就绪）。
    /// </summary>
    public static double GetRemainingCd(string characterName)
    {
        if (string.IsNullOrEmpty(characterName)) return 0;
        if (EReadyAt.TryGetValue(characterName, out var readyAt))
        {
            var remaining = (readyAt - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }

        return 0;
    }

    /// <summary>
    /// 判断指定角色 E 技能是否就绪。
    /// </summary>
    public static bool IsReady(string characterName)
    {
        return GetRemainingCd(characterName) <= 0;
    }

    /// <summary>
    /// 清除所有角色的 E 技能冷却记录（用于调试或重置）。
    /// </summary>
    public static void Clear()
    {
        EReadyAt.Clear();
    }
}
