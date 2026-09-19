using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

/// <summary>单次尘歌壶操作的单调时钟，仅扣除暂停组件收到的显式暂停时间。</summary>
internal sealed class SereniteaPotActiveTime : TimeProvider, ISuspendable, IDisposable
{
    private readonly TimeProvider clock;
    private readonly IDictionary<string, ISuspendable> components;
    private readonly string registration = $"pot-clock-{Guid.NewGuid():N}";
    private readonly long started;
    private long pausedAt;
    private long pausedTicks;
    private bool disposed;

    internal SereniteaPotActiveTime(IDictionary<string, ISuspendable> components, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.components = components;
        started = GetTimestamp();
        components.Add(registration, this);
    }

    public bool IsSuspended { get; private set; }
    public override long TimestampFrequency => clock.TimestampFrequency;
    public override long GetTimestamp() => (IsSuspended ? pausedAt : clock.GetTimestamp()) - pausedTicks;
    internal TimeSpan Elapsed => GetElapsedTime(started);
    internal double ElapsedMilliseconds => Elapsed.TotalMilliseconds;

    public void Suspend()
    {
        if (disposed || IsSuspended) return;
        pausedAt = clock.GetTimestamp();
        IsSuspended = true;
    }

    public void Resume()
    {
        if (disposed || !IsSuspended) return;
        pausedTicks += clock.GetTimestamp() - pausedAt;
        IsSuspended = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (components.TryGetValue(registration, out var current) && ReferenceEquals(current, this))
            components.Remove(registration);
    }
}
