using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Session;

/// <summary>
/// 保存当前异步调用链所属游戏会话。
/// 基于 <see cref="AsyncLocal{T}"/> 的上下文会随 Task 执行流传播；
/// 未进入会话作用域时，各兼容代理会自动回退到现有 PC 单例。
/// </summary>
public static class GameSessionContext
{
    // AsyncLocal 按异步执行流保存会话，不会让并发任务相互覆盖。
    private static readonly AsyncLocal<GameSession?> CurrentSession = new();

    /// <summary>
    /// 获取当前异步调用链所属会话；PC 旧链路中返回空。
    /// </summary>
    public static GameSession? Current => CurrentSession.Value;

    /// <summary>
    /// 进入指定会话作用域。返回值必须释放，以恢复进入前的会话上下文。
    /// </summary>
    public static IDisposable Enter(GameSession session)
    {
        // 支持嵌套进入：退出内层作用域时必须恢复外层原值。
        var previous = CurrentSession.Value;
        CurrentSession.Value = session;
        return new Scope(previous);
    }

    /// <summary>
    /// 恢复进入作用域前会话值的一次性句柄。
    /// </summary>
    /// <param name="previous">进入当前作用域前的会话。</param>
    private sealed class Scope(GameSession? previous) : IDisposable
    {
        // 防止重复 Dispose 再次改写后续调用已经设置的新上下文。
        private bool _disposed;

        /// <summary>
        /// 恢复外层会话上下文。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 恢复捕获值，而不是简单清空，以支持会话作用域嵌套。
            CurrentSession.Value = previous;
            _disposed = true;
        }
    }
}
