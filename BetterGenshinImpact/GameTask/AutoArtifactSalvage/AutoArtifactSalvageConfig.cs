using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage;

[Serializable]
public partial class AutoArtifactSalvageConfig : ObservableObject
{
    // 正则表达式
    [ObservableProperty]
    private string _regularExpression = @"(?=[\S\s]*攻击力\+[\d]*\n)(?=[\S\s]*防御力\+[\d]*\n)";

    // 快速分解圣遗物的最大星级
    // 1~4
    [ObservableProperty]
    private string _maxArtifactStar = "4";
}