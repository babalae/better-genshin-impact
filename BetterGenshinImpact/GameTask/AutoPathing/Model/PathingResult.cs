using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

/// <summary>
/// 一次地图追踪的不可变执行结果；完成状态和本次统计通过返回值传递，不读取执行器遗留状态。
/// </summary>
public sealed class PathingResult
{
    /// <summary>整份路线文件是否成功执行；任一路径段未完成时为 false。</summary>
    [JsonProperty("is_success")]
    public bool IsSuccess { get; }

    /// <summary>本次路线已确认成功的战斗次数，不等同于实际击杀数量。</summary>
    [JsonProperty("success_fight_count")]
    public int SuccessFightCount { get; }

    /// <summary>已知失败或提前结束原因；成功时为空，未处理异常仍按原有异常通道传播。</summary>
    [JsonProperty("error_message", NullValueHandling = NullValueHandling.Include)]
    public string? ErrorMessage { get; }

    /// <summary>本次复活次数；未接入完整统计时为 null，已统计且没有复活时才为 0。</summary>
    [JsonProperty("revive_count", NullValueHandling = NullValueHandling.Include)]
    public int? ReviveCount { get; }

    /// <summary>本次已确认拾取的物品名称与数量；null 表示未统计，空集合表示已统计但未拾取。</summary>
    [JsonProperty("collected_items", NullValueHandling = NullValueHandling.Include)]
    public IReadOnlyDictionary<string, int>? CollectedItems { get; }

    /// <summary>固定一次执行的完成状态及统计副本，后续执行或来源集合变化不会污染已有结果。</summary>
    [JsonConstructor]
    public PathingResult(bool isSuccess, int successFightCount = 0, string? errorMessage = null,
        int? reviveCount = null, IReadOnlyDictionary<string, int>? collectedItems = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(successFightCount);
        if (reviveCount is < 0) throw new ArgumentOutOfRangeException(nameof(reviveCount));
        if (isSuccess && !string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("成功结果不能同时包含失败信息。", nameof(errorMessage));
        IsSuccess = isSuccess;
        SuccessFightCount = successFightCount;
        ErrorMessage = errorMessage;
        ReviveCount = reviveCount;
        if (collectedItems is not null)
        {
            // 接入统计时仍保留未知与空集合的差异，并防止调用方继续修改本次结果。
            if (collectedItems.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value < 0))
                throw new ArgumentException("拾取物品名称不能为空，数量不能为负数。", nameof(collectedItems));
            CollectedItems = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(collectedItems, StringComparer.Ordinal));
        }
    }
}
