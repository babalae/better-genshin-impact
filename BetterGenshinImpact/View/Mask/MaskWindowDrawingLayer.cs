using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Core.Mask;

namespace BetterGenshinImpact.View.Mask;

/// <summary>
/// 遮罩窗口的绘制层：渲染 <see cref="IMaskWindowDrawingBoard"/> 的快照（识别框、线、文字、技能 CD）。
/// 只在绑定的快照或样式变化时重绘这一层，不会触发整个窗口重绘。
/// </summary>
public sealed class MaskWindowDrawingLayer : FrameworkElement
{
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(
            nameof(Snapshot),
            typeof(MaskWindowDrawingSnapshot),
            typeof(MaskWindowDrawingLayer),
            new FrameworkPropertyMetadata(MaskWindowDrawingSnapshot.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowRecognitionProperty =
        DependencyProperty.Register(
            nameof(ShowRecognition),
            typeof(bool),
            typeof(MaskWindowDrawingLayer),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DrawingStyleProperty =
        DependencyProperty.Register(
            nameof(DrawingStyle),
            typeof(MaskWindowDrawingStyle),
            typeof(MaskWindowDrawingLayer),
            new FrameworkPropertyMetadata(MaskWindowDrawingStyle.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// 技能 CD 样式的内边距与圆角相对字号的比例（原实现为 26 号字配 6/2/5 的边距与圆角）
    /// </summary>
    private const double BadgePaddingXRatio = 6d / 26d;
    private const double BadgePaddingYRatio = 2d / 26d;
    private const double BadgeRadiusRatio = 5d / 26d;

    private const int MaxCacheSize = 256;

    private static readonly CultureInfo TextCulture = CultureInfo.GetCultureInfo("zh-cn");
    private static readonly Lazy<Typeface> TextTypeface = new(CreateTextTypeface);
    private static readonly Lazy<Typeface> NumericTypeface = new(CreateNumericTypeface);

    private readonly Dictionary<(Color Color, double Thickness), Pen> _penCache = new();
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    public MaskWindowDrawingLayer()
    {
        IsHitTestVisible = false;
    }

    public MaskWindowDrawingSnapshot? Snapshot
    {
        get => (MaskWindowDrawingSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    /// <summary>
    /// 对应配置「在遮罩上显示识别结果」，只影响 Recognition 分组
    /// </summary>
    public bool ShowRecognition
    {
        get => (bool)GetValue(ShowRecognitionProperty);
        set => SetValue(ShowRecognitionProperty, value);
    }

    public MaskWindowDrawingStyle? DrawingStyle
    {
        get => (MaskWindowDrawingStyle?)GetValue(DrawingStyleProperty);
        set => SetValue(DrawingStyleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var snapshot = Snapshot;
        if (snapshot == null || snapshot.IsEmpty)
        {
            return;
        }

        var style = DrawingStyle ?? MaskWindowDrawingStyle.Default;
        var showRecognition = ShowRecognition;
        var metrics = MaskWindowLayerMetrics.From(this);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var entry in snapshot.Entries)
        {
            var isRecognition = entry.Group.Kind == MaskWindowDrawingKind.Recognition;
            if (isRecognition && !showRecognition)
            {
                continue;
            }

            foreach (var shape in entry.Shapes)
            {
                switch (shape)
                {
                    case MaskWindowDrawingRect rect:
                        DrawRect(dc, rect, ResolveStroke(rect.Stroke, style.RectStroke, isRecognition, style), metrics);
                        break;
                    case MaskWindowDrawingLine line:
                        DrawLine(dc, line, ResolveStroke(line.Stroke, style.LineStroke, isRecognition, style), metrics);
                        break;
                    case MaskWindowDrawingText text:
                        DrawText(dc, text, style, metrics, pixelsPerDip);
                        break;
                }
            }
        }
    }

    private static MaskWindowDrawingStroke ResolveStroke(MaskWindowDrawingStroke? own, MaskWindowDrawingStroke unified,
        bool isRecognition, MaskWindowDrawingStyle style)
    {
        if (isRecognition && style.OverrideRecognitionStroke)
        {
            return unified;
        }

        return own ?? MaskWindowDrawingStyle.DefaultStroke;
    }

    private void DrawRect(DrawingContext dc, MaskWindowDrawingRect rect, MaskWindowDrawingStroke stroke, MaskWindowLayerMetrics metrics)
    {
        var bounds = rect.Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        var unitRect = new Rect(
            bounds.X * metrics.PixelToUnitX,
            bounds.Y * metrics.PixelToUnitY,
            bounds.Width * metrics.PixelToUnitX,
            bounds.Height * metrics.PixelToUnitY);
        dc.DrawRectangle(null, GetPen(stroke, metrics), unitRect);
    }

    private void DrawLine(DrawingContext dc, MaskWindowDrawingLine line, MaskWindowDrawingStroke stroke, MaskWindowLayerMetrics metrics)
    {
        var from = new Point(line.From.X * metrics.PixelToUnitX, line.From.Y * metrics.PixelToUnitY);
        var to = new Point(line.To.X * metrics.PixelToUnitX, line.To.Y * metrics.PixelToUnitY);
        dc.DrawLine(GetPen(stroke, metrics), from, to);
    }

    private void DrawText(DrawingContext dc, MaskWindowDrawingText text, MaskWindowDrawingStyle style,
        MaskWindowLayerMetrics metrics, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text.Text))
        {
            return;
        }

        var textStyle = text.Style;
        var fontSizePx = Math.Max(1, textStyle?.FontSize ?? style.TextFontSize);
        var fontSize = fontSizePx * metrics.PixelToUnitY;
        var typeface = textStyle?.Numeric == true ? NumericTypeface.Value : TextTypeface.Value;
        var foreground = GetBrush(textStyle?.Foreground ?? style.TextColor);
        var origin = new Point(text.Origin.X * metrics.PixelToUnitX, text.Origin.Y * metrics.PixelToUnitY);

        var formattedText = new FormattedText(
            text.Text,
            TextCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground,
            pixelsPerDip);

        if (textStyle?.Background is { } background)
        {
            var paddingX = fontSize * BadgePaddingXRatio;
            var paddingY = fontSize * BadgePaddingYRatio;
            var radius = fontSize * BadgeRadiusRatio;
            var badge = new Rect(
                origin.X - paddingX,
                origin.Y - paddingY,
                formattedText.Width + paddingX * 2,
                formattedText.Height + paddingY * 2);
            dc.DrawRoundedRectangle(GetBrush(background), null, badge, radius, radius);
        }

        dc.DrawText(formattedText, origin);
    }

    private Pen GetPen(MaskWindowDrawingStroke stroke, MaskWindowLayerMetrics metrics)
    {
        var thickness = Math.Max(0, stroke.Thickness) * metrics.DipToUnit;
        var key = (stroke.Color, thickness);
        if (_penCache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        if (_penCache.Count >= MaxCacheSize)
        {
            _penCache.Clear();
        }

        pen = new Pen(GetBrush(stroke.Color), thickness);
        pen.Freeze();
        _penCache[key] = pen;
        return pen;
    }

    private SolidColorBrush GetBrush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var brush))
        {
            return brush;
        }

        if (_brushCache.Count >= MaxCacheSize)
        {
            _brushCache.Clear();
        }

        brush = new SolidColorBrush(color);
        brush.Freeze();
        _brushCache[color] = brush;
        return brush;
    }

    private static Typeface CreateTextTypeface()
    {
        if (Application.Current?.TryFindResource("TextThemeFontFamily") is FontFamily fontFamily)
        {
            return fontFamily.GetTypefaces().First();
        }

        return new FontFamily("Microsoft Yahei UI").GetTypefaces().First();
    }

    private static Typeface CreateNumericTypeface()
    {
        try
        {
            var fgi = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/Fgi-Regular.ttf#Fgi-Regular")
                .GetTypefaces()
                .First();
            return new Typeface(fgi.FontFamily, fgi.Style, FontWeights.Medium, fgi.Stretch);
        }
        catch
        {
            return TextTypeface.Value;
        }
    }
}
