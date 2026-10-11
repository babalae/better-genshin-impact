using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BetterGenshinImpact.Helpers.Ui;

/// <summary>
/// 把任意线程、任意频率的 <see cref="Request"/> 合并成 UI 线程上的一次 apply。
/// <list type="bullet">
/// <item>单飞：同一时刻最多只有一个待执行的回调</item>
/// <item>限频：两次 apply 的间隔不小于 minInterval，不足时推迟到满足间隔再执行</item>
/// <item>不阻塞：只用 BeginInvoke，调用方立即返回</item>
/// </list>
/// 没有 Dispatcher（单测环境）时直接同步执行。
/// </summary>
public sealed class UiCoalescer
{
    private readonly Action _apply;
    private readonly TimeSpan _minInterval;
    private readonly DispatcherPriority _priority;
    private readonly Dispatcher? _dispatcher;

    private int _pending;

    // 以下字段只在 UI 线程访问
    private long _lastApplyTimestamp;
    private DispatcherTimer? _delayTimer;

    public UiCoalescer(Action apply, TimeSpan minInterval = default,
        DispatcherPriority priority = DispatcherPriority.Render, Dispatcher? dispatcher = null)
    {
        _apply = apply;
        _minInterval = minInterval;
        _priority = priority;
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher;
    }

    /// <summary>
    /// 任意线程调用。已有待执行的回调时直接返回
    /// </summary>
    public void Request()
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0)
        {
            return;
        }

        var dispatcher = _dispatcher;
        if (dispatcher == null)
        {
            Volatile.Write(ref _pending, 0);
            _apply();
            return;
        }

        if (dispatcher.HasShutdownStarted)
        {
            Volatile.Write(ref _pending, 0);
            return;
        }

        dispatcher.BeginInvoke(_priority, (Action)Run);
    }

    private void Run()
    {
        if (_minInterval > TimeSpan.Zero && _lastApplyTimestamp != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(_lastApplyTimestamp);
            if (elapsed < _minInterval)
            {
                ScheduleDelayed(_minInterval - elapsed);
                return;
            }
        }

        // 先复位再执行：apply 期间到达的请求会再排一次，不会丢失
        Volatile.Write(ref _pending, 0);
        _lastApplyTimestamp = Stopwatch.GetTimestamp();
        _apply();
    }

    private void ScheduleDelayed(TimeSpan delay)
    {
        if (_delayTimer == null)
        {
            _delayTimer = new DispatcherTimer(_priority, _dispatcher!);
            _delayTimer.Tick += (_, _) =>
            {
                _delayTimer!.Stop();
                Run();
            };
        }

        _delayTimer.Interval = delay;
        _delayTimer.Start();
    }
}
