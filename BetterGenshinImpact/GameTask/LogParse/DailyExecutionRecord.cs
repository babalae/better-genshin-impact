using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.LogParse;

public class DailyExecutionRecord
{
    [JsonProperty("name")]
    public string Name { get; set; }=string.Empty;
    [JsonProperty("execution_records")]
    public List<ExecutionRecord> ExecutionRecords { get; set; } = new();
}