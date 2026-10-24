using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>
/// 单个仓库已保留归档的索引，对应 <c>Snapshots\&lt;仓库ID&gt;\retained.json</c>。
/// 归档块文件只用短 ID 命名；内容身份与来源修订只保存在本索引中，不进入路径。
/// </summary>
internal sealed class ScriptRetainedIndex
{
    /// <summary>归档块 ID（<c>blk_</c> + 16 位）到内容身份的映射；相同身份的范围跨修订复用同一块。</summary>
    [JsonProperty("blocks")]
    public Dictionary<string, string> Blocks { get; init; } = [];

    /// <summary>来源修订到“范围路径 → 归档块 ID”的映射；范围路径如 <c>repo/pathing/地方特产</c>。</summary>
    [JsonProperty("revisions")]
    public Dictionary<string, Dictionary<string, string>> Revisions { get; init; } = [];
}
