using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>可见库存页每分钟刷新临期展示与规则，卸载时停止计时。</summary>
public sealed class AccountRecordsTimeRefreshBehavior : Behavior<FrameworkElement>
{
    /// <summary>交由 ViewModel 执行刷新。</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(AccountRecordsTimeRefreshBehavior));
    /// <summary>刷新命令。</summary>
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    /// <summary>仅 UI 存活时运行的分钟计时器。</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    /// <summary>注册视图生命周期事件。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        _timer.Tick += Tick;
        AssociatedObject.Loaded += Loaded;
        AssociatedObject.Unloaded += Unloaded;
        if (AssociatedObject.IsLoaded) _timer.Start();
    }
    /// <summary>加载时启动，首次数据读取由页面原有流程处理。</summary>
    private void Loaded(object sender, RoutedEventArgs e) => _timer.Start();
    /// <summary>页面卸载后不继续轮询。</summary>
    private void Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();
    /// <summary>只刷新可见页面，异步命令运行中不重复执行。</summary>
    private void Tick(object? sender, EventArgs e)
    {
        if (AssociatedObject.IsVisible && Command?.CanExecute(null) == true) Command.Execute(null);
    }
    /// <summary>解除计时器和视图引用。</summary>
    protected override void OnDetaching()
    {
        _timer.Stop(); _timer.Tick -= Tick;
        AssociatedObject.Loaded -= Loaded; AssociatedObject.Unloaded -= Unloaded;
        base.OnDetaching();
    }
}
