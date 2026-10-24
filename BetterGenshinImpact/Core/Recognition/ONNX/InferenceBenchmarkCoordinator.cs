using System;
using System.Threading;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public interface IInferenceBenchmarkLeaseProvider
{
    bool IsAvailable { get; }
    IDisposable Acquire();
}

/// <summary>与独立任务使用同一把锁，暂停并等待正在执行的自动识别帧完成。</summary>
public sealed class InferenceBenchmarkCoordinator(SemaphoreSlim taskSemaphore, Func<IDisposable> suspend) : IInferenceBenchmarkLeaseProvider
{
    public bool IsAvailable => taskSemaphore.CurrentCount > 0;
    public IDisposable Acquire()
    {
        if (!taskSemaphore.Wait(0)) throw new InvalidOperationException("请先停止正在运行的任务，再进行推理测速。");
        try { return new Lease(taskSemaphore, suspend()); }
        catch { taskSemaphore.Release(); throw; }
    }

    private sealed class Lease(SemaphoreSlim semaphore, IDisposable suspension) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { suspension.Dispose(); }
            finally { semaphore.Release(); }
        }
    }
}
