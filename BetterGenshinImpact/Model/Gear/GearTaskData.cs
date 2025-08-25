using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Model.Gear;

/// <summary>
/// 任务定义数据模型，用于 JSON 序列化
/// </summary>
public class GearTaskDefinitionData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("created_time")]
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    [JsonProperty("modified_time")]
    public DateTime ModifiedTime { get; set; } = DateTime.Now;

    [JsonProperty("root_task")]
    public GearTaskData? RootTask { get; set; }
}

/// <summary>
/// 任务数据模型，用于 JSON 序列化
/// </summary>
public class GearTaskData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("task_type")]
    public string TaskType { get; set; } = string.Empty;

    [JsonProperty("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonProperty("is_directory")]
    public bool IsDirectory { get; set; } = false;

    [JsonProperty("parameters")]
    public string Parameters { get; set; } = "{}";

    [JsonProperty("created_time")]
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    [JsonProperty("modified_time")]
    public DateTime ModifiedTime { get; set; } = DateTime.Now;

    [JsonProperty("priority")]
    public int Priority { get; set; } = 0;

    [JsonProperty("tags")]
    public string Tags { get; set; } = string.Empty;

    [JsonProperty("children")]
    public List<GearTaskData> Children { get; set; } = new();
}