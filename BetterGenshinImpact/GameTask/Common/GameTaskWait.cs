using System;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 游戏任务的同步等待边界：取消或原运行环境失效时退出，避免恢复焦点、暂停和长延时阻塞任务收尾。
/// </summary>
public sealed class GameTaskWait
{
    /// <summary>
    /// 无取消令牌的旧调用也需定期检查运行环境，最多每隔 100 毫秒检查一次。
    /// </summary>
    private const int CheckIntervalMilliseconds = 100;

    /// <summary>
    /// 检查本次等待开始时绑定的运行环境是否仍可使用，不跟随新绑定的环境。
    /// </summary>
    private readonly Func<bool> _isRuntimeAvailable;

    /// <summary>
    /// 当前任务的局部取消令牌，不持有或释放其来源。
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// 使用运行环境检查与本轮取消令牌建立等待边界，不访问应用宿主或真实游戏资源。
    /// </summary>
    public GameTaskWait(Func<bool> isRuntimeAvailable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isRuntimeAvailable);
        _isRuntimeAvailable = isRuntimeAvailable;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// 在每轮等待或恢复窗口前检查停止条件，沿用旧任务的正常中断异常。
    /// </summary>
    public void ThrowIfStopped()
    {
        if (_cancellationToken.IsCancellationRequested)
            throw new NormalEndException("取消自动任务");
        if (!_isRuntimeAvailable())
            throw new NormalEndException("游戏运行环境已停止");
    }

    /// <summary>
    /// 将同步延时切成可中断的短等待；令牌取消可立即唤醒，无令牌入口也不会无限等待失效的环境。
    /// </summary>
    public void Sleep(int millisecondsTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, Timeout.Infinite);
        ThrowIfStopped();
        var remaining = millisecondsTimeout;
        while (remaining != 0)
        {
            var interval = remaining == Timeout.Infinite
                ? CheckIntervalMilliseconds
                : Math.Min(remaining, CheckIntervalMilliseconds);
            if (_cancellationToken.CanBeCanceled)
                _cancellationToken.WaitHandle.WaitOne(interval);
            else
                Thread.Sleep(interval);

            // 环境已经解绑或游戏进程已退出时，不再使用旧窗口恢复焦点或继续模拟输入。
            ThrowIfStopped();
            if (remaining != Timeout.Infinite)
                remaining -= interval;
        }
    }
}
