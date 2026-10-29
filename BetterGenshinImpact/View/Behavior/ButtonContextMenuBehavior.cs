using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
