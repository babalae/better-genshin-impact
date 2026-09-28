namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>最高难度每20体掉落期望。返回独立数组，允许按本次实际掉落修正。</summary>
public static class TrainingGuideDropExpectations
{
    public static decimal[] Per20(bool isWeapon) => isWeapon
        ? [2.20m, 2.40m, .62m, .0775m]
        : [2.20m, 1.98m, .22m];
}
