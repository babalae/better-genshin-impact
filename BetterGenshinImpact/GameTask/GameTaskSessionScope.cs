using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 标记当前异步流已经持有任务会话（任务锁、运行环境和任务模式）。
/// 会话内的代码不能再通过 TaskRunner 启动任务，应直接调用任务的 RunAsync。
/// </summary>
public static class GameTaskSessionScope
{
    /// <summary>
    /// 当前异步流是否处于会话内。
    /// </summary>
    private static readonly AsyncLocal<bool> Active = new();

    /// <summary>
    /// 当前异步流是否处于会话内。
    /// </summary>
    public static bool IsActive => Active.Value;

    /// <summary>
    /// 进入会话。必须在持有会话的方法内用 using 包住会话期间的调用，标记才会流向后续 await。
    /// </summary>
    /// <returns>离开会话时恢复原值的句柄。</returns>
    public static IDisposable Enter()
    {
        var previous = Active.Value;
        Active.Value = true;
        return new Scope(previous);
    }

    /// <summary>
    /// 离开会话时恢复原值。
    /// </summary>
    /// <param name="previous">进入前的值。</param>
    private sealed class Scope(bool previous) : IDisposable
    {
        /// <summary>
        /// 是否已经恢复。
        /// </summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Active.Value = previous;
        }
    }
}
