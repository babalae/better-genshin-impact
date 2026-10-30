using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>导航时先呈现已有视图，再在 UI 空闲时检查库存快照。</summary>
public sealed class AccountRecordsDeferredLoadBehavior : Behavior<FrameworkElement>
{
    /// <summary>页面检查与加载命令。</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(AccountRecordsDeferredLoadBehavior));

    /// <summary>交由页面 ViewModel 执行的加载命令。</summary>
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>尚未开始的检查；切走时取消，避免过期的导航触发加载。</summary>
    private DispatcherOperation? _pendingLoad;

    /// <summary>监听页面加载和可见性，兼容缓存页面的再次显示。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Loaded += Loaded;
        AssociatedObject.Unloaded += Unloaded;
        AssociatedObject.IsVisibleChanged += VisibilityChanged;
        ScheduleLoad();
    }

    /// <summary>加载事件仅排队，不直接读取数据或更新绑定。</summary>
    private void Loaded(object sender, RoutedEventArgs e) => ScheduleLoad();

    /// <summary>离开页面时取消尚未开始的检查。</summary>
    private void Unloaded(object sender, RoutedEventArgs e) => CancelPendingLoad();

    /// <summary>隐藏时取消检查，再次显示时重新排队。</summary>
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => ScheduleLoad();

    /// <summary>让已排队的布局、渲染和输入优先完成，再执行快照时间检查。</summary>
    private void ScheduleLoad()
    {
        CancelPendingLoad();
        if (!AssociatedObject.IsLoaded || !AssociatedObject.IsVisible) return;
        _pendingLoad = AssociatedObject.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _pendingLoad = null;
            if (AssociatedObject.IsLoaded && AssociatedObject.IsVisible && Command?.CanExecute(null) == true)
                Command.Execute(null);
        }));
    }

    /// <summary>取消排队操作；已经开始的异步加载仍由 ViewModel 管理。</summary>
    private void CancelPendingLoad()
    {
        _pendingLoad?.Abort();
        _pendingLoad = null;
    }

    /// <summary>释放排队操作和页面事件引用。</summary>
    protected override void OnDetaching()
    {
        CancelPendingLoad();
        AssociatedObject.Loaded -= Loaded;
        AssociatedObject.Unloaded -= Unloaded;
        AssociatedObject.IsVisibleChanged -= VisibilityChanged;
        base.OnDetaching();
    }
}
