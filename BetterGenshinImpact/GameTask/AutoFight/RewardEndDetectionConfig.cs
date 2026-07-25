using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 当前地图追踪战斗点的奖励结束检测配置，不写回全局自动战斗设置。
/// </summary>
internal sealed class RewardEndDetectionConfig
{
    private RewardEndDetectionConfig(RewardEndDetectionType type, HashSet<int>? moraValues)
    {
        Type = type;
        MoraValues = moraValues;
    }

    public RewardEndDetectionType Type { get; }

    /// <summary>限制允许匹配的摩拉数值；null 表示使用全部已加载的摩拉模板。</summary>
    public HashSet<int>? MoraValues { get; }

    public bool IsMoraValueEnabled(int value)
    {
        return MoraValues is null || MoraValues.Contains(value);
    }

    public static bool TryCreate(string? rewardType, IEnumerable<int>? moraValues, out RewardEndDetectionConfig? config)
    {
        config = (rewardType ?? "").Trim().ToLowerInvariant() switch
        {
            "exp" or "experience" or "经验" or "经验值" =>
                new RewardEndDetectionConfig(RewardEndDetectionType.Experience, null),
            "mora" or "摩拉" =>
                new RewardEndDetectionConfig(
                    RewardEndDetectionType.Mora,
                    moraValues is null ? null : new HashSet<int>(moraValues)),
            _ => null
        };

        return config is not null;
    }
}

internal enum RewardEndDetectionType
{
    Mora,
    Experience
}