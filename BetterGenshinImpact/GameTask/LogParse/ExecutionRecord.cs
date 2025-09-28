using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.LogParse;

public class ExecutionRecord
{
    [JsonProperty("guid")] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonProperty("group_name")] public string GroupName { get; set; } = string.Empty;
    [JsonProperty("project_name")] public string ProjectName { get; set; } = string.Empty;
    [JsonProperty("folder_name")] public string FolderName { get; set; } = string.Empty;
    [JsonProperty("type")] public string Type = string.Empty;

    /// <summary>
    /// 执行开始时间（服务器时间，包含时区信息）
    /// </summary>
    [JsonProperty("server_start_time")]
    public DateTimeOffset? ServerStartTime { get; set; }

    /// <summary>
    /// 执行开始时间的本地时间表示（用于向后兼容的统计显示）
    /// </summary>
    [JsonProperty("start_time")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 执行结束时间（服务器时间，包含时区信息）
    /// </summary>
    [JsonProperty("server_end_time")]
    public DateTimeOffset? ServerEndTime { get; set; }

    /// <summary>
    /// 执行结束时间的本地时间表示（用于向后兼容的统计显示）
    /// </summary>
    [JsonProperty("end_time")]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 是否执行成功
    /// </summary>
    [JsonProperty("is_successful")]
    public bool IsSuccessful { get; set; } = false;
}