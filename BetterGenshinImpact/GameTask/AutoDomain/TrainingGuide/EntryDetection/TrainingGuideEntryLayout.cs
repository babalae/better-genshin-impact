using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>从底部 IV 组建立行号；仅用名称、行距及连续的小幅位移跟踪列表。</summary>
internal sealed class TrainingGuideEntryLayout
{
    internal sealed record Row(TrainingGuideEntry Entry, double Y);
    public TrainingGuideEntry[] Group { get; }
    public double Pitch { get; }
    public double BottomY { get; private set; }
    public double LastShift { get; private set; }
    public int MatchedRows { get; private set; }
    private readonly double _tolerance;

    public TrainingGuideEntryLayout(Row[] rows, int groupSize, double scale)
    {
        rows = rows.OrderBy(r => r.Y).ToArray();
        if (rows.Length < Math.Max(3, groupSize))
            throw new InvalidOperationException("底部入口不足，无法建立难度分组");
        _tolerance = 12 * scale;
        var gaps = rows.Zip(rows.Skip(1), (a, b) => b.Y - a.Y)
            .Where(g => g >= 90 * scale && g <= 130 * scale).OrderBy(g => g).ToArray();
        if (gaps.Length < 2) throw new InvalidOperationException("入口行距未确认，停止建立难度分组");
        Pitch = gaps[gaps.Length / 2];
        Group = rows.TakeLast(groupSize).Select(r => r.Entry).ToArray();
        if (Group.Distinct().Count() != groupSize)
            throw new InvalidOperationException("底部入口组名称重复，停止建立难度分组");
        BottomY = rows[^1].Y;
        if (!TryUpdate(rows)) throw new InvalidOperationException("底部入口排列不符合难度分组，停止规划");
    }

    public int Index(TrainingGuideEntry entry, int difficulty)
    {
        var member = Array.IndexOf(Group, entry);
        if (member < 0 || difficulty is < 1 or > 4)
            throw new InvalidOperationException("目标不属于当前入口分组");
        return (4 - difficulty) * Group.Length + Group.Length - 1 - member;
    }

    public double Y(int index) => BottomY - index * Pitch;

    public bool IsTarget(Row row, int index) =>
        row.Entry == Expected(index) && Math.Abs(row.Y - Y(index)) <= _tolerance;

    private TrainingGuideEntry Expected(int index) => Group[Group.Length - 1 - index % Group.Length];

    public bool TryUpdate(IReadOnlyList<Row> rows, bool afterUpwardScroll = false)
    {
        if (afterUpwardScroll && Group.Length > 1)
            return TryUpdateAfterUpwardScroll(rows);
        // 位移限制在半行以内，防止普通开放时同名的邻级被当成原行。
        // 调用方每次只滚一格，并在下一次滚动前重新截图确认。
        var shifts = new List<double>();
        foreach (var row in rows)
        {
            var index = (int)Math.Round((BottomY - row.Y) / Pitch);
            if (index < 0 || index >= Group.Length * 4 || row.Entry != Expected(index)) return false;
            var shift = row.Y - Y(index);
            if (Math.Abs(shift) >= Pitch * .45) return false;
            shifts.Add(shift);
        }
        if (shifts.Count < 2) return false;
        shifts.Sort();
        var movement = shifts[shifts.Count / 2];
        if (shifts.Any(s => Math.Abs(s - movement) > _tolerance)) return false;
        var indices = rows.Select(r => (int)Math.Round((BottomY + movement - r.Y) / Pitch)).ToArray();
        if (indices.Distinct().Count() != indices.Length) return false;
        BottomY += movement;
        LastShift = movement;
        MatchedRows = rows.Count;
        return true;
    }

    private bool TryUpdateAfterUpwardScroll(IReadOnlyList<Row> rows)
    {
        if (rows.Count < 2) return false;
        // 向上五格会跨行；按三种名称的周期对齐，允许正向移动但不跨完整同名周期。
        // 多个对齐结果或超过范围时拒绝，不能将五格直接当作五行。
        var candidates = new List<double>();
        for (var index = 0; index < Group.Length * 4; index++)
        {
            if (rows[0].Entry != Expected(index)) continue;
            var shift = rows[0].Y - Y(index);
            if (shift < -_tolerance || shift > Pitch * (Group.Length - .5)) continue;
            var indices = rows.Select(r => (int)Math.Round((BottomY + shift - r.Y) / Pitch)).ToArray();
            if (indices.Distinct().Count() != rows.Count) continue;
            if (rows.Where((r, i) => indices[i] < 0 || indices[i] >= Group.Length * 4 ||
                    r.Entry != Expected(indices[i]) ||
                    Math.Abs(r.Y - (Y(indices[i]) + shift)) > _tolerance).Any()) continue;
            var shifts = rows.Select((r, i) => r.Y - Y(indices[i])).OrderBy(s => s).ToArray();
            candidates.Add(shifts[shifts.Length / 2]);
        }
        if (candidates.Count != 1) return false;
        LastShift = candidates[0];
        BottomY += LastShift;
        MatchedRows = rows.Count;
        return true;
    }
}
