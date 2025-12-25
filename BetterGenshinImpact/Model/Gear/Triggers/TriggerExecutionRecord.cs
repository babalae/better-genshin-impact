using System;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Model.Gear.Triggers;

public enum TriggerExecutionStatus
{
    Running,
    Success,
    Failed,
    Skipped
}

public class TriggerExecutionRecord
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty("trigger_name")]
    public string TriggerName { get; set; } = string.Empty;

    [JsonProperty("trigger_id")]
    public string TriggerId { get; set; } = string.Empty;

    [JsonProperty("task_name")]
    public string TaskName { get; set; } = string.Empty;

    [JsonProperty("start_time")]
    public DateTime StartTime { get; set; } = DateTime.Now;

    [JsonProperty("end_time")]
    public DateTime? EndTime { get; set; }

    [JsonProperty("status")]
    public TriggerExecutionStatus Status { get; set; } = TriggerExecutionStatus.Running;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("log_details")]
    public string LogDetails { get; set; } = string.Empty;
    
    [JsonProperty("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    [JsonIgnore]
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.Now - StartTime;
}
