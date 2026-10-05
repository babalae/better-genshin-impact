using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Script.Repositories;

/// <summary>同一个逻辑仓库的远端或镜像地址，切换地址不改变仓库身份。</summary>
public sealed class ScriptRepositoryRemote
{
    /// <summary>远端的稳定标识，用于选择当前更新地址。</summary>
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>界面展示的渠道名称。</summary>
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Git 远端地址，不包含另行存储的认证令牌。</summary>
    [JsonProperty("url")]
    public string Url { get; init; } = string.Empty;
}
