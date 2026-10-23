using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     按形状桶懒加载的会话缓存：同桶复用、异桶各建（pnnx 静态形状要求每个桶一份模型）。
///     统一 det / rec 各自的「字典 + 锁 + 逐桶 Dispose」样板。
/// </summary>
internal sealed class NcnnBucketCache<T> : IDisposable where T : IDisposable
{
    private readonly Dictionary<int, T> _items = new();
    private readonly object _lock = new();
    private readonly Func<int, T> _factory;
    private bool _disposed;

    public NcnnBucketCache(Func<int, T> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>取指定桶的会话；不存在时用工厂创建（工厂在锁内调用，桶模型缺失抛出的异常不会缓存）。</summary>
    public T GetOrCreate(int bucket)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(bucket, out var item)) return item;
            item = _factory(bucket);
            _items[bucket] = item;
            return item;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var item in _items.Values) item.Dispose();
            _items.Clear();
        }
    }
}
