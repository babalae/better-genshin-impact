using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>策略目录树中的不可变文件或文件夹节点。</summary>
public sealed class CombatStrategyNode
{
    /// <summary>文件夹名或不含扩展名的策略名。</summary>
    public required string Name { get; init; }
    /// <summary>相对所属根目录的路径。</summary>
    public required string RelativePath { get; init; }
    /// <summary>是否为文件夹。</summary>
    public bool IsFolder { get; init; }
    /// <summary>文件的策略引用，文件夹为 null。</summary>
    public CombatStrategyRef? Strategy { get; init; }
    /// <summary>创建时间，用于录制的默认排序。</summary>
    public DateTime CreationTimeUtc { get; init; }
    /// <summary>修改时间，用于排序及元数据缓存失效。</summary>
    public DateTime LastWriteTimeUtc { get; init; }
    /// <summary>子节点，文件夹在前；文件为空列表。</summary>
    public IReadOnlyList<CombatStrategyNode> Children { get; init; } = [];
    /// <summary>子树内策略文件数量，文件为 1。</summary>
    public int FileCount { get; init; }
}
