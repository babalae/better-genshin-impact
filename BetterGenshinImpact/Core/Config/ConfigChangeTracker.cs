using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 递归追踪配置对象图中所有 <see cref="INotifyPropertyChanged"/> / <see cref="INotifyCollectionChanged"/> 节点的变更。
/// <para>
/// 订阅是增量且幂等的：
/// 1. 已访问对象记录在 <see cref="ConditionalWeakTable{TKey,TValue}"/> 中，同一对象只订阅一次，也天然防环，且不阻止 GC；
/// 2. 属性被整体替换为新对象、集合新增元素时，只补订阅新出现的对象；
/// 3. 被替换掉的旧对象不主动退订，它们之后再触发事件也只是多一次无害的变更回调。
/// </para>
/// <para>
/// 不做全量重扫：根节点上的子配置是无通知的自动属性，运行时可能被临时替换（如地脉花任务临时替换 AutoFightConfig），
/// 全量重扫会把追踪挂到临时对象上。
/// </para>
/// 回调在触发事件的线程上同步执行。
/// </summary>
internal sealed class ConfigChangeTracker
{
    /// <summary>
    /// 递归深度上限，防御异常的对象图
    /// </summary>
    private const int MaxDepth = 32;

    private static readonly object Marker = new();

    /// <summary>
    /// 每个类型可继续向下追踪的属性（按属性名索引）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> TrackablePropertyCache = new();

    private readonly ConditionalWeakTable<object, object> _tracked = new();
    private readonly object _trackedLocker = new();
    private readonly Action<object> _onChanged;

    /// <param name="onChanged">任意节点变更时回调，参数为触发事件的对象（属性所属对象或集合本身）</param>
    public ConfigChangeTracker(Action<object> onChanged)
    {
        _onChanged = onChanged;
    }

    /// <summary>
    /// 从根节点开始追踪
    /// </summary>
    public void Track(object root)
    {
        Visit(root, 0);
    }

    private void Visit(object? node, int depth)
    {
        if (node == null || depth > MaxDepth || !IsTrackableInstance(node))
        {
            return;
        }

        lock (_trackedLocker)
        {
            if (_tracked.TryGetValue(node, out _))
            {
                return;
            }

            _tracked.Add(node, Marker);
        }

        if (node is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += OnPropertyChanged;
        }

        if (node is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnCollectionChanged;
        }

        VisitChildren(node, depth);
    }

    private void VisitChildren(object node, int depth)
    {
        VisitItems(node, depth);

        if (node is not INotifyPropertyChanged)
        {
            // 只沿着可通知对象向下展开属性，普通 POCO（如 GiTpPosition）没有变更通知，且可能存在会抛异常的计算属性
            return;
        }

        foreach (var property in GetTrackableProperties(node.GetType()).Values)
        {
            Visit(TryGetValue(property, node), depth + 1);
        }
    }

    private void VisitItems(object node, int depth)
    {
        if (node is string)
        {
            return;
        }

        try
        {
            switch (node)
            {
                case IDictionary dictionary:
                    // 先拷贝一份，缩短与其他线程并发修改的冲突窗口
                    foreach (var value in dictionary.Values.Cast<object?>().ToArray())
                    {
                        Visit(value, depth + 1);
                    }

                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable.Cast<object?>().ToArray())
                    {
                        Visit(item, depth + 1);
                    }

                    break;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // 其他线程正在修改集合，尽力而为：新元素会在下一次 CollectionChanged 或属性替换时补订阅
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName))
        {
            // 约定：属性名为空表示所有属性都已变化
            VisitChildren(sender, 0);
        }
        else if (GetTrackableProperties(sender.GetType()).TryGetValue(e.PropertyName, out var property))
        {
            // 子对象可能被整体替换，补订阅新值
            Visit(TryGetValue(property, sender), 0);
        }

        _onChanged(sender);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender == null)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            VisitItems(sender, 0);
        }
        else if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                Visit(item, 0);
            }
        }

        _onChanged(sender);
    }

    private static object? TryGetValue(PropertyInfo property, object owner)
    {
        try
        {
            return property.GetValue(owner);
        }
        catch
        {
            // 计算属性可能抛异常（如依赖未初始化的数据），跳过即可
            return null;
        }
    }

    /// <summary>
    /// 只追踪可能产生变更通知或包含可通知元素的引用类型实例
    /// </summary>
    private static bool IsTrackableInstance(object node)
    {
        if (node is string or Delegate or MemberInfo || node.GetType().IsValueType)
        {
            return false;
        }

        return node is INotifyPropertyChanged or INotifyCollectionChanged or IEnumerable;
    }

    private static Dictionary<string, PropertyInfo> GetTrackableProperties(Type type)
    {
        return TrackablePropertyCache.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsTrackableProperty)
            // 派生类用 new 隐藏基类属性时会出现同名属性，取第一个（最派生）即可
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
    }

    private static bool IsTrackableProperty(PropertyInfo property)
    {
        if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        var propertyType = property.PropertyType;
        if (propertyType.IsValueType || propertyType == typeof(string) || typeof(Delegate).IsAssignableFrom(propertyType))
        {
            return false;
        }

        // 不参与序列化的属性不属于配置内容
        var jsonIgnore = property.GetCustomAttribute<JsonIgnoreAttribute>();
        return jsonIgnore is not { Condition: JsonIgnoreCondition.Always };
    }
}
