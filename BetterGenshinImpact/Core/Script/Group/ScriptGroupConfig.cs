using BetterGenshinImpact.Core.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Script.Group;

[Serializable]
public partial class ScriptGroupConfig : ObservableObject
{
    [ObservableProperty]
    private PathingPartyConfig _pathingConfig = new();

    /// <summary>
    /// Shell 执行配置
    /// </summary>
    [ObservableProperty]
    private ShellConfig _shellConfig = new();
}
