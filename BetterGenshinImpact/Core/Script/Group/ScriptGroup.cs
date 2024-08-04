using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Script.Group;

public class ScriptGroup : ObservableObject
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    [JsonIgnore]
    public List<ScriptGroupProject> Projects { get; set; } = [];
}
