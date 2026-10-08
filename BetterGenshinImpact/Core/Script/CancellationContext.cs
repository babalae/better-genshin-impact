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

    /// <summary>
    /// 本次执行已被停止或取消。
    /// <para>
    /// 不能只看 <see cref="IsCancellationRequested"/>：任务收尾时 <see cref="Clear"/> 会把上下文置为已清理，
    /// 该属性随即变回 false；而 <see cref="IsManualStop"/> 只有下一次 <see cref="Set"/> 才会复位。
    /// 因此这里同时看「用户停止标记」与 CTS 本身的取消状态（CTS 释放后读取取消状态是安全的）。
    /// </para>
    /// </summary>
    public bool IsAborted => IsManualStop || Cts.IsCancellationRequested;

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

        cts.Dispose();
    }
}
