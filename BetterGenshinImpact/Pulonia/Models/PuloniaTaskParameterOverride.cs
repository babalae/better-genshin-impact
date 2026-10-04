using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 分组公共参数的作用域，JS 自定义参数必须同时匹配资源和参数版本；宿主公共设置可以跨资源继承。
/// </summary>
public sealed class PuloniaTaskParameterOverride
{
    /// <summary>
    /// 目标任务类型。
    /// </summary>
    [JsonProperty("task_type", Required = Required.Always)]
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 特定资源的稳定标识；包含 JS 自定义参数时不允许省略。
    /// </summary>
    [JsonProperty("resource_id")]
    public string? ResourceId { get; set; }

    /// <summary>
    /// 参数格式版本，必须与类型说明匹配。
    /// </summary>
    [JsonProperty("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// 本层覆盖值；顶层字段整体替换，不合并数组或嵌套对象。
    /// </summary>
    [JsonProperty("values", Required = Required.DisallowNull)]
    public JObject Values { get; set; } = new();
}
