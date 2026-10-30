using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>使加载层或底部通知跟随表体视口，避开滚动条占用的区域。</summary>
public sealed class AccountRecordsViewportOverlayBehavior : Behavior<FrameworkElement>
{
    /// <summary>仅对齐横向边界，保留底部通知自身的行位置和高度。</summary>
    public bool HorizontalOnly { get; set; }
    /// <summary>需要覆盖表体的表格。</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(DataGrid), typeof(AccountRecordsViewportOverlayBehavior));
    /// <summary>提供实际滚动视口的表格。</summary>
    public DataGrid? Target
    {
        get => (DataGrid?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>布局完成后更新位置，兼容主题模板及滚动条显隐变化。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.LayoutUpdated += UpdateBounds;
    }

    /// <summary>解除布局事件。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.LayoutUpdated -= UpdateBounds;
        base.OnDetaching();
    }

    /// <summary>按模板中表体呈现器的实际边界定位，不猜测表头及滚动条宽度。</summary>
    private void UpdateBounds(object? sender, EventArgs e)
    {
        if (!AssociatedObject.IsVisible || Target == null || AssociatedObject.Parent is not Visual parent) return;
        var presenter = FindViewport(Target);
        if (presenter == null) return;
        var origin = presenter.TranslatePoint(new Point(), parent as UIElement);
        var margin = HorizontalOnly
            ? new Thickness(origin.X, AssociatedObject.Margin.Top, 0, AssociatedObject.Margin.Bottom)
            : new Thickness(origin.X, origin.Y, 0, 0);
        if (AssociatedObject.Margin != margin) AssociatedObject.Margin = margin;
        if (AssociatedObject.Width != presenter.ActualWidth) AssociatedObject.Width = presenter.ActualWidth;
        if (!HorizontalOnly && AssociatedObject.Height != presenter.ActualHeight) AssociatedObject.Height = presenter.ActualHeight;
    }

    /// <summary>优先查找表格滚动容器的视口，避免误用单元格内部控件。</summary>
    private static ScrollContentPresenter? FindViewport(DependencyObject root)
    {
        if (root is ScrollContentPresenter presenter && presenter.Name == "PART_ScrollContentPresenter") return presenter;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindViewport(VisualTreeHelper.GetChild(root, index));
            if (found != null) return found;
        }
        return null;
    }
}
