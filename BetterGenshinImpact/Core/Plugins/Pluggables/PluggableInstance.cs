using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 一个实现的实例及其上进行中的调用数（租约）。
/// 实例退役后不再接受新租约，最后一个租约归还时释放实例，保证进行中的调用不会遇到已释放的对象。
/// </summary>
internal sealed class PluggableInstance(PluggableImplDescriptor implementation, object target, ILogger logger)
{
    /// <summary>
    /// 保护租约计数和退役标记。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 实例释放完成时完成，供停用插件时等待。
    /// </summary>
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 进行中的调用数。
    /// </summary>
    private int _leases;

    /// <summary>
    /// 是否已退役。
    /// </summary>
    private bool _retired;

    /// <summary>
    /// 实例对应的实现。
    /// </summary>
    public PluggableImplDescriptor Implementation { get; } = implementation;

    /// <summary>
    /// 实现对象本身。
    /// </summary>
    public object Target { get; } = target;

    /// <summary>
    /// 实例释放完成的任务。
    /// </summary>
    public Task Released => _released.Task;

    /// <summary>
    /// 申请一个租约；实例已退役时返回 null。
    /// </summary>
    public PluggableLease? TryAcquire()
    {
        lock (_gate)
        {
            if (_retired)
            {
                return null;
            }

            _leases++;
            return new PluggableLease(this);
        }
    }

    /// <summary>
    /// 归还一个租约；已退役且没有租约时释放实例。只由 <see cref="PluggableLease"/> 调用。
    /// </summary>
    public void Release()
    {
        bool dispose;
        lock (_gate)
        {
            _leases--;
            dispose = _retired && _leases == 0;
        }

        if (dispose)
        {
            DisposeTarget();
        }
    }

    /// <summary>
    /// 退役：不再接受新租约，没有进行中的调用时立即释放。重复调用无副作用。
    /// </summary>
    public void Retire()
    {
        bool dispose;
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            dispose = _leases == 0;
        }

        if (dispose)
        {
            DisposeTarget();
        }
    }

    /// <summary>
    /// 释放实现对象。释放失败只记录日志，不影响调用方。
    /// </summary>
    private void DisposeTarget()
    {
        try
        {
            switch (Target)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IAsyncDisposable asyncDisposable:
                    // 只实现了异步释放的实例在后台释放，释放完成后再标记完成
                    _ = DisposeAsyncCore(asyncDisposable);
                    return;
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "释放可插拔实现 {Id} 的实例失败", Implementation.Id);
        }

        _released.TrySetResult();
    }

    /// <summary>
    /// 异步释放实现对象并标记完成。
    /// </summary>
    private async Task DisposeAsyncCore(IAsyncDisposable asyncDisposable)
    {
        try
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "释放可插拔实现 {Id} 的实例失败", Implementation.Id);
        }
        finally
        {
            _released.TrySetResult();
        }
    }
}

/// <summary>
/// 一次调用持有的租约，释放时归还给实例。重复释放无副作用。
/// </summary>
internal sealed class PluggableLease(PluggableInstance instance) : IDisposable
{
    /// <summary>
    /// 是否已归还。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 本次调用使用的实例。
    /// </summary>
    public PluggableInstance Instance { get; } = instance;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Instance.Release();
        }
    }
}
