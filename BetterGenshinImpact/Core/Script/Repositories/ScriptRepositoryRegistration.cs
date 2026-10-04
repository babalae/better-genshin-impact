using Newtonsoft.Json;

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
}
