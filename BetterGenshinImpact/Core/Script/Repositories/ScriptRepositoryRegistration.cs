using Newtonsoft.Json;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>已拉取或本地维护的脚本仓库注册信息，独立于用户选择的浏览渠道。</summary>
public sealed class ScriptRepositoryRegistration
{
    /// <summary>重新定位或重命名时保持不变的仓库 ID。</summary>
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>资源选择界面展示的仓库名称。</summary>
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>本机拉取目录的位置。</summary>
    [JsonProperty("directory")]
    public string Directory { get; init; } = string.Empty;

    /// <summary>是否在资源选择中显示；移除后仍保留稳定身份供旧任务读取。</summary>
    [JsonProperty("enabled")]
    public bool IsEnabled { get; init; } = true;

    /// <summary>来源类型：official 为内置官方来源，remote 为托管远程来源，local 为本地引用。</summary>
    [JsonProperty("kind")]
    public string Kind { get; init; } = "local";

    /// <summary>同一个仓库可配置多个远端；第三方界面仅编辑当前远端。</summary>
    [JsonProperty("remotes")]
    public IReadOnlyList<ScriptRepositoryRemote> Remotes { get; init; } = [];

    /// <summary>当前使用的远端标识；官方渠道由共享 ScriptConfig 决定。</summary>
    [JsonProperty("active_remote_id")]
    public string? ActiveRemoteId { get; init; }

    /// <summary>拉取分支；官方固定 release，第三方可自行指定。</summary>
    [JsonProperty("branch")]
    public string Branch { get; init; } = "release";

    /// <summary>官方身份使用保留 ID 判定，不能通过改名规避保护。</summary>
    [JsonIgnore]
    public bool IsOfficial => Id == ScriptRepositoryStore.OfficialRepositoryId;

    /// <summary>仅托管的官方和第三方远程仓库允许拉取与重置。</summary>
    [JsonIgnore]
    public bool IsRemote => IsOfficial || Kind == "remote";
}
