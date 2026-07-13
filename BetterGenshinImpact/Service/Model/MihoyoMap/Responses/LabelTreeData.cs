using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class LabelTreeData
    {
        [JsonProperty("tree")] public List<LabelNode> Tree { get; set; } = new();
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
