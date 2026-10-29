// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>SceneInkTotals：原神自动配音的业务类型。</summary>
internal readonly record struct SceneInkTotals(int Count, long Pixels, long TwiceHorizontalMoment)
{
    public static SceneInkTotals operator +(SceneInkTotals a, SceneInkTotals b) =>
        new(a.Count + b.Count, a.Pixels + b.Pixels, a.TwiceHorizontalMoment + b.TwiceHorizontalMoment);
    public static SceneInkTotals operator -(SceneInkTotals a, SceneInkTotals b) =>
        new(a.Count - b.Count, a.Pixels - b.Pixels, a.TwiceHorizontalMoment - b.TwiceHorizontalMoment);
}

// Intervals are half-open: [Top, Bottom). A component intersects a query exactly when
// it starts before Bottom and has not ended at Top. Prefix totals make each query O(1).
internal sealed class SceneInkIntervalIndex
{
    /// <summary>墨迹区间起点索引。</summary>
    private readonly SceneInkTotals[] _starts;
    /// <summary>墨迹区间终点索引。</summary>
    private readonly SceneInkTotals[] _ends;

    /// <summary>建立 SceneInkIntervalIndex 的业务依赖与初始状态。</summary>
    public SceneInkIntervalIndex(IReadOnlyList<SceneInkComponent> components, int height, CancellationToken token)
    {
        _starts = new SceneInkTotals[height + 1];
        _ends = new SceneInkTotals[height + 1];
        foreach (var component in components)
        {
            token.ThrowIfCancellationRequested();
            var totals = new SceneInkTotals(1, component.Pixels,
                ((long)component.Left + component.Right) * component.Pixels);
            _starts[component.Top + 1] += totals;
            _ends[component.Bottom] += totals;
        }
        for (var row = 1; row <= height; row++)
        {
            token.ThrowIfCancellationRequested();
            _starts[row] += _starts[row - 1];
            _ends[row] += _ends[row - 1];
        }
    }

    /// <summary>Overlapping：执行SceneInkIntervalIndex中的对应校验或计算步骤。</summary>
    public SceneInkTotals Overlapping(SceneInkComponent component) => _starts[component.Bottom] - _ends[component.Top];
}
