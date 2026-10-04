using System;
using System.Linq;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>仓库资源的稳定定位与已确认来源版本；不包含本机缓存路径。</summary>
public sealed class ScriptResourceReference
{
    /// <summary>仓库注册的稳定 ID。</summary>
    [JsonProperty("repository_id", Required = Required.Always)]
    public string RepositoryId { get; init; } = string.Empty;

    /// <summary>相对于仓库 repo/ 内容目录的资源路径。</summary>
    [JsonProperty("relative_path", Required = Required.Always)]
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>资源定位的 Git 提交或文件式来源版本，确认保存任务前必须完成内容保留。</summary>
    [JsonProperty("approved_revision", Required = Required.Always)]
    public string ApprovedRevision { get; init; } = string.Empty;

    /// <summary>不包含版本的参数作用域键，防止同名资源跨仓库共用参数。</summary>
    [JsonIgnore]
    public string ScopeKey => RepositoryId + "/" + RelativePath;

    /// <summary>为同一版本的子资源建立独立定位。</summary>
    public ScriptResourceReference WithPath(string path) => new()
    {
        RepositoryId = RepositoryId, RelativePath = ScriptRepositorySnapshot.NormalizePath(path),
        ApprovedRevision = ApprovedRevision
    };

    /// <summary>为显式采用的新版本建立定位，原对象保持不变。</summary>
    public ScriptResourceReference WithRevision(string revision) => new()
    {
        RepositoryId = RepositoryId, RelativePath = RelativePath, ApprovedRevision = revision
    };

    /// <summary>拒绝路径逃逸、无效身份和不受支持的版本格式。</summary>
    public void Validate()
    {
        if (RepositoryId.Length != 32 || !RepositoryId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || ApprovedRevision.Length is not (40 or 64)
            || !ApprovedRevision.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || string.IsNullOrWhiteSpace(RelativePath))
            throw new ArgumentException("仓库资源身份或固定版本无效。");
        if (ScriptRepositorySnapshot.NormalizePath(RelativePath) != RelativePath)
            throw new ArgumentException("仓库资源路径必须使用规范化相对路径。");
    }
}
