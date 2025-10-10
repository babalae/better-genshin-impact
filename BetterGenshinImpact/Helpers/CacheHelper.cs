using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BetterGenshinImpact.Helpers;

public abstract class CacheHelper
{
    public class LruCache<TKey, TValue> where TKey : class where TValue : class
    {
        // 缓存容量上限
        private readonly int _capacity;

        // 可选的全局过期时间
        private readonly TimeSpan? _expireAfter;

        // 是否对Key使用弱引用
        private readonly bool _weakKey;

        // 是否对Value使用弱引用
        private readonly bool _weakValue;

        // 主缓存字典，key为TKey或WeakReference<TKey>
        private readonly Dictionary<object, LinkedListNode<CacheItem>> _cacheMap;

        // LRU链表，头部为最新，尾部为最旧
        private readonly LinkedList<CacheItem> _lruList;

        // 线程安全锁
        private readonly object _lock = new();

        // 弱引用Key辅助表
        private readonly ConditionalWeakTable<object, object>? _weakKeyTable;

        // 缓存项结构，支持过期和弱引用
        private class CacheItem
        {
            // Key的引用（TKey或WeakReference<TKey>）
            public required object KeyRef;

            // Value的引用（TValue或WeakReference<TValue>）
            public object? ValueRef;

            // 过期时间
            public DateTime ExpireAt;

            // 获取强引用Key
            public TKey? StrongKey => KeyRef as TKey ??
                                      ((KeyRef as WeakReference<TKey>)?.TryGetTarget(out var t) == true ? t : null);

            // 获取强引用Value
            public TValue? StrongValue => ValueRef as TValue ??
                                          ((ValueRef as WeakReference<TValue>)?.TryGetTarget(out var t) == true
                                              ? t
                                              : null);
        }

        /// <summary>
        /// 构造LRU缓存
        /// </summary>
        /// <param name="capacity">最大容量</param>
        /// <param name="expireAfter">全局过期时间</param>
        /// <param name="weakKey">是否弱引用Key</param>
        /// <param name="weakValue">是否弱引用Value</param>
        public LruCache(int capacity, TimeSpan? expireAfter = null, bool weakKey = false, bool weakValue = false)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _capacity = capacity;
            _expireAfter = expireAfter;
            _weakKey = weakKey;
            _weakValue = weakValue;
            _cacheMap = new Dictionary<object, LinkedListNode<CacheItem>>();
            _lruList = [];
            if (weakKey) _weakKeyTable = new ConditionalWeakTable<object, object>();
        }

        // 包装Key为弱引用或强引用
        private object WrapKey(TKey key)
        {
            if (!_weakKey) return key;
            var weak = new WeakReference<TKey>(key);
            _weakKeyTable!.AddOrUpdate(weak, key);
            return weak;
        }

        // 包装Value为弱引用或强引用
        private object WrapValue(TValue value) => _weakValue ? new WeakReference<TValue>(value) : value;

        // 判断缓存项是否过期
        private bool IsExpired(CacheItem item) => _expireAfter.HasValue && DateTime.UtcNow > item.ExpireAt;

        /// <summary>
        /// 获取缓存项，若不存在或已过期返回false
        /// </summary>
        public bool TryGet(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                var mapKey = _weakKey
                    ? _cacheMap.Keys.FirstOrDefault(k =>
                        (k as WeakReference<TKey>)?.TryGetTarget(out var t) == true &&
                        EqualityComparer<TKey>.Default.Equals(t, key))
                    : key;
                if (mapKey != null && _cacheMap.TryGetValue(mapKey, out var node))
                {
                    var item = node.Value;
                    if (IsExpired(item) || (_weakKey && item.StrongKey == null) ||
                        (_weakValue && item.StrongValue == null))
                    {
                        _lruList.Remove(node);
                        _cacheMap.Remove(mapKey);
                        value = null;
                        return false;
                    }

                    // 命中则移到链表头
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = item.StrongValue!;
                    return true;
                }

                value = null;
                return false;
            }
        }

        /// <summary>
        /// 设置缓存项，若已存在则更新并移到头部
        /// </summary>
        public void Set(TKey key, TValue value)
        {
            lock (_lock)
            {
                var mapKey = _weakKey
                    ? _cacheMap.Keys.FirstOrDefault(k =>
                        (k as WeakReference<TKey>)?.TryGetTarget(out var t) == true &&
                        EqualityComparer<TKey>.Default.Equals(t, key))
                    : key;
                if (mapKey != null && _cacheMap.TryGetValue(mapKey, out var node))
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
                        // 淘汰最久未使用项
                        var lru = _lruList.Last;
                        if (lru != null)
                        {
                            _cacheMap.Remove(lru.Value.KeyRef);
                            _lruList.RemoveLast();
                        }
                    }

                    var item = new CacheItem
                    {
                        KeyRef = WrapKey(key),
                        ValueRef = WrapValue(value),
                        ExpireAt = _expireAfter.HasValue ? DateTime.UtcNow.Add(_expireAfter.Value) : DateTime.MaxValue
                    };
                    var newNode = new LinkedListNode<CacheItem>(item);
                    _lruList.AddFirst(newNode);
                    _cacheMap[item.KeyRef] = newNode;
                }
            }
        }

        /// <summary>
        /// 移除指定Key的缓存项
        /// </summary>
        public bool Remove(TKey key)
        {
            lock (_lock)
            {
                var mapKey = _weakKey
                    ? _cacheMap.Keys.FirstOrDefault(k =>
                        (k as WeakReference<TKey>)?.TryGetTarget(out var t) == true &&
                        EqualityComparer<TKey>.Default.Equals(t, key))
                    : key;
                if (mapKey == null || !_cacheMap.TryGetValue(mapKey, out var node)) return false;
                _lruList.Remove(node);
                _cacheMap.Remove(mapKey);
                return true;
            }
        }

        /// <summary>
        /// 当前缓存项数量
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cacheMap.Count;
                }
            }
        }

        /// <summary>
        /// 清空所有缓存项
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cacheMap.Clear();
                _lruList.Clear();
            }
        }
    }

    /// <summary>;slsl
    /// LruCache构建器，支持链式配置
    /// </summary>
    public class LruCacheBuilder<TKey, TValue> where TKey : class where TValue : class
    {
        private int _capacity = 128;
        private TimeSpan? _expireAfter;
        private bool _weakKey;
        private bool _weakValue;

        /// <summary>设置容量</summary>
        public LruCacheBuilder<TKey, TValue> Capacity(int capacity)
        {
            _capacity = capacity;
            return this;
        }

        /// <summary>设置全局过期时间</summary>
        public LruCacheBuilder<TKey, TValue> ExpireAfter(TimeSpan expire)
        {
            _expireAfter = expire;
            return this;
        }

        /// <summary>启用弱引用Key</summary>
        public LruCacheBuilder<TKey, TValue> WeakKey()
        {
            _weakKey = true;
            return this;
        }

        /// <summary>启用弱引用Value</summary>
        public LruCacheBuilder<TKey, TValue> WeakValue()
        {
            _weakValue = true;
            return this;
        }

        /// <summary>构建LruCache实例</summary>
        public LruCache<TKey, TValue> Build() => new(_capacity, _expireAfter, _weakKey, _weakValue);
    }
}