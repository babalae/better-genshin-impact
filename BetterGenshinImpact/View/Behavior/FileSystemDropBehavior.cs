using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>将 Windows 文件拖放转为单路径命令，并提供拖入高亮和拒绝原因。</summary>
public sealed class FileSystemDropBehavior : Behavior<FrameworkElement>
{
    /// <summary>接收一个文件或目录路径的命令。</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(FileSystemDropBehavior), new PropertyMetadata(null));
    /// <summary>向视图模型报告多项拖入等错误的命令。</summary>
    public static readonly DependencyProperty RejectedCommandProperty = DependencyProperty.Register(
        nameof(RejectedCommand), typeof(ICommand), typeof(FileSystemDropBehavior), new PropertyMetadata(null));
    /// <summary>向视图模型回传拖入高亮状态的依赖属性。</summary>
    public static readonly DependencyProperty IsDraggingOverProperty = DependencyProperty.Register(
        nameof(IsDraggingOver), typeof(bool), typeof(FileSystemDropBehavior), new PropertyMetadata(false));

    /// <summary>成功拖入单一路径时执行；来源类型校验留给调用方。</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    /// <summary>拖放数据无效时执行，参数为面向用户的原因。</summary>
    public ICommand? RejectedCommand
    {
        get => (ICommand?)GetValue(RejectedCommandProperty);
        set => SetValue(RejectedCommandProperty, value);
    }
    /// <summary>有效的单项文件拖放目前是否停留在目标内。</summary>
    public bool IsDraggingOver
    {
        get => (bool)GetValue(IsDraggingOverProperty);
        set => SetValue(IsDraggingOverProperty, value);
    }

    /// <summary>读取唯一来源路径，多项拖入必须由用户重新选择。</summary>
    public static string GetSinglePath(IReadOnlyList<string> paths)
    {
        if (paths.Count != 1) throw new ArgumentException("一次只能添加一个仓库目录或 ZIP 压缩包。");
        if (string.IsNullOrWhiteSpace(paths[0])) throw new ArgumentException("未取得有效的文件或目录路径。");
        return paths[0];
    }

    /// <summary>监听预览事件，使目标内的按钮和文字都可接收文件拖放。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewDragEnter += OnDragOver;
        AssociatedObject.PreviewDragOver += OnDragOver;
        AssociatedObject.PreviewDragLeave += OnDragLeave;
        AssociatedObject.PreviewDrop += OnDrop;
        AssociatedObject.IsEnabledChanged += OnIsEnabledChanged;
    }

    /// <summary>释放事件订阅和悬停状态，避免保留已经关闭的窗口。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewDragEnter -= OnDragOver;
        AssociatedObject.PreviewDragOver -= OnDragOver;
        AssociatedObject.PreviewDragLeave -= OnDragLeave;
        AssociatedObject.PreviewDrop -= OnDrop;
        AssociatedObject.IsEnabledChanged -= OnIsEnabledChanged;
        IsDraggingOver = false;
        base.OnDetaching();
    }

    /// <summary>只有空闲目标、单一路径和可执行命令才显示允许拖入。</summary>
    private void OnDragOver(object sender, DragEventArgs args)
    {
        var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
        var accepted = AssociatedObject.IsEnabled && paths is { Length: 1 }
            && Command?.CanExecute(paths[0]) == true;
        // 多项拖入显示禁止光标时不会产生 Drop，因此在悬停阶段直接说明拒绝原因。
        if (AssociatedObject.IsEnabled && paths is { Length: > 1 })
        {
            const string message = "一次只能添加一个仓库目录或 ZIP 压缩包。";
            if (RejectedCommand?.CanExecute(message) == true) RejectedCommand.Execute(message);
        }
        IsDraggingOver = accepted;
        args.Effects = accepted ? args.AllowedEffects & DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    /// <summary>鼠标离开目标外沿后清除高亮，不因进入目标内的子控件闪烁。</summary>
    private void OnDragLeave(object sender, DragEventArgs args)
    {
        var position = args.GetPosition(AssociatedObject);
        if (position.X < 0 || position.Y < 0 || position.X >= AssociatedObject.ActualWidth
            || position.Y >= AssociatedObject.ActualHeight) IsDraggingOver = false;
        args.Handled = true;
    }

    /// <summary>解析单一路径交给命令；无效拖放通过独立命令在窗口内反馈。</summary>
    private void OnDrop(object sender, DragEventArgs args)
    {
        IsDraggingOver = false;
        args.Effects = DragDropEffects.None;
        args.Handled = true;
        if (!AssociatedObject.IsEnabled) return;
        string path;
        try
        {
            var paths = args.Data.GetData(DataFormats.FileDrop) as string[]
                ?? throw new ArgumentException("请拖入文件资源管理器中的仓库目录或 ZIP 压缩包。");
            path = GetSinglePath(paths);
        }
        catch (ArgumentException ex)
        {
            if (RejectedCommand?.CanExecute(ex.Message) == true) RejectedCommand.Execute(ex.Message);
            return;
        }
        if (Command?.CanExecute(path) != true) return;
        args.Effects = args.AllowedEffects & DragDropEffects.Copy;
        Command.Execute(path);
    }

    /// <summary>处理开始时立即清除拖入高亮。</summary>
    private void OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!AssociatedObject.IsEnabled) IsDraggingOver = false;
    }
}
