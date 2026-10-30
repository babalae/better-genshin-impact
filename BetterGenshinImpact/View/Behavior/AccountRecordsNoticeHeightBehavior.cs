using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>多条通知只展示首条及下一条的一部分，保留滚动入口而不挤占库存表格。</summary>
public sealed class AccountRecordsNoticeHeightBehavior : Behavior<ScrollViewer>
{
    /// <summary>当前提示目标变化时重置位置，同一目标刷新内容时保留滚动位置。</summary>
    public static readonly DependencyProperty NoticeTargetProperty = DependencyProperty.Register(
        nameof(NoticeTarget), typeof(object), typeof(AccountRecordsNoticeHeightBehavior),
        new PropertyMetadata(null, OnNoticeTargetChanged));

    /// <summary>由视图绑定当前提示物品，行为不依赖具体业务类型。</summary>
    public object? NoticeTarget
    {
        get => GetValue(NoticeTargetProperty);
        set => SetValue(NoticeTargetProperty, value);
    }

    /// <summary>切换物品或关闭提示后，下次展示从第一条开始。</summary>
    private static void OnNoticeTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var behavior = (AccountRecordsNoticeHeightBehavior)sender;
        behavior.AssociatedObject?.ScrollToTop();
    }

    /// <summary>监听模板布局，兼容通知刷新、文本换行及窗口宽度变化。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>释放布局监听。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.LayoutUpdated -= OnLayoutUpdated;
        base.OnDetaching();
    }

    /// <summary>根据第一张卡片的实际高度计算可视区，上限用于保护超长单条通知。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var height = 260d;
        if (AssociatedObject.Content is ItemsControl { Items.Count: > 1 } items &&
            items.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement { ActualHeight: > 0 } first)
            height = Math.Min(height, first.ActualHeight * 1.2);
        // 只有目标高度变化时才重新布局，避免 LayoutUpdated 引发重复失效。
        if (Math.Abs(AssociatedObject.MaxHeight - height) > 0.5)
            AssociatedObject.MaxHeight = height;
    }
}
