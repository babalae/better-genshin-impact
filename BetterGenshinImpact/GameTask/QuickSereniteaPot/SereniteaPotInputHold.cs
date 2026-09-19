using System;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

// PostMessage 按键不能依赖 SendInput.ReleaseAllKey 清理。
internal sealed class SereniteaPotInputHold(Action press, Action release, CancellationToken ct) : ISuspendable, IDisposable
{
    private bool disposed;
    public bool IsSuspended { get; private set; } = true;

    public void Resume()
    {
        if (disposed || !IsSuspended || ct.IsCancellationRequested) return;
        IsSuspended = false;
        press();
    }

    public void Suspend()
    {
        if (disposed || IsSuspended) return;
        release();
        IsSuspended = true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        release();
        IsSuspended = true;
    }
}
