using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Script.Group;

public class ScriptGroupProject : ObservableObject
{
    public int Order { get; set; }

    public string Id { get; set; }

    public ScriptProject Project { get; set; }

    public ScriptGroupProject(int order, ScriptProject project)
    {
        Order = order;
        Id = project.Manifest.Id;
        Project = project;
    }
}
