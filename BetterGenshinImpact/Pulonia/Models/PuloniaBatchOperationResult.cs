using System.Collections.Generic;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 仓库同步或任务资源更新的简单批量结果；失败原因按实际处理项保留。
/// </summary>
public sealed class PuloniaBatchOperationResult
{
    /// <summary>成功处理的仓库或任务资源数量。</summary>
    public int Succeeded { get; }

    /// <summary>失败处理的仓库或任务资源数量。</summary>
    public int Failed => FailureReasons.Count;

    /// <summary>供最终汇总展示的逐项失败原因。</summary>
    public IReadOnlyList<string> FailureReasons { get; }

    /// <summary>建立一份不可变的批量处理结果。</summary>
    public PuloniaBatchOperationResult(int succeeded, IReadOnlyList<string> failureReasons)
    {
        Succeeded = succeeded;
        FailureReasons = failureReasons;
    }
}
