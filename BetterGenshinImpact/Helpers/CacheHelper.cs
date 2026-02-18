using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Helpers;

public abstract class CacheHelper
{
    public class LruCache<TKey, TValue> where TKey : class where TValue : class
    {
        private readonly int _capacity;
        private readonly TimeSpan? _expireAfter;
        private readonly bool _weakValue;
        private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cacheMap;
        private readonly LinkedList<CacheItem> _lruList;
        private readonly object _lock = new();

        private class CacheItem
        {
            public required TKey Key;
            public object? ValueRef;
            public DateTime ExpireAt;
            public TValue? StrongValue => ValueRef as TValue ??
                                          ((ValueRef as WeakReference<TValue>)?.TryGetTarget(out var t) == true
                                              ? t
                                              : null);
        }

        public LruCache(int capacity, TimeSpan? expireAfter = null, bool weakValue = false)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
            _expireAfter = expireAfter;
            _weakValue = weakValue;
            _cacheMap = new Dictionary<TKey, LinkedListNode<CacheItem>>();
            _lruList = [];
        }

        private object WrapValue(TValue value) => _weakValue ? new WeakReference<TValue>(value) : value;
        private bool IsExpired(CacheItem item) => _expireAfter.HasValue && DateTime.UtcNow > item.ExpireAt;

        public bool TryGet(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    var item = node.Value;
                    if (IsExpired(item) || (_weakValue && item.StrongValue == null))
                    {
                        _lruList.Remove(node);
                        _cacheMap.Remove(key);
                        value = null;
                        return false;
                    }
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = item.StrongValue!;
                    return true;
                }
                value = null;
                return false;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    node.Value.ValueRef = WrapValue(value);
                    node.Value.ExpireAt = _expireAfter.HasValue
                        ? DateTime.UtcNow.Add(_expireAfter.Value)
                        : DateTime.MaxValue;
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                }
                else
                {
                    if (_cacheMap.Count >= _capacity)
                    {
                        var lru = _lruList.Last;
                        if (lru != null)
                        {
                            _cacheMap.Remove(lru.Value.Key);
                            _lruList.RemoveLast();
                        }
                    }
                    var item = new CacheItem
                    {
                        Key = key,
                        ValueRef = WrapValue(value),
                        ExpireAt = _expireAfter.HasValue ? DateTime.UtcNow.Add(_expireAfter.Value) : DateTime.MaxValue
                    };
                    var newNode = new LinkedListNode<CacheItem>(item);
                    _lruList.AddFirst(newNode);
                    _cacheMap[key] = newNode;
                }
            }
        }

        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                if (!_cacheMap.TryGetValue(key, out var node)) return false;
                _lruList.Remove(node);
                _cacheMap.Remove(key);
                return true;
            }
        }

        public int Count
        {
            get { lock (_lock) { return _cacheMap.Count; } }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cacheMap.Clear();
                _lruList.Clear();
            }
        }
    }

    public class LruCacheBuilder<TKey, TValue> where TKey : class where TValue : class
    {
        private int _capacity = 128;
        private TimeSpan? _expireAfter;
        private bool _weakValue;

        public LruCacheBuilder<TKey, TValue> Capacity(int capacity) { _capacity = capacity; return this; }
        public LruCacheBuilder<TKey, TValue> ExpireAfter(TimeSpan expire) { _expireAfter = expire; return this; }
        public LruCacheBuilder<TKey, TValue> WeakValue() { _weakValue = true; return this; }
        public LruCache<TKey, TValue> Build() => new(_capacity, _expireAfter, _weakValue);
    }
}
