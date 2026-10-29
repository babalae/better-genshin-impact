using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 左键点击按钮时在按钮下方打开它的 ContextMenu，并在打开前、关闭后执行命令（用于准备菜单内容、保持窗口栏显示）
/// </summary>
public sealed class ButtonContextMenuBehavior : Behavior<ButtonBase>
{
    /// <summary>
    /// 打开菜单前执行的命令
    /// </summary>
    public static readonly DependencyProperty OpeningCommandProperty =
        DependencyProperty.Register(nameof(OpeningCommand), typeof(ICommand), typeof(ButtonContextMenuBehavior));

    /// <summary>
    /// 菜单关闭后执行的命令
    /// </summary>
    public static readonly DependencyProperty ClosedCommandProperty =
        DependencyProperty.Register(nameof(ClosedCommand), typeof(ICommand), typeof(ButtonContextMenuBehavior));

    /// <summary>
    /// 打开菜单前执行的命令
    /// </summary>
    public ICommand? OpeningCommand
    {
        get => (ICommand?)GetValue(OpeningCommandProperty);
        set => SetValue(OpeningCommandProperty, value);
    }

    /// <summary>
    /// 菜单关闭后执行的命令
    /// </summary>
    public ICommand? ClosedCommand
    {
        get => (ICommand?)GetValue(ClosedCommandProperty);
        set => SetValue(ClosedCommandProperty, value);
    }

    /// <summary>
    /// 当前订阅了关闭事件的菜单
    /// </summary>
    private ContextMenu? _menu;

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Click += OnClick;
        // 右键与左键一样处理，避免系统默认的打开方式绕过准备命令
        AssociatedObject.ContextMenuOpening += OnContextMenuOpening;
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        AssociatedObject.Click -= OnClick;
        AssociatedObject.ContextMenuOpening -= OnContextMenuOpening;
        DetachMenu();
        base.OnDetaching();
    }

    /// <summary>
    /// 左键点击：打开菜单
    /// </summary>
    private void OnClick(object sender, RoutedEventArgs e)
    {
        Open();
    }

    /// <summary>
    /// 右键：改为与左键相同的打开方式
    /// </summary>
    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        Open();
    }

    /// <summary>
    /// 执行准备命令后在按钮下方打开菜单
    /// </summary>
    private void Open()
    {
        if (AssociatedObject.ContextMenu is not { } menu || menu.IsOpen)
        {
            return;
        }

        EnsureMenuDataContext(menu);
        if (OpeningCommand is { } opening && opening.CanExecute(null))
        {
            opening.Execute(null);
        }

        DetachMenu();
        _menu = menu;
        menu.Closed += OnMenuClosed;
        menu.PlacementTarget = AssociatedObject;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// 把菜单的 DataContext 固定绑定到按钮的 DataContext。
    /// ContextMenu 默认只在打开期间继承 DataContext，关闭时变为 null；菜单项列表随之清空，
    /// 下次打开时在弹出过程中重建的菜单项继承不到菜单的文字颜色，显示为默认的黑色。
    /// 固定后菜单项只在打开前由准备命令更新，不在弹出过程中重建
    /// </summary>
    private void EnsureMenuDataContext(ContextMenu menu)
    {
        if (BindingOperations.GetBindingExpression(menu, FrameworkElement.DataContextProperty) is not null)
        {
            return;
        }

        BindingOperations.SetBinding(menu, FrameworkElement.DataContextProperty,
            new Binding(nameof(FrameworkElement.DataContext)) { Source = AssociatedObject });
    }

    /// <summary>
    /// 菜单关闭：执行关闭命令
    /// </summary>
    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        DetachMenu();
        if (ClosedCommand is { } closed && closed.CanExecute(null))
        {
            closed.Execute(null);
        }
    }

    /// <summary>
    /// 取消对菜单关闭事件的订阅
    /// </summary>
    private void DetachMenu()
    {
        if (_menu is { } menu)
        {
            menu.Closed -= OnMenuClosed;
            _menu = null;
        }
    }
}
