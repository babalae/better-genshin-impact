using System;
using System.Windows;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Mask;

/// <summary>
/// 遮罩窗口渲染层的坐标换算参数。
/// 渲染层位于窗口内容树中，外层可能有 DpiAwarenessController 施加的 LayoutTransform，
/// 所以不能直接用 VisualTreeHelper.GetDpi，需要把"本层 → 窗口"的缩放和"窗口 → 设备"的缩放一起算进去。
/// </summary>
/// <param name="RootScaleX">本层单位到窗口 DIP 的缩放</param>
/// <param name="RootScaleY">本层单位到窗口 DIP 的缩放</param>
/// <param name="DeviceScaleX">窗口 DIP 到物理像素的缩放</param>
/// <param name="DeviceScaleY">窗口 DIP 到物理像素的缩放</param>
internal readonly record struct MaskWindowLayerMetrics(double RootScaleX, double RootScaleY, double DeviceScaleX, double DeviceScaleY)
{
    /// <summary>
    /// 物理像素（捕获像素）到本层单位
    /// </summary>
    public double PixelToUnitX => 1 / (RootScaleX * DeviceScaleX);

    public double PixelToUnitY => 1 / (RootScaleY * DeviceScaleY);

    /// <summary>
    /// 窗口 DIP 长度（线宽、准星尺寸等配置值）到本层单位
    /// </summary>
    public double DipToUnit => 1 / RootScaleX;

    public static MaskWindowLayerMetrics From(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        try
        {
            var source = PresentationSource.FromVisual(element);
            if (source?.CompositionTarget == null || source.RootVisual == null)
            {
                return new MaskWindowLayerMetrics(1, 1, dpi.DpiScaleX, dpi.DpiScaleY);
            }

            var toDevice = source.CompositionTarget.TransformToDevice;
            var toRoot = element.TransformToAncestor(source.RootVisual);
            var origin = toRoot.Transform(new Point(0, 0));
            var rootScaleX = (toRoot.Transform(new Point(1, 0)) - origin).Length;
            var rootScaleY = (toRoot.Transform(new Point(0, 1)) - origin).Length;
            return new MaskWindowLayerMetrics(
                Sanitize(rootScaleX),
                Sanitize(rootScaleY),
                Sanitize(toDevice.M11),
                Sanitize(toDevice.M22));
        }
        catch (InvalidOperationException)
        {
            return new MaskWindowLayerMetrics(1, 1, dpi.DpiScaleX, dpi.DpiScaleY);
        }
    }

    private static double Sanitize(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }
}
