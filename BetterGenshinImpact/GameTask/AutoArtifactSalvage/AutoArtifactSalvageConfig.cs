using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage;

[Serializable]
public partial class AutoArtifactSalvageConfig : ObservableObject
{
    // JavaScript
    [ObservableProperty]
    private string _javaScript = 
        @"(async function (artifact) {
            var hasATK = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'ATK');
            var hasDEF = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'DEF');
            Output = hasATK && hasDEF;
        })(ArtifactStat);";

    // 正则表达式
    [Obsolete]
    [ObservableProperty]
    private string _regularExpression = @"(?=[\S\s]*攻击力\+[\d]*\n)(?=[\S\s]*防御力\+[\d]*\n)";

    // 快速分解圣遗物的最大星级
    // 1~4
    [ObservableProperty]
    private string _maxArtifactStar = "4";

    // 最多检查多少个圣遗物
    [ObservableProperty]
    private int _maxNumToCheck = 100;
}