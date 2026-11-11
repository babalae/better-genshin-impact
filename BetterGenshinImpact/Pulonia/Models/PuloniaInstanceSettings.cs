using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 实例设置 <c>User/Pulonia/instances/&lt;实例目录名&gt;/settings.json</c>：唯一按实例保存的 Pulonia 设置。
/// 只描述“这个实例的游戏现在登录的是哪个账户”，不进入计划和快照。
/// </summary>
public sealed class PuloniaInstanceSettings
{
    /// <summary>当前支持的实例设置格式版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>格式版本，缺失或不同时拒绝读取。</summary>
    [JsonProperty("schema_version", Required = Required.Always)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 实例当前账户 ID；<c>null</c> 表示“未确定”（例如切换中途失败），跟随当前账户的运行会进入待处理。
    /// 字段必须写出，缺失视为格式错误。
    /// </summary>
    [JsonProperty("current_account_id", Required = Required.AllowNull)]
    public string? CurrentAccountId { get; set; }
}

/// <summary>
/// 列出实例设置时的一项：实例目录名与其设置。
/// </summary>
/// <param name="DirectoryName">实例目录名，如 <c>primary</c>、<c>webview-小号a</c></param>
/// <param name="Settings">读取到的实例设置</param>
public sealed record PuloniaInstanceSettingsEntry(string DirectoryName, PuloniaInstanceSettings Settings);
