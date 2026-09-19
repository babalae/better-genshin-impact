using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

internal enum PotScrollObservation { Unknown, Moved, Stable }

internal static class SereniteaPotInventorySearch
{
    internal static PotScrollObservation Classify(bool validGrids, double response,
        double shiftX, double shiftY, double meanDifference)
    {
        if (!validGrids || !double.IsFinite(response) || !double.IsFinite(shiftX) ||
            !double.IsFinite(shiftY) || !double.IsFinite(meanDifference) || response < 0.5)
            return PotScrollObservation.Unknown;
        if (response >= 0.95 && Math.Abs(shiftX) <= 1 && Math.Abs(shiftY) <= 1 && meanDifference <= 2)
            return PotScrollObservation.Stable;
        return Math.Abs(shiftX) > 1 || Math.Abs(shiftY) > 1
            ? PotScrollObservation.Moved : PotScrollObservation.Unknown;
    }

    // 先查当前页；未找到才向上逐步扫描，再从确认的顶端向下扫描。
    // 无效帧和低相关度不代表边界，必须连续三次有效且稳定才结束一个方向。
    internal static async Task<bool> FindAsync(Func<bool> findAndClick,
        Func<int, CancellationToken, Task<PotScrollObservation>> scroll,
        CancellationToken ct, int maxScrollSteps = 80)
    {
        ct.ThrowIfCancellationRequested();
        if (findAndClick()) { ct.ThrowIfCancellationRequested(); return true; }
        foreach (var direction in new[] { 1, -1 })
        {
            var stable = 0;
            for (var step = 0; step < maxScrollSteps; step++)
            {
                ct.ThrowIfCancellationRequested();
                var observation = await scroll(direction, ct);
                ct.ThrowIfCancellationRequested();
                if (findAndClick()) { ct.ThrowIfCancellationRequested(); return true; }
                stable = observation == PotScrollObservation.Stable ? stable + 1 : 0;
                if (stable >= 3) break;
            }
            if (stable < 3) return false;
        }
        return false;
    }
}
