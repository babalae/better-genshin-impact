using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>各难度每20体的掉落期望，按材料从低到高排列；不掉落的等级为零。</summary>
public static class TrainingGuideDropExpectations
{
    public static decimal[] Per20(bool isWeapon, int difficulty = 4) => (isWeapon, difficulty) switch
    {
        (true, 1) => [4.70m, 0m, 0m, 0m],
        (true, 2) => [2.70m, 2m, 0m, 0m],
        (true, 3) => [2.30m, 2.76m, .24m, 0m],
        (true, 4) => [2.20m, 2.40m, .62m, .0775m],
        (false, 1) => [3.20m, 0m, 0m],
        (false, 2) => [2.50m, 1m, 0m],
        (false, 3) => [1.80m, 2m, 0m],
        (false, 4) => [2.20m, 1.98m, .22m],
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };

    public static int ReadDifficulty(string title) =>
        Regex.Match(title.Normalize(NormalizationForm.FormKC).Trim(), @"[IVX]+$", RegexOptions.IgnoreCase)
            .Value.ToUpperInvariant() switch
        {
            "I" => 1, "II" => 2, "III" => 3, "IV" => 4, _ => 0
        };
}
