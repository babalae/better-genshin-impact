namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>
/// 一个材料家族中某一级材料的库存、需求和单次秘境预计掉落。
/// 等级列表必须按从低到高的顺序传给计算器。
/// </summary>
public sealed record TrainingGuideMaterialLevel(
    string Name,
    int Stock,
    int Required,
    decimal ExpectedDropPerRun = 0);
