using System;
using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    private readonly object _sync = new();
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
    /// 为整次连续执行保留取消状态，直到所有配置组和组间等待结束。
    /// 已有连续执行时返回 null，避免重入覆盖正在运行的上下文。
    /// </summary>
    public CancellationScope? TryBeginScope()
    {
        lock (_sync)
        {
            if (_scope != null)
            {
                return null;
            }

            Set();
            _scope = new CancellationScope(this, Cts.Token);
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
            if (disposed || _scope != null)
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
        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_scope != scope)
            {
                return;
            }

            _scope = null;
            cts = Cts;
            disposed = true;
        }

        cts.Dispose();
    }

    public sealed class CancellationScope : IDisposable
    {
        private readonly CancellationContext _context;

        public CancellationToken Token { get; }

        internal CancellationScope(CancellationContext context, CancellationToken token)
        {
            _context = context;
            Token = token;
        }

        public void Dispose() => _context.EndScope(this);
    }
}
