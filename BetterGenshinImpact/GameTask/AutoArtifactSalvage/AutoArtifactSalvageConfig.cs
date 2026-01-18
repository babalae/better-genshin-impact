using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage;

[Serializable]
public partial class AutoArtifactSalvageConfig : ObservableObject
{
    // JavaScript
    [ObservableProperty]
    private string _javaScript = @"var hasATK = Array.from(ArtifactStat.MinorAffixes).some(affix => affix.Type == 'ATK');
var hasDEF = Array.from(ArtifactStat.MinorAffixes).some(affix => affix.Type == 'DEF');
var hasHP = Array.from(ArtifactStat.MinorAffixes).some(affix => affix.Type == 'HP');
Output = (hasATK && hasDEF) || (hasHP && hasDEF);";

    // JavaScript
    [ObservableProperty]
    private string _artifactSetFilter = "";

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

    // 单次识别失败策略
    [ObservableProperty]
    private RecognitionFailurePolicy _recognitionFailurePolicy = RecognitionFailurePolicy.Skip;
}