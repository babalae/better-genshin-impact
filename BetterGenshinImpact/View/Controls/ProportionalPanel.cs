using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Controls;

/// <summary>
/// 横向排列子元素，宽度按附加属性 <see cref="WeightProperty"/> 的比例分配。用于占用比例条等场景
/// </summary>
public sealed class ProportionalPanel : Panel
{
    /// <summary>
    /// 子元素的权重，权重越大占用的宽度越多；全部为 0 时不显示任何子元素
    /// </summary>
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>
    /// 读取子元素的权重
    /// </summary>
    public static double GetWeight(UIElement element) => (double)element.GetValue(WeightProperty);

    /// <summary>
    /// 设置子元素的权重
    /// </summary>
    public static void SetWeight(UIElement element, double value) => element.SetValue(WeightProperty, value);

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // 宽度由父元素拉伸决定，这里不提出要求
        return new Size(0, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        var total = children.Sum(child => Math.Max(0, GetWeight(child)));
        var x = 0d;
        foreach (var child in children)
        {
            var width = total > 0 ? finalSize.Width * Math.Max(0, GetWeight(child)) / total : 0;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
