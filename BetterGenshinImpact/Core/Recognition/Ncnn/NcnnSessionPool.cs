using System;
using System.Collections.Generic;
using System.Threading;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     单个用途、单个形状桶的 ncnn 会话池：持有 1–<see cref="NcnnTuningSettings.MaxParallelSessions" /> 个
///     <c>ncnn::Net</c> 句柄，按需创建、用后归还，供 <c>Parallel.ForEach</c> 并行推理。
///     <para>
///         默认并行数为 1，此时行为与历史单会话完全一致；只有配置大于 1 才会创建额外会话。
///         所有会话共享同一个 <see cref="NcnnShim" />（库句柄在进程内缓存，只有 net 句柄按会话区分）。
///     </para>
/// </summary>
internal sealed class NcnnSessionPool : IDisposable
{
    private readonly NcnnShim _shim;
    private readonly NcnnNetOptions _template;
    private readonly int _maxSessions;
    private readonly SemaphoreSlim _gate;
    private readonly object _createLock = new();
    private readonly List<nint> _owned = [];
    private readonly Stack<nint> _idle = [];
    private bool _disposed;

    public NcnnSessionPool(NcnnShim shim, NcnnNetOptions template, int maxSessions)
    {
        _shim = shim ?? throw new ArgumentNullException(nameof(shim));
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _maxSessions = Math.Clamp(maxSessions, 1, NcnnTuningSettings.MaxParallelSessions);
        _gate = new SemaphoreSlim(_maxSessions, _maxSessions);
    }

    /// <summary>已创建的会话数（用于诊断展示）。</summary>
    public int CreatedCount
    {
        get
        {
            lock (_createLock) return _owned.Count;
        }
    }

    public int MaxSessions => _maxSessions;

    /// <summary>租用一个会话；并行数用尽时阻塞等待归还。</summary>
    public nint Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.Wait();
        try
        {
            lock (_createLock)
            {
                if (_idle.Count > 0) return _idle.Pop();
            }

            return CreateSession();
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    /// <summary>归还会话；已释放的池会直接销毁句柄。</summary>
    public void Return(nint net)
    {
        if (net == nint.Zero)
        {
            _gate.Release();
            return;
        }

        // 池释放时已统一销毁全部句柄（含租出中的），归还路径不能再销毁一次。
        if (!_disposed)
        {
            lock (_createLock) _idle.Push(net);
        }

        _gate.Release();
    }

    /// <summary>在租约内执行一次同步操作，保证异常路径也会归还会话。</summary>
    public T Use<T>(Func<nint, T> action)
    {
        var net = Rent();
        try
        {
            return action(net);
        }
        finally
        {
            Return(net);
        }
    }

    private nint CreateSession()
    {
        var net = _shim.Create(_template);
        if (net == nint.Zero)
            throw new InvalidOperationException(
                $"ncnn 会话创建失败（{System.IO.Path.GetFileName(_template.ParamPath)}，并行上限 {_maxSessions}）");

        lock (_createLock) _owned.Add(net);
        return net;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        List<nint> owned;
        lock (_createLock)
        {
            owned = [.. _owned];
            _owned.Clear();
            _idle.Clear();
        }

        foreach (var net in owned)
        {
            if (net != nint.Zero) _shim.Destroy(net);
        }

        // 故意不 Dispose 信号量：租约可能在池释放后归还，Release 需要它仍然可用（SemaphoreSlim 无句柄，随 GC 回收）。
    }
}
