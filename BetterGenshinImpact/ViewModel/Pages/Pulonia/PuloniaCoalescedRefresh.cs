using System;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 合并高频刷新请求：请求可来自任意线程，刷新始终在 UI 线程依次执行；
/// 刷新进行中再收到请求时，只在结束后补跑一次，不堆积重复查询，也不漏掉最后一次变化。
/// </summary>
public sealed class PuloniaCoalescedRefresh
{
    /// <summary>实际的刷新操作，在 UI 线程调用。</summary>
    private readonly Func<Task> _refresh;

    /// <summary>每轮刷新前等待的合并窗口；为零时不等待。</summary>
    private readonly TimeSpan _delay;

    /// <summary>是否有一轮刷新正在执行。</summary>
    private bool _isRunning;

    /// <summary>是否有尚未处理的刷新请求。</summary>
    private bool _isRequested;

    /// <summary>建立刷新合并器。</summary>
    /// <param name="refresh">刷新操作；异常只记录调试信息，不中断后续请求。</param>
    /// <param name="delay">每轮刷新前的合并窗口。</param>
    public PuloniaCoalescedRefresh(Func<Task> refresh, TimeSpan delay = default)
    {
        _refresh = refresh;
        _delay = delay;
    }

    /// <summary>请求一次刷新；没有 UI 调度器或程序正在关闭时忽略。</summary>
    public void Request()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;
        dispatcher.BeginInvoke(new Action(() => _ = RunAsync()));
    }

    /// <summary>在 UI 线程执行刷新循环，直到没有新的请求。</summary>
    private async Task RunAsync()
    {
        _isRequested = true;
        if (_isRunning)
            return;
        _isRunning = true;
        try
        {
            while (_isRequested)
            {
                if (_delay > TimeSpan.Zero)
                    await Task.Delay(_delay);
                _isRequested = false;
                try
                {
                    await _refresh();
                }
                catch (Exception ex)
                {
                    // 刷新操作自行报告可预期的错误；这里只防止未观察的异常中断后续刷新。
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }
        finally
        {
            _isRunning = false;
        }
    }
}
