using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 计划中的账号选择，不存储登录凭据。
/// </summary>
public sealed class PuloniaTaskAccountBinding
{
    /// <summary>
    /// 外部账号资料的稳定 ID。
    /// </summary>
    [JsonProperty("account_id", Required = Required.Always)]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// 是否参与该计划。
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    // 按账号替换预设（preset_selections）随共享设置改造删除，是否按账号区分设置随 D5 决定。
}
