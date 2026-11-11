using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 一项可插拔能力的“槽”：记录已登记的实现、用户的选择、创建失败的实现和存活的实例。
/// 切换实现时旧实例退役，进行中的调用在旧实例上完成后才释放它。
/// </summary>
internal sealed class PluggableSlot
{
    /// <summary>
    /// 所属注册表，用于创建实例和报告失败。
    /// </summary>
    private readonly PluggableRegistry _registry;

    /// <summary>
    /// 保护下面的全部状态。只做内存操作，不在锁内创建或释放实例。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 串行化实例创建：同一时刻只加载一个模型，并发调用等待同一个实例。
    /// </summary>
    private readonly object _createGate = new();

    /// <summary>
    /// 已登记的实现，内置实现在构造时加入，插件实现在启用时加入。
    /// </summary>
    private readonly List<PluggableImplDescriptor> _implementations;

    /// <summary>
    /// 存活的实例，按实现标识索引。
    /// </summary>
    private readonly Dictionary<string, PluggableInstance> _instances = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建实例失败的实现。用户重新选择或重新启用插件之前不再尝试，改用默认实现。
    /// </summary>
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);

    /// <summary>
    /// 用户保存的选择，没有保存时为 null。
    /// </summary>
    private string? _selectedId;

    /// <summary>
    /// 不固定实现的代理最近一次使用的实现，换用其他实现时让它的实例退役。
    /// </summary>
    private string? _activeId;

    /// <summary>
    /// 状态版本号。创建实例期间状态发生变化时丢弃新实例，重新解析。
    /// </summary>
    private int _version;

    /// <summary>
    /// 创建槽。
    /// </summary>
    /// <param name="registry">所属注册表。</param>
    /// <param name="descriptor">可插拔能力。</param>
    /// <param name="builtins">内置实现，恰好一个是默认实现。</param>
    /// <param name="selectedId">配置中保存的选择。</param>
    public PluggableSlot(PluggableRegistry registry, PluggableDescriptor descriptor,
        IReadOnlyList<PluggableImplDescriptor> builtins, string? selectedId)
    {
        _registry = registry;
        Descriptor = descriptor;
        _implementations = builtins.ToList();
        Default = _implementations.Single(i => i.IsDefault);
        _selectedId = string.IsNullOrEmpty(selectedId) ? null : selectedId;
    }

    /// <summary>
    /// 可插拔能力。
    /// </summary>
    public PluggableDescriptor Descriptor { get; }

    /// <summary>
    /// 默认实现。
    /// </summary>
    public PluggableImplDescriptor Default { get; }

    /// <summary>
    /// 当前可用的实现：内置在前（默认实现第一个），插件实现按插件名称排列。
    /// </summary>
    public IReadOnlyList<PluggableImplDescriptor> GetImplementations()
    {
        lock (_gate)
        {
            return _implementations
                .OrderBy(i => i.IsBuiltin ? 0 : 1)
                .ThenBy(i => i.IsDefault ? 0 : 1)
                .ThenBy(i => i.PluginName, StringComparer.CurrentCulture)
                .ThenBy(i => i.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }
    }

    /// <summary>
    /// 是否已登记指定实现。
    /// </summary>
    public bool Contains(string implementationId)
    {
        lock (_gate)
        {
            return FindLocked(implementationId) is not null;
        }
    }

    /// <summary>
    /// 当前的选择状态。
    /// </summary>
    public PluggableSelectionState GetSelection()
    {
        lock (_gate)
        {
            var saved = _selectedId is null ? null : FindLocked(_selectedId);
            var effective = ResolveLocked(null);
            return new PluggableSelectionState(
                Descriptor,
                _selectedId,
                saved,
                effective,
                _selectedId is not null && effective.Id != _selectedId,
                saved is not null && _failed.Contains(saved.Id));
        }
    }

    /// <summary>
    /// 更改选择，并清除该实现的失败记录。下一次调用时生效。
    /// </summary>
    public void Select(string implementationId)
    {
        lock (_gate)
        {
            _failed.Remove(implementationId);
            _selectedId = implementationId;
            _version++;
        }
    }

    /// <summary>
    /// 所有实例退役，下一次调用重新创建。
    /// </summary>
    public void Reset()
    {
        List<PluggableInstance> retired;
        lock (_gate)
        {
            retired = _instances.Values.ToList();
            _instances.Clear();
            _activeId = null;
            _version++;
        }

        foreach (var instance in retired)
        {
            instance.Retire();
        }
    }

    /// <summary>
    /// 登记插件提供的实现，并清除它们之前的失败记录。调用方已检查标识不重复。
    /// </summary>
    public void Add(IReadOnlyList<PluggableImplDescriptor> implementations)
    {
        lock (_gate)
        {
            _implementations.AddRange(implementations);
            foreach (var implementation in implementations)
            {
                _failed.Remove(implementation.Id);
            }

            _version++;
        }
    }

    /// <summary>
    /// 移除指定插件的全部实现，并让它们的实例退役。
    /// </summary>
    /// <returns>没有该插件的实现时返回 null；否则返回各实例释放完成的任务。</returns>
    public IReadOnlyList<Task>? RemovePlugin(string pluginId)
    {
        List<PluggableInstance> retired = [];
        lock (_gate)
        {
            var removed = _implementations.RemoveAll(i => i.PluginId == pluginId);
            foreach (var (id, instance) in _instances.ToList())
            {
                if (instance.Implementation.PluginId == pluginId)
                {
                    _instances.Remove(id);
                    retired.Add(instance);
                }
            }

            if (removed == 0 && retired.Count == 0)
            {
                return null;
            }

            _failed.RemoveWhere(id => id.StartsWith(pluginId + ".", StringComparison.Ordinal));
            _version++;
        }

        foreach (var instance in retired)
        {
            instance.Retire();
        }

        return retired.Select(i => i.Released).ToList();
    }

    /// <summary>
    /// 为一次调用申请租约：按选择确定实现，没有实例时创建实例。
    /// 插件实现创建失败时记为失败并改用默认实现；默认实现创建失败时异常照常抛出。
    /// </summary>
    /// <param name="pinnedId">固定使用的实现标识，null 表示按用户选择。</param>
    public PluggableLease Acquire(string? pinnedId)
    {
        while (true)
        {
            // 1. 在锁内解析实现，已有实例时直接取得租约
            PluggableImplDescriptor implementation;
            PluggableInstance? replaced = null;
            PluggableLease? lease = null;
            int version;
            lock (_gate)
            {
                implementation = ResolveLocked(pinnedId);
                if (pinnedId is null && _activeId != implementation.Id)
                {
                    // 换用了新实现：上一个实现的实例退役，进行中的调用结束后释放
                    if (_activeId is not null && _instances.Remove(_activeId, out var previous))
                    {
                        replaced = previous;
                    }

                    _activeId = implementation.Id;
                }

                if (_instances.TryGetValue(implementation.Id, out var existing))
                {
                    lease = existing.TryAcquire();
                }

                version = _version;
            }

            replaced?.Retire();
            if (lease is not null)
            {
                return lease;
            }

            // 2. 串行创建实例。创建可能耗时（加载模型），期间不持有状态锁
            Exception? failure = null;
            lock (_createGate)
            {
                lock (_gate)
                {
                    if (_version != version)
                    {
                        continue;
                    }

                    // 等待创建锁期间，其他调用可能已经创建好实例
                    if (_instances.TryGetValue(implementation.Id, out var created) && created.TryAcquire() is { } createdLease)
                    {
                        return createdLease;
                    }
                }

                PluggableInstance? instance = null;
                try
                {
                    instance = new PluggableInstance(implementation, _registry.CreateInstance(implementation, []), _registry.Logger);
                }
                catch (Exception e) when (!implementation.IsDefault)
                {
                    lock (_gate)
                    {
                        _failed.Add(implementation.Id);
                        _version++;
                    }

                    failure = e;
                }

                if (instance is not null)
                {
                    lock (_gate)
                    {
                        if (_version == version && ResolveLocked(pinnedId).Id == implementation.Id)
                        {
                            _instances[implementation.Id] = instance;
                            return instance.TryAcquire()!;
                        }
                    }

                    // 创建期间实现被移除或选择发生变化：丢弃新实例，重新解析
                    instance.Retire();
                }
            }

            // 3. 在锁外报告失败（会触发界面刷新），然后按回退后的选择重试
            if (failure is not null)
            {
                _registry.OnCreateFailed(implementation, failure);
            }
        }
    }

    /// <summary>
    /// 按当前选择创建一个独立实例，不登记到槽中。创建失败时异常照常抛出，不影响全局选择。
    /// </summary>
    /// <param name="args">补充或覆盖的构造参数。</param>
    public object CreateDetached(object[] args)
    {
        PluggableImplDescriptor implementation;
        lock (_gate)
        {
            implementation = ResolveLocked(null);
        }

        try
        {
            return _registry.CreateInstance(implementation, args);
        }
        catch (Exception e)
        {
            _registry.OnCallFailed(implementation, e);
            throw;
        }
    }

    /// <summary>
    /// 解析实际使用的实现：指定或保存的实现可用且没有失败时用它，否则用默认实现。
    /// </summary>
    private PluggableImplDescriptor ResolveLocked(string? requestedId)
    {
        var id = requestedId ?? _selectedId;
        if (id is not null && !_failed.Contains(id) && FindLocked(id) is { } found)
        {
            return found;
        }

        return Default;
    }

    /// <summary>
    /// 按标识查找已登记的实现。
    /// </summary>
    private PluggableImplDescriptor? FindLocked(string implementationId)
        => _implementations.FirstOrDefault(i => i.Id == implementationId);
}
