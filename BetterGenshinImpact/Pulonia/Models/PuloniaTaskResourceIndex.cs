using System.Collections.Generic;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 一种任务能力的只读轻量资源索引及扫描诊断。
/// </summary>
public sealed class PuloniaTaskResourceIndex
{
    /// <summary>
    /// 按相对路径稳定排序的资源清单。
    /// </summary>
    public IReadOnlyList<PuloniaTaskResourceDescriptor> Resources { get; }

    /// <summary>
    /// 扫描是否因安全上限而截断。
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// 扫描期间跳过的不可访问目录数量。
    /// </summary>
    public int SkippedDirectoryCount { get; }

    /// <summary>
    /// 建立不可变资源索引。
    /// </summary>
    public PuloniaTaskResourceIndex(IReadOnlyList<PuloniaTaskResourceDescriptor> resources,
        bool isTruncated, int skippedDirectoryCount)
    {
        Resources = resources;
        IsTruncated = isTruncated;
        SkippedDirectoryCount = skippedDirectoryCount;
    }
}
