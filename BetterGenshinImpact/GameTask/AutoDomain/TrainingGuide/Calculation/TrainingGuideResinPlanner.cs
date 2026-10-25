using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public sealed record TrainingGuideResinAllowance(string Name, int ResinPerClaim, int Count);
public sealed record TrainingGuideResinPlan(int Resin, int UncoveredResin, IReadOnlyList<string> Uses);

public static class TrainingGuideResinPlanner
{
    /// <summary>按配置优先级分配每个家族的需求；一轮的溢出不能转移给其他家族。</summary>
    public static TrainingGuideResinPlan Allocate(IReadOnlyList<int> familyRequirements,
        IReadOnlyList<TrainingGuideResinAllowance> allowances)
    {
        if (familyRequirements.Any(n => n < 0 || n % 20 != 0) ||
            allowances.Any(a => a.Count < 0 || a.ResinPerClaim is not (20 or 40 or 60)))
            throw new ArgumentException("树脂需求和额度无效");
        var remaining = allowances.Select(a => a.Count).ToArray();
        var used = new int[allowances.Count];
        var uncovered = 0;
        var total = 0;
        foreach (var requirement in familyRequirements)
        {
            var shortage = requirement;
            for (var i = 0; i < allowances.Count && shortage > 0; i++)
            {
                var count = (int)Math.Min(remaining[i], ((long)shortage + allowances[i].ResinPerClaim - 1) / allowances[i].ResinPerClaim);
                var resin = checked(count * allowances[i].ResinPerClaim);
                remaining[i] -= count;
                used[i] += count;
                total = checked(total + resin);
                shortage = Math.Max(0, shortage - resin);
            }
            uncovered = checked(uncovered + shortage);
        }
        return new(total, uncovered, allowances.Select((a, i) => (a, count: used[i])).Where(x => x.count > 0)
            .Select(x => $"{x.a.Name} × {x.count}（{x.a.ResinPerClaim}体/次）").ToArray());
    }
}
