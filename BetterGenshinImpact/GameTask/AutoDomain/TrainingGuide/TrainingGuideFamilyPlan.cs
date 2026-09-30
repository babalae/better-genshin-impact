using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>单关卡的任务内库存快照。入口重扫覆盖库存，领奖增量只应用一次。</summary>
public sealed class TrainingGuideFamilyPlan
{
    public List<TrainingGuideMaterialReading> Materials { get; private set; }
    public int KnownResinSpent { get; private set; }
    private readonly Dictionary<string, long> _observedDrops = new();
    private int _observedResinSpent;
    private int? _initialResinBudget;
    public TrainingGuideFamilyPlan(IEnumerable<TrainingGuideMaterialReading> materials) => Materials = materials.ToList();
    public void Refresh(IEnumerable<TrainingGuideMaterialReading> materials) => Materials = materials.ToList();

    /// <summary>不使用奖励更新库存时，按首次估算的预算扣除实际消耗。</summary>
    public int? RemainingInitialResin(int reservePercent)
    {
        _initialResinBudget ??= RemainingResin(reservePercent);
        return _initialResinBudget is int budget ? Math.Max(0, budget - KnownResinSpent) : null;
    }

    public bool ApplyRewards(IReadOnlyDictionary<string, int>? rewards, int resin)
    {
        if (resin > 0) KnownResinSpent = checked(KnownResinSpent + resin);
        if (rewards == null || rewards.Count == 0 || rewards.Any(r => r.Value <= 0)) return false;
        var family = Materials[0].Material.Family;
        // 未知名称或其他家族的材料不能混入当前家族；回到入口读取真实库存。
        foreach (var name in rewards.Keys)
        {
            var material = TrainingGuideMaterialCatalog.Find(name);
            if (material != null && material.Family != family) return false;
            if (material == null && name is not ("摩拉" or "冒险阅历" or "角色经验" or "好感经验")) return false;
        }
        var drops = rewards.Select(r => (Material: TrainingGuideMaterialCatalog.Find(r.Key), Count: r.Value))
            .Where(r => r.Material?.Family == family).ToArray();
        if (drops.Length == 0) return false;
        foreach (var drop in drops)
        {
            var index = Materials.FindIndex(m => m.Material == drop.Material);
            if (index >= 0)
                Materials[index] = Materials[index] with { Stock = checked(Materials[index].Stock + drop.Count) };
            if (resin > 0)
                _observedDrops[drop.Material!.Name] = checked(_observedDrops.GetValueOrDefault(drop.Material.Name) + drop.Count);
        }
        if (resin > 0) _observedResinSpent = checked(_observedResinSpent + resin);
        return true;
    }

    public int? RemainingResin(int reservePercent)
    {
        var expected = TrainingGuideDropExpectations.Per20(Materials[0].Material.IsWeapon);
        // 保留一份20体先验，避免少量样本暂未掉落高级材料时把其期望直接置零。
        if (_observedResinSpent > 0)
            foreach (var item in TrainingGuideMaterialCatalog.Materials.Where(m => m.Family == Materials[0].Material.Family))
                expected[item.Tier] = (expected[item.Tier] + _observedDrops.GetValueOrDefault(item.Name)) * 20m / (_observedResinSpent + 20m);
        return TrainingGuideResinEstimator.Estimate(Materials, reservePercent, 20, expected);
    }
}
