using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Helpers;

public abstract class CacheHelper
{
    public class LruCache<TKey, TValue> where TKey : notnull where TValue : class
    {
        private readonly int _capacity;
        private readonly TimeSpan? _expireAfter;
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, DateTime ExpireAt)>> _cacheMap;
        private readonly LinkedList<(TKey Key, TValue Value, DateTime ExpireAt)> _lruList;
        private readonly object _lock = new();

        public LruCache(int capacity, TimeSpan? expireAfter = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
            _expireAfter = expireAfter;
            _cacheMap = new Dictionary<TKey, LinkedListNode<(TKey, TValue, DateTime)>>();
            _lruList = [];
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    if (_expireAfter.HasValue && DateTime.UtcNow > node.Value.ExpireAt)
                    {
                        _lruList.Remove(node);
                        _cacheMap.Remove(key);
                        value = null;
                        return false;
                    }
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
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
                var expireAt = _expireAfter.HasValue ? DateTime.UtcNow.Add(_expireAfter.Value) : default;

                if (_cacheMap.TryGetValue(key, out var node))
                {
                    node.Value = (key, value, expireAt);
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
                    var newNode = new LinkedListNode<(TKey, TValue, DateTime)>((key, value, expireAt));
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
}
