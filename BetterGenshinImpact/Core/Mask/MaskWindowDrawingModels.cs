using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 绘制分组的类型，决定分组在遮罩窗口上的显示规则
/// </summary>
public enum MaskWindowDrawingKind
{
    /// <summary>
    /// 识别结果（调试可视化）：受「在遮罩上显示识别结果」开关和统一样式控制
    /// </summary>
    Recognition,

    /// <summary>
    /// 功能提示（技能 CD、地脉 OCR 区域等）：遮罩可见就显示，使用图形自带样式
    /// </summary>
    Feature,
}

/// <summary>
/// 绘制分组。一组图形按 Name 整体替换，Kind 决定显示规则
/// </summary>
public readonly record struct MaskWindowDrawingGroup(string Name, MaskWindowDrawingKind Kind = MaskWindowDrawingKind.Recognition)
{
    public static implicit operator MaskWindowDrawingGroup(string name) => new(name);
}

/// <summary>
/// 遮罩窗口上的图形。坐标统一使用游戏捕获区域的物理像素（与截图 Mat 相同）
/// </summary>
public abstract record MaskWindowDrawingShape;

public sealed record MaskWindowDrawingRect(Rect Bounds, MaskWindowDrawingStroke? Stroke = null) : MaskWindowDrawingShape;

public sealed record MaskWindowDrawingLine(Point From, Point To, MaskWindowDrawingStroke? Stroke = null) : MaskWindowDrawingShape;

public sealed record MaskWindowDrawingText(string Text, Point Origin, MaskWindowDrawingTextStyle? Style = null) : MaskWindowDrawingShape;

/// <summary>
/// 线条样式。Thickness 为 DIP
/// </summary>
public readonly record struct MaskWindowDrawingStroke(Color Color, double Thickness = 2)
{
    /// <summary>
    /// 兼容业务代码里现有的 System.Drawing.Pen（如 Pens.Lime、识别对象的 DrawOnWindowPen）
    /// </summary>
    public static MaskWindowDrawingStroke? FromPen(System.Drawing.Pen? pen)
    {
        if (pen == null)
        {
            return null;
        }

        var c = pen.Color;
        return new MaskWindowDrawingStroke(Color.FromArgb(c.A, c.R, c.G, c.B), pen.Width);
    }
}

/// <summary>
/// 文字样式。为 null 的字段使用 MaskWindowConfig 中的识别结果样式
/// </summary>
/// <param name="Foreground">文字颜色</param>
/// <param name="FontSize">字号，单位为捕获像素</param>
/// <param name="Background">不为空时在文字下方绘制圆角底色</param>
/// <param name="Numeric">是否使用数字字体（Fgi）</param>
public sealed record MaskWindowDrawingTextStyle(
    Color? Foreground = null,
    double? FontSize = null,
    Color? Background = null,
    bool Numeric = false);

/// <summary>
/// 绘制快照中的一个分组
/// </summary>
public sealed record MaskWindowDrawingEntry(MaskWindowDrawingGroup Group, IReadOnlyList<MaskWindowDrawingShape> Shapes);

/// <summary>
/// 绘制内容的不可变快照，UI 线程直接读取
/// </summary>
public sealed record MaskWindowDrawingSnapshot(long Version, IReadOnlyList<MaskWindowDrawingEntry> Entries)
{
    public static MaskWindowDrawingSnapshot Empty { get; } = new(0, Array.Empty<MaskWindowDrawingEntry>());

    public bool IsEmpty => Entries.Count == 0;
}
