using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 按 <see cref="GuideMenuOption"/> 列表生成菜单项，可用于 ContextMenu 或 MenuItem 的子菜单。
/// 菜单项直接创建为 MenuItem 元素，沿用主题的隐式样式，不需要 ItemContainerStyle
/// </summary>
public sealed class MenuOptionsBehavior : Behavior<ItemsControl>
{
    /// <summary>
    /// 菜单选项列表（实现 INotifyCollectionChanged 时随列表变化重建）
    /// </summary>
    public static readonly DependencyProperty OptionsProperty =
        DependencyProperty.Register(nameof(Options), typeof(IEnumerable), typeof(MenuOptionsBehavior),
            new PropertyMetadata(null, OnOptionsChanged));

    /// <summary>
    /// 菜单选项列表
    /// </summary>
    public IEnumerable? Options
    {
        get => (IEnumerable?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        Rebuild();
    }

    /// <summary>
    /// 列表对象变化：改为订阅新列表
    /// </summary>
    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var behavior = (MenuOptionsBehavior)d;
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= behavior.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += behavior.OnCollectionChanged;
        }

        behavior.Rebuild();
    }

    /// <summary>
    /// 列表内容变化：重建子菜单
    /// </summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Rebuild();
    }

    /// <summary>
    /// 用当前列表重建子菜单项
    /// </summary>
    private void Rebuild()
    {
        if (AssociatedObject is not { } menu)
        {
            return;
        }

        menu.Items.Clear();
        if (Options is null)
        {
            return;
        }

        foreach (var item in Options)
        {
            if (item is not GuideMenuOption option)
            {
                continue;
            }

            menu.Items.Add(new MenuItem
            {
                Header = option.DisplayText,
                Command = option.Command,
                CommandParameter = option,
                IsChecked = option.IsChecked,
                IsEnabled = option.IsEnabled,
                ToolTip = string.IsNullOrEmpty(option.Detail) ? null : option.Header,
            });
        }
    }
}
