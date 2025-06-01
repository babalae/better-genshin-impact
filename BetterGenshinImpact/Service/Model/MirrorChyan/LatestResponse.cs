using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Service.Model.MirrorChyan;

#nullable enable
#pragma warning disable CS8618
#pragma warning disable CS8601
#pragma warning disable CS8603
public partial class LatestResponse
{
    /// <summary>
    /// 响应代码，https://github.com/MirrorChyan/docs/blob/main/ErrorCode.md
    /// </summary>
    [JsonPropertyName("code")]
    public long Code { get; set; }

    /// <summary>
    /// 响应数据
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public Data Data { get; set; }

    /// <summary>
    /// 响应信息
    /// </summary>
    [JsonPropertyName("msg")]
    public string Msg { get; set; }
}

/// <summary>
/// 响应数据
/// </summary>
public partial class Data
{
    /// <summary>
    /// 更新包架构
    /// </summary>
    [JsonPropertyName("arch")]
    public string Arch { get; set; }

    /// <summary>
    /// CDK过期时间戳
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cdk_expired_time")]
    public double? CdkExpiredTime { get; set; }

    /// <summary>
    /// 更新频道，stable | beta | alpha
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; }

    /// <summary>
    /// 自定义数据
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("custom_data")]
    public string CustomData { get; set; }

    /// <summary>
    /// 文件大小
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("filesize")]
    public long? Filesize { get; set; }

    /// <summary>
    /// 更新包系统
    /// </summary>
    [JsonPropertyName("os")]
    public string Os { get; set; }

    /// <summary>
    /// 发版日志
    /// </summary>
    [JsonPropertyName("release_note")]
    public string ReleaseNote { get; set; }

    /// <summary>
    /// sha256
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; }

    /// <summary>
    /// 更新包类型，incremental | full
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("update_type")]
    public string UpdateType { get; set; }

    /// <summary>
    /// 下载地址
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    public string Url { get; set; }

    /// <summary>
    /// 资源版本名称
    /// </summary>
    [JsonPropertyName("version_name")]
    public string VersionName { get; set; }

    /// <summary>
    /// 资源版本号仅内部使用
    /// </summary>
    [JsonPropertyName("version_number")]
    public long VersionNumber { get; set; }
}

#pragma warning restore CS8618
#pragma warning restore CS8601
#pragma warning restore CS8603