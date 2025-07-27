using System;
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
    /// 执行开始时间
    /// </summary>
    [JsonProperty("start_time")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 执行结束时间
    /// </summary>
    [JsonProperty("end_time")]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 是否执行成功
    /// </summary>
    [JsonProperty("is_successful")]
    public bool IsSuccessful { get; set; } = false;
}