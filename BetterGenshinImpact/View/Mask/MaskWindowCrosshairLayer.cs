using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Ui;

namespace BetterGenshinImpact.View.Mask;

/// <summary>
/// 遮罩窗口的准星层。每个配置项绑定一个 AffectsRender 依赖属性，只在准星配置变化时重绘。
/// 是否显示由外部通过 Visibility 控制（关闭点击穿透时必须隐藏，否则准星像素会拦截点击）。
/// </summary>
public sealed class MaskWindowCrosshairLayer : FrameworkElement
{
    public static readonly DependencyProperty CrosshairTypeProperty =
        DependencyProperty.Register(nameof(CrosshairType), typeof(CrosshairType), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(CrosshairType.Crosshair, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CrosshairColorProperty =
        DependencyProperty.Register(nameof(CrosshairColor), typeof(string), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineWidthProperty =
        DependencyProperty.Register(nameof(LineWidth), typeof(double), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(4d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CrosshairSizeProperty =
        DependencyProperty.Register(nameof(CrosshairSize), typeof(double), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(30d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(nameof(Gap), typeof(double), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImagePathProperty =
        DependencyProperty.Register(nameof(ImagePath), typeof(string), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnImagePathChanged));

    public static readonly DependencyProperty ScaleModeProperty =
        DependencyProperty.Register(nameof(ScaleMode), typeof(CrosshairScaleMode), typeof(MaskWindowCrosshairLayer),
            new FrameworkPropertyMetadata(CrosshairScaleMode.Original, FrameworkPropertyMetadataOptions.AffectsRender));

    private BitmapSource? _image;

    public MaskWindowCrosshairLayer()
    {
        IsHitTestVisible = false;
    }

    public CrosshairType CrosshairType
    {
        get => (CrosshairType)GetValue(CrosshairTypeProperty);
        set => SetValue(CrosshairTypeProperty, value);
    }

    /// <summary>
    /// #RRGGBB 或 #RRGGBBAA
    /// </summary>
    public string? CrosshairColor
    {
        get => (string?)GetValue(CrosshairColorProperty);
        set => SetValue(CrosshairColorProperty, value);
    }

    public double LineWidth
    {
        get => (double)GetValue(LineWidthProperty);
        set => SetValue(LineWidthProperty, value);
    }

    public double CrosshairSize
    {
        get => (double)GetValue(CrosshairSizeProperty);
        set => SetValue(CrosshairSizeProperty, value);
    }

    /// <summary>
    /// 中心点与十字线的间隔（仅 DotCrosshair 类型）
    /// </summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public CrosshairScaleMode ScaleMode
    {
        get => (CrosshairScaleMode)GetValue(ScaleModeProperty);
        set => SetValue(ScaleModeProperty, value);
    }

    private static void OnImagePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MaskWindowCrosshairLayer)d)._image = LoadImage(e.NewValue as string);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // 准星尺寸等配置值是窗口 DIP，本层可能处于 DPI 适配的 LayoutTransform 之下，需要换算回本层单位
        var unit = MaskWindowLayerMetrics.From(this).DipToUnit;
        var centerX = ActualWidth / 2;
        var centerY = ActualHeight / 2;
        var color = OverlayStyleHelper.ParseRgbaHexColor(CrosshairColor) ?? Colors.White;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var lineWidth = LineWidth * unit;
        var dotRadius = Math.Max(lineWidth / 2.0, 2.0 * unit);

        switch (CrosshairType)
        {
            case CrosshairType.Crosshair:
            {
                var half = CrosshairSize * unit / 2.0;
                var pen = new Pen(brush, lineWidth);
                dc.DrawLine(pen, new Point(centerX - half, centerY), new Point(centerX + half, centerY));
                dc.DrawLine(pen, new Point(centerX, centerY - half), new Point(centerX, centerY + half));
                break;
            }
            case CrosshairType.Diagonal:
            {
                var half = CrosshairSize * unit / 2.0;
                var pen = new Pen(brush, lineWidth);
                var d = half * 0.7071; // cos(45°) = sin(45°) ≈ 0.7071
                dc.DrawLine(pen, new Point(centerX, centerY), new Point(centerX + d, centerY + d));
                dc.DrawLine(pen, new Point(centerX, centerY), new Point(centerX - d, centerY + d));
                // 填补两条斜线端帽交汇处的空隙
                dc.DrawEllipse(brush, null, new Point(centerX, centerY), dotRadius, dotRadius);
                break;
            }
            case CrosshairType.Dot:
            {
                var pen = new Pen(brush, lineWidth);
                var radius = CrosshairSize * unit / 2.0;
                dc.DrawEllipse(null, pen, new Point(centerX, centerY), radius, radius);
                dc.DrawEllipse(brush, null, new Point(centerX, centerY), dotRadius, dotRadius);
                break;
            }
            case CrosshairType.DotCrosshair:
            {
                var halfLine = CrosshairSize * unit / 2.0;
                var gap = Gap * unit;
                var outer = gap + halfLine;
                var pen = new Pen(brush, lineWidth);
                dc.DrawEllipse(brush, null, new Point(centerX, centerY), dotRadius, dotRadius);
                dc.DrawLine(pen, new Point(centerX - outer, centerY), new Point(centerX - gap, centerY));
                dc.DrawLine(pen, new Point(centerX + gap, centerY), new Point(centerX + outer, centerY));
                dc.DrawLine(pen, new Point(centerX, centerY - outer), new Point(centerX, centerY - gap));
                dc.DrawLine(pen, new Point(centerX, centerY + gap), new Point(centerX, centerY + outer));
                break;
            }
            case CrosshairType.Custom:
            {
                var image = _image;
                if (image == null)
                {
                    return;
                }

                if (ScaleMode == CrosshairScaleMode.Fit)
                {
                    var scale = Math.Min(ActualWidth / image.Width, ActualHeight / image.Height);
                    var width = image.Width * scale;
                    var height = image.Height * scale;
                    dc.DrawImage(image, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
                }
                else
                {
                    var width = image.Width * unit;
                    var height = image.Height * unit;
                    dc.DrawImage(image, new Rect(centerX - width / 2, centerY - height / 2, width, height));
                }

                break;
            }
        }
    }

    private static BitmapSource? LoadImage(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
