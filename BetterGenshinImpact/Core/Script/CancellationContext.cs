using System;
using BetterGenshinImpact.Model;
using System.Threading;

namespace BetterGenshinImpact.Core.Script;

public class CancellationContext : Singleton<CancellationContext>
{
    private readonly object _sync = new();
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

    public void Set()
    {
        lock (_sync)
        {
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
            if (disposed)
            {
                return;
            }

            cts = Cts;
            disposed = true;
        }

        // 先取消再释放：已发出的令牌必须被触发。回调异常不得外抛，否则调用方后续清理会被跳过。
        try
        {
            cts.Cancel();
        }
        catch (Exception)
        {
            // 取消回调自身抛异常时仍须完成释放
        }

        cts.Dispose();
    }

    /// <summary>
    /// 任务令牌。Clear() 会并发释放 CTS，此时返回 false 且 token 为 <see cref="CancellationToken.None"/>。
    /// </summary>
    public bool TryGetToken(out CancellationToken token)
    {
        try
        {
            token = Cts.Token;
            return true;
        }
        catch (ObjectDisposedException)
        {
            token = CancellationToken.None;
            return false;
        }
    }

    /// <summary>
    /// 取任务令牌；<paramref name="preferred"/> 可取消时优先用它。
    /// Clear() 并发释放 CTS 时退化为不可取消。
    /// </summary>
    public CancellationToken ResolveToken(CancellationToken preferred = default)
    {
        if (preferred.CanBeCanceled)
        {
            return preferred;
        }

        return TryGetToken(out var taskToken) ? taskToken : CancellationToken.None;
    }
}
