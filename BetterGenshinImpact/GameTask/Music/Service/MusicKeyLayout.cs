using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Music.Model;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 一个琴键在「覆盖层画布坐标系」（0,0 = 游戏画面左上角，单位 = 游戏画面像素）里的位置。
/// X / Y 是引导圈的圆心，Size 是引导圈直径。
/// </summary>
public readonly record struct GuidedKeyPosition(double X, double Y, double Size, int Row, int Column);

/// <summary>
/// 计算引导圈画在哪里：**直接画在原神演奏界面那 21 个琴键的实际位置上**。
///
/// 用户要求（推翻上一版「在画面偏上画一块独立提示板」的做法）：不要另一个窗口/提示板，
/// 就要在游戏自己的琴键上方套提示圈。所以这里不再自己排布键盘，而是把琴键在画面上的位置
/// 照抄一遍——参数见 <see cref="MusicGuidedKeyLayoutConfig"/>（1080P 基准）。
///
/// 换算规则（配置里的数字都是「1080P 基准」像素）：
/// - 水平：x = BaseX / 1920 × 画面宽度 + 列序号 × (ColumnSpacing / 1920 × 画面宽度)；
/// - 纵向：y = BaseY / 1080 × 画面高度 + 行序号 × (RowSpacing / 1080 × 画面高度)；
/// - 圈的大小：按 min(横向比例, 纵向比例) 缩放，保证引导圈是正圆、和琴键一样大；
/// - 于是 1080P / 2K / 4K 都自动等比适配，16:10、21:9 这种非 16:9 的窗口也不会把琴键拉偏
///   （16:9 下这套公式与「整体等比放大」完全等价）；
/// - 启动时还会先用 <see cref="MusicKeyAutoDetector"/> 从游戏画面里把琴键的实际位置量出来、
///   写回这些参数，所以就算参数本身不准也会被现场纠正。
/// - 键位排布固定为原神演奏界面的三排（与 <see cref="InstrumentProfile"/> 无关）：
///       Q W E R T Y U
///       A S D F G H J
///       Z X C V B N M
///   三排是上下对齐的（实测原神就是这样排的，BaseX 三排相同），需要交错时再用
///   <see cref="MusicGuidedKeyLayoutConfig.RowIndent"/> 逐排右移。当前乐器没有的键不会画出来
///   （列序号仍按上面的固定排布取，不会被挤位）。
/// </summary>
public static class MusicKeyLayout
{
    /// <summary>布局参数的基准分辨率（配置里的数字都是这个尺寸下的像素）</summary>
    public const double ReferenceWidth = 1920d;

    /// <inheritdoc cref="ReferenceWidth" />
    public const double ReferenceHeight = 1080d;

    /// <summary>标准 21 键：从上到下 = 音高从高到低，列序号 = 琴键在那一排里的次序</summary>
    private static readonly char[][] CanonicalRows =
    [
        ['Q', 'W', 'E', 'R', 'T', 'Y', 'U'],
        ['A', 'S', 'D', 'F', 'G', 'H', 'J'],
        ['Z', 'X', 'C', 'V', 'B', 'N', 'M']
    ];

    public static IReadOnlyDictionary<char, GuidedKeyPosition> Resolve(
        InstrumentProfile profile,
        double canvasWidth,
        double canvasHeight)
    {
        return Resolve(profile, new MusicGuidedKeyLayoutConfig(), canvasWidth, canvasHeight);
    }

    /// <summary>
    /// 解析每个键的位置。当前乐器没有的键不会出现在结果里（上层会把它们列到提示面板里，不静默丢失）。
    /// </summary>
    public static IReadOnlyDictionary<char, GuidedKeyPosition> Resolve(
        InstrumentProfile profile,
        MusicGuidedKeyLayoutConfig layout,
        double canvasWidth,
        double canvasHeight)
    {
        var positions = new Dictionary<char, GuidedKeyPosition>();
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return positions;
        }

        var keys = CollectKeys(profile);
        if (keys.Count == 0)
        {
            return positions;
        }

        // 配置里的数字是 1080P 基准像素：横向按画面宽度、纵向按画面高度分别取比例，
        // 这样 1080P / 2K / 4K 都能自动适配（16:9 时与整体等比放大等价）。
        var scaleX = canvasWidth / ReferenceWidth;
        var scaleY = canvasHeight / ReferenceHeight;
        var ringScale = Math.Min(scaleX, scaleY);
        var columnSpacing = Math.Max(4, layout.ColumnSpacing) * scaleX;
        var rowSpacing = Math.Max(4, layout.RowSpacing) * scaleY;
        var rowIndent = layout.RowIndent * scaleX;
        var size = Math.Max(8, layout.RingRadius) * 2 * ringScale;

        for (var rowIndex = 0; rowIndex < CanonicalRows.Length; rowIndex++)
        {
            var row = CanonicalRows[rowIndex];
            for (var column = 0; column < row.Length; column++)
            {
                var key = row[column];
                if (!keys.Contains(key))
                {
                    continue;
                }

                var x = layout.BaseX * scaleX + column * columnSpacing + rowIndex * rowIndent;
                var y = layout.BaseY * scaleY + rowIndex * rowSpacing;
                ApplyOffset(layout, key, scaleX, scaleY, ref x, ref y);
                positions[key] = new GuidedKeyPosition(x, y, size, rowIndex, column);
            }
        }

        // 不在标准 21 键里的键（自定义乐器档案）：没有对应的真实琴键，不画圈。
        return positions;
    }

    /// <summary>
    /// 乐器档案里实际存在的按键；档案为空时退回标准 21 键，保证引导一定画得出来。
    /// </summary>
    private static HashSet<char> CollectKeys(InstrumentProfile profile)
    {
        var keys = new HashSet<char>();
        foreach (var mapping in profile.Mappings)
        {
            if (!string.IsNullOrEmpty(mapping.Key))
            {
                keys.Add(char.ToUpperInvariant(mapping.Key[0]));
            }
        }

        if (keys.Count == 0)
        {
            foreach (var key in CanonicalRows.SelectMany(x => x))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static void ApplyOffset(
        MusicGuidedKeyLayoutConfig layout,
        char key,
        double scaleX,
        double scaleY,
        ref double x,
        ref double y)
    {
        if (layout.KeyOffsets.Count == 0)
        {
            return;
        }

        if (layout.KeyOffsets.TryGetValue(key.ToString(), out var offset))
        {
            x += offset.X * scaleX;
            y += offset.Y * scaleY;
        }
    }
}
