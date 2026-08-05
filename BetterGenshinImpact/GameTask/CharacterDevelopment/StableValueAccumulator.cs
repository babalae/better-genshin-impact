using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.CharacterDevelopment;

/// <summary>
/// 连续一致值累加器。用于抑制单次 OCR 抖动，只有同一解析结果连续出现指定次数才视为稳定。
/// </summary>
/// <typeparam name="T">参与一致性比较的结构化结果类型。</typeparam>
/// <param name="requiredCount">判定稳定所需的连续相同次数。</param>
internal sealed class StableValueAccumulator<T>(int requiredCount)
{
    private readonly int _requiredCount = requiredCount > 0
        ? requiredCount
        : throw new ArgumentOutOfRangeException(nameof(requiredCount));
    private readonly IEqualityComparer<T> _comparer = EqualityComparer<T>.Default;
    private T? _lastValue;
    private bool _hasValue;

    /// <summary>
    /// 当前值已经连续出现的次数。
    /// </summary>
    public int ConsecutiveCount { get; private set; }

    /// <summary>
    /// 本轮采样中曾达到的最大连续次数，用于生成失败诊断信息。
    /// </summary>
    public int MaxConsecutiveCount { get; private set; }

    /// <summary>
    /// 加入一个有效采样值，并返回当前值是否已达到稳定要求。
    /// </summary>
    public bool Add(T value)
    {
        if (_hasValue && _comparer.Equals(_lastValue!, value))
        {
            ConsecutiveCount++;
        }
        else
        {
            _lastValue = value;
            _hasValue = true;
            ConsecutiveCount = 1;
        }

        MaxConsecutiveCount = Math.Max(MaxConsecutiveCount, ConsecutiveCount);
        return ConsecutiveCount >= _requiredCount;
    }

    /// <summary>
    /// 清除当前连续序列。OCR 为空或解析失败时必须调用，避免跨无效帧累计。
    /// </summary>
    public void Reset()
    {
        _lastValue = default;
        _hasValue = false;
        ConsecutiveCount = 0;
    }
}
