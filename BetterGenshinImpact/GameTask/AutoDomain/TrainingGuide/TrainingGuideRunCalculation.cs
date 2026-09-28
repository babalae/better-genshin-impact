using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public sealed record TrainingGuideLevelCalculation(
    string Name,
    int Available,
    int Required,
    int Shortage,
    int Crafted,
    int ReservedBonus);

public sealed record TrainingGuideRunCalculation(
    int Runs,
    bool IsSatisfied,
    IReadOnlyList<TrainingGuideLevelCalculation> Levels);
