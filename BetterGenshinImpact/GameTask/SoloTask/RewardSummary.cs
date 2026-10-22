using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 一次任务观测到的奖励产出。它不等于已确认的副作用，周期额度仍由任务计划的完成事件结算。
/// </summary>
public sealed class RewardSummary
{
    /// <summary>
    /// 按名称汇总的奖励项。
    /// </summary>
    public List<RewardSummaryItem> Items { get; set; } = [];

    /// <summary>
    /// 从名称到数量的字典建立奖励汇总。
    /// </summary>
    /// <param name="rewards">名称到数量的字典。</param>
    /// <returns>奖励汇总。</returns>
    public static RewardSummary From(IEnumerable<KeyValuePair<string, int>> rewards) => new()
    {
        Items = rewards.Select(pair => new RewardSummaryItem { Name = pair.Key, Count = pair.Value }).ToList()
    };

    /// <summary>
    /// 转换为名称到数量的字典，供 JS 等旧接口按原形状返回。
    /// </summary>
    /// <returns>名称到数量的字典。</returns>
    public Dictionary<string, int> ToDictionary()
    {
        var result = new Dictionary<string, int>();
        foreach (var item in Items)
        {
            result[item.Name] = result.GetValueOrDefault(item.Name) + item.Count;
        }

        return result;
    }
}
