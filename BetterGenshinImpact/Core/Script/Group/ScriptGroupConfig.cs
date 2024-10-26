using CommunityToolkit.Mvvm.ComponentModel;
using System;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Script.Group;

[Serializable]
public partial class ScriptGroupConfig : ObservableObject
{
    [ObservableProperty]
    private PathingConfig _pathingConfig = new();
}
