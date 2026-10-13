using System;
using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    private readonly object _sync = new();
    private readonly AsyncLocal<CancellationScope?> _currentScope = new();
    private CancellationScope? _scope;
    public CancellationTokenSource Cts { get; private set; } = new();
    public bool IsManualStop { get; private set; }

    public bool IsCancellationRequested
    {
        get
        {
            lock (_sync) 
            {
                return !disposed && Cts.IsCancellationRequested; 
            }
        }
    }

    private bool disposed;

    /// <summary>
    /// 独占一次顶层执行，覆盖启动等待、子任务和组间等待。
    /// 已有任务时返回 null，避免其他入口覆盖或释放正在使用的取消源。
    /// </summary>
    public CancellationScope? TryBeginScope() => TryEnterScope(false);

    /// <summary>
    /// 子任务只能加入同一异步调用链的作用域；没有活动任务时创建新作用域。
    /// </summary>
    public CancellationScope? TryJoinOrBeginScope() => TryEnterScope(true);

    private CancellationScope? TryEnterScope(bool allowJoin)
    {
        lock (_sync)
        {
            var currentScope = _currentScope.Value;
            // 延迟启动的子任务不能使用已经退出的调用链，也不能加入后来的新任务。
            if (currentScope is { IsDisposed: true })
            {
                return null;
            }

            if (_scope != null)
            {
                if (!allowJoin || currentScope?.Owner != _scope)
                {
                    return null;
                }

                var childScope = new CancellationScope(this, _scope.Token, currentScope);
                _scope.LeaseCount++;
                _currentScope.Value = childScope;
                return childScope;
            }

            Set();
            _scope = new CancellationScope(this, Cts.Token, null);
            _currentScope.Value = _scope;
            return _scope;
        }
    }

    public void Set()
    {
        lock (_sync)
        {
            // 子任务不能重置整次连续执行的取消状态。
            if (_scope != null)
            {
                EnsureScopeOwner();
                return;
            }

            Cts = new CancellationTokenSource();
            IsManualStop = false;
            disposed = false;
        }
    }

    public void ManualCancel()
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (disposed)
            {
                return;
            }

            IsManualStop = true;
            cts = Cts;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 并发 Clear 可能已释放 CTS，这里视为已取消/已清理。
        }
    }

    public void Cancel()
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (disposed)
            {
                return;
            }

            cts = Cts;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 并发 Clear 可能已释放 CTS，这里视为已取消/已清理。
        }
    }

    public void Clear()
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            // 子任务结束后仍需在组间等待期间接收停止指令。
            if (_scope != null)
            {
                EnsureScopeOwner();
                return;
            }

            if (disposed)
            {
                return;
            }

            cts = Cts;
            disposed = true;
        }

        cts.Dispose();
    }

    private void EndScope(CancellationScope scope)
    {
        CancellationTokenSource? cts = null;
        lock (_sync)
        {
            if (_currentScope.Value == scope)
            {
                var parent = scope.Parent;
                while (parent is { IsDisposed: true })
                {
                    parent = parent.Parent;
                }
                _currentScope.Value = parent;
            }

            if (scope.IsDisposed)
            {
                return;
            }

            scope.IsDisposed = true;
            if (--scope.Owner.LeaseCount == 0 && _scope == scope.Owner)
            {
                _scope = null;
                cts = Cts;
                disposed = true;
            }
        }

        cts?.Dispose();
    }

    private void EnsureScopeOwner()
    {
        if (_currentScope.Value is not { IsDisposed: false } currentScope || currentScope.Owner != _scope)
        {
            throw new InvalidOperationException("其他任务正在使用取消上下文，请先获取执行作用域。");
        }
    }

    public sealed class CancellationScope : IDisposable
    {
        private readonly CancellationContext _context;

        public CancellationToken Token { get; }

        internal CancellationScope Owner { get; }
        internal CancellationScope? Parent { get; }
        internal int LeaseCount { get; set; } = 1;
        internal bool IsDisposed { get; set; }

        internal CancellationScope(CancellationContext context, CancellationToken token, CancellationScope? parent)
        {
            _context = context;
            Token = token;
            Parent = parent;
            Owner = parent?.Owner ?? this;
        }

        public void Dispose() => _context.EndScope(this);
    }
}
