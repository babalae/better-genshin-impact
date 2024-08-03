using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Script.Group;

public class ScriptGroup : ObservableObject
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<ScriptGroupProject> Projects { get; set; } = [];
}
