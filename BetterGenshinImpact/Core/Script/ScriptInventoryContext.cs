using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script;

/// <summary>宿主确定的一次脚本库存会话；不由 JS 提供账号、来源或批次身份。</summary>
public sealed class ScriptInventoryContext
{
    /// <summary>一次节点尝试的稳定批次标识；直接执行脚本时每次生成新标识。</summary>
    public string BatchId { get; }
    /// <summary>调度器已核验的游戏 UID；其他入口首次访问时识别。</summary>
    public string? VerifiedUid { get; }
    /// <summary>稳定脚本目录标识，仓库脚本包含仓库前缀。</summary>
    public string Source { get; }
    /// <summary>脚本的可读名称。</summary>
    public string DisplayName { get; }

    /// <summary>建立直接运行或调度节点的库存上下文。</summary>
    public ScriptInventoryContext(string source, string displayName, string? verifiedUid = null,
        string? runId = null, string? taskAddress = null, int attempt = 1)
    {
        if (runId != null && (string.IsNullOrWhiteSpace(taskAddress) || attempt < 1))
            throw new ArgumentException("调度库存上下文缺少有效节点地址或尝试次数。");
        Source = source;
        DisplayName = displayName;
        VerifiedUid = verifiedUid;
        BatchId = runId == null ? Guid.NewGuid().ToString("N") :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(new { runId, taskAddress, attempt }))));
    }

    /// <summary>分组名仅在本次执行内有效；省略时保留默认批次，不允许指定外部批次 ID。</summary>
    public string GetBatchId(string? group)
    {
        if (group == null) return BatchId;
        if (string.IsNullOrWhiteSpace(group) || group.Length > 128)
            throw new ArgumentException("batch 必须是 1～128 个字符的非空分组名。");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(new { execution = BatchId, group }))));
    }

}
