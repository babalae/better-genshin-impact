using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 秘境奖励停止目标。
/// </summary>
public partial class TargetItem : ObservableObject
{
    public TargetItem()
    {
    }

    public TargetItem(string itemName, int targetCount)
    {
        ItemName = itemName;
        TargetCount = targetCount;
    }

    /// <summary>
    /// 目标物品名。
    /// </summary>
    [ObservableProperty]
    private string _itemName = string.Empty;

    /// <summary>
    /// 目标数量。
    /// </summary>
    [ObservableProperty]
    private int _targetCount = 1;

    partial void OnTargetCountChanged(int value)
    {
        if (value < 1)
        {
            TargetCount = 1;
        }
    }

    /// <summary>
    /// 深拷贝目标列表，避免配置与任务参数共享可变对象。
    /// </summary>
    public static List<TargetItem> CloneAll(IEnumerable<TargetItem>? source)
    {
        return source?.Select(x => new TargetItem
        {
            ItemName = x.ItemName,
            TargetCount = x.TargetCount
        }).ToList() ?? [];
    }
}

/// <summary>
/// 纯计算器使用的目标定义。
/// </summary>
public sealed record TargetItemSpec(string ItemName, int TargetCount);

/// <summary>
/// 单个目标的进度。
/// </summary>
public sealed record TargetItemProgress(string ItemName, int TargetCount, int CountedCount, bool IsSatisfied);

/// <summary>
/// 一组目标的进度。
/// </summary>
public sealed record TargetProgress(IReadOnlyList<TargetItemProgress> Targets)
{
    /// <summary>
    /// 所有目标是否都已满足；空目标不算满足。
    /// </summary>
    public bool AllSatisfied => Targets.Count > 0 && Targets.All(x => x.IsSatisfied);
}

/// <summary>
/// 合成配方边。
/// </summary>
public sealed record RewardCraftRecipe(string InputName, string OutputName, int InputCount, int OutputCount);

/// <summary>
/// 秘境类型。
/// </summary>
public enum RewardDomainType
{
    /// <summary>
    /// 天赋材料秘境。
    /// </summary>
    TalentMaterial,

    /// <summary>
    /// 武器材料秘境。
    /// </summary>
    WeaponMaterial
}

/// <summary>
/// 秘境掉落档案。
/// </summary>
public sealed record DomainRewardProfile(
    string Name,
    RewardDomainType Type,
    IReadOnlyList<TargetItemOption> Drops);

/// <summary>
/// 首领档案。
/// </summary>
public sealed record BossProfile(string Name, string BossMaterial, IReadOnlyList<BossGemFamily> GemFamilies);

/// <summary>
/// 首领宝石系列。
/// </summary>
public sealed record BossGemFamily(string Name, IReadOnlyList<BossGemRarityItem> Items);

/// <summary>
/// 宝石系列中的单档物品。
/// </summary>
public sealed record BossGemRarityItem(int Rarity, string ItemName);

/// <summary>
/// 秘境物品下拉选项。
/// </summary>
public sealed record TargetItemOption(string Name, string DisplayName);

/// <summary>
/// 经验书选项。
/// </summary>
public sealed record ExpItemOption(string Name, int ExpValue);

/// <summary>
/// 奖励停止配置错误。
/// </summary>
public sealed class TargetItemConfigurationException : Exception
{
    public TargetItemConfigurationException(string message) : base(message)
    {
    }
}
