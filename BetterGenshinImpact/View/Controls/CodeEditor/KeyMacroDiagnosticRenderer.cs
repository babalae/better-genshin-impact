using BetterGenshinImpact.GameTask.KeyMacro.Script;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls.CodeEditor;

/// <summary>
/// 在脚本编辑器中标出诊断：错误行加浅红背景，出错或提醒的指令下加波浪线
/// </summary>
public sealed class KeyMacroDiagnosticRenderer : IBackgroundRenderer
{
    /// <summary>
    /// 错误行背景
    /// </summary>
    private static readonly Brush ErrorLineBrush = CreateFrozenBrush(Color.FromArgb(0x30, 0xE8, 0x11, 0x23));

    /// <summary>
    /// 错误波浪线
    /// </summary>
    private static readonly Pen ErrorPen = CreateFrozenPen(Color.FromRgb(0xE8, 0x11, 0x23));

    /// <summary>
    /// 提醒波浪线
    /// </summary>
    private static readonly Pen WarningPen = CreateFrozenPen(Color.FromRgb(0xD8, 0x8A, 0x00));

    /// <summary>
    /// 要标出的诊断
    /// </summary>
    public IReadOnlyList<KeyMacroDiagnostic> Diagnostics { get; set; } = [];

    /// <inheritdoc />
    public KnownLayer Layer => KnownLayer.Background;

    /// <inheritdoc />
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        var document = textView.Document;
        if (document == null || !textView.VisualLinesValid || Diagnostics.Count == 0)
        {
            return;
        }

        foreach (var diagnostic in Diagnostics)
        {
            // 整个脚本的提醒没有位置；行号可能因尚未重新解析而超出范围
            if (diagnostic.Line < 1 || diagnostic.Line > document.LineCount)
            {
                continue;
            }

            var line = document.GetLineByNumber(diagnostic.Line);
            if (diagnostic.IsError)
            {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
                {
                    drawingContext.DrawRectangle(ErrorLineBrush, null, new Rect(0, rect.Top, textView.ActualWidth, rect.Height));
                }
            }

            var start = line.Offset + Math.Min(diagnostic.Column, line.Length);
            var length = diagnostic.Length > 0 ? Math.Min(diagnostic.Length, line.EndOffset - start) : line.EndOffset - start;
            if (length <= 0)
            {
                continue;
            }

            var segment = new TextSegment { StartOffset = start, Length = length };
            var pen = diagnostic.IsError ? ErrorPen : WarningPen;
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                drawingContext.DrawGeometry(null, pen, CreateWave(rect));
            }
        }
    }

    /// <summary>
    /// 沿矩形底边生成波浪线
    /// </summary>
    private static StreamGeometry CreateWave(Rect rect)
    {
        const double step = 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var y = rect.Bottom - 1;
            context.BeginFigure(new Point(rect.Left, y), false, false);
            var up = true;
            for (var x = rect.Left + step; x <= rect.Right; x += step)
            {
                context.LineTo(new Point(x, up ? y - step : y), true, false);
                up = !up;
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// 创建冻结的画刷
    /// </summary>
    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 创建冻结的画笔
    /// </summary>
    private static Pen CreateFrozenPen(Color color)
    {
        var pen = new Pen(CreateFrozenBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
