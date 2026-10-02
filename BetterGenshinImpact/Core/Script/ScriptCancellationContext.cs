using System;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 为一次脚本执行及其全部异步子调用传递同一个取消令牌。
/// </summary>
public static class ScriptCancellationContext
{
    /// <summary>
    /// 当前异步调用链显式指定的脚本取消令牌。
    /// </summary>
    private static readonly AsyncLocal<CancellationToken?> AmbientToken = new();

    /// <summary>
    /// 当前脚本调用应使用的令牌；非托管脚本调用保持兼容并回退到全局任务令牌。
    /// </summary>
    public static CancellationToken Token => AmbientToken.Value ?? CancellationContext.Instance.Cts.Token;

    /// <summary>
    /// 在当前异步调用链安装令牌，并在作用域结束时恢复外层令牌。
    /// </summary>
    public static IDisposable Push(CancellationToken token)
    {
        var previous = AmbientToken.Value;
        AmbientToken.Value = token;
        return new TokenScope(previous);
    }

    /// <summary>
    /// 恢复嵌套调用前的脚本取消令牌。
    /// </summary>
    private sealed class TokenScope : IDisposable
    {
        /// <summary>
        /// 进入当前作用域前的令牌。
        /// </summary>
        private readonly CancellationToken? _previous;

        /// <summary>
        /// 防止作用域重复释放后覆盖后续上下文。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 保存需要在退出时恢复的外层令牌。
        /// </summary>
        public TokenScope(CancellationToken? previous)
        {
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                AmbientToken.Value = _previous;
        }
    }
}
