using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 可插拔能力注册表：保存全部可插拔能力、已登记的实现和用户的选择，为每个可插拔能力提供代理。
/// 内置实现在构造时扫描主程序集登记；插件实现由 PluginManager 在启用插件时登记、停用时移除。
/// </summary>
/// <remarks>
/// 宿主内部类型，不承诺兼容，插件不要使用；插件和业务代码通过 <see cref="Pluggable"/> 操作。
/// 任意线程都可以读取和调用；登记、移除实现只由 PluginManager 在界面线程进行。
/// </remarks>
public sealed class PluggableRegistry
{
    /// <summary>
    /// 按接口索引的槽。
    /// </summary>
    private readonly Dictionary<Type, PluggableSlot> _slotsByType = [];

    /// <summary>
    /// 按可插拔能力标识索引的槽。
    /// </summary>
    private readonly Dictionary<string, PluggableSlot> _slotsById = new(StringComparer.Ordinal);

    /// <summary>
    /// 已创建的代理，按接口和固定实现标识索引。
    /// </summary>
    private readonly ConcurrentDictionary<(Type Contract, string? ImplementationId), object> _proxies = new();

    /// <summary>
    /// 保存选择的配置。
    /// </summary>
    private readonly PluginConfig _config;

    /// <summary>
    /// 日志。注册表在服务容器构建前创建，第一次使用时再取得。
    /// </summary>
    private ILogger? _logger;

    /// <summary>
    /// 主程序的服务容器，第一次创建实例时再取得。
    /// </summary>
    private IServiceProvider? _services;

    /// <summary>
    /// 扫描主程序集中的可插拔能力和内置实现，并读取保存的选择。
    /// </summary>
    /// <param name="assemblies">主程序集。</param>
    /// <param name="config">插件配置。</param>
    /// <exception cref="InvalidOperationException">可插拔能力或实现的声明不符合规则。</exception>
    public PluggableRegistry(IEnumerable<Assembly> assemblies, PluginConfig config)
    {
        _config = config;
        var assemblyList = assemblies.ToList();
        var descriptors = PluggableScanner.ScanContracts(assemblyList);
        var contracts = descriptors.ToDictionary(d => d.ContractType);
        var builtins = assemblyList
            .SelectMany(a => PluggableScanner.ScanImplementations(a, contracts, null, null, null))
            .ToList();
        var saved = config.Selections
            .Where(s => !string.IsNullOrEmpty(s.PluggableId))
            .GroupBy(s => s.PluggableId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().ImplementationId, StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            var implementations = builtins.Where(i => i.PluggableId == descriptor.Id).ToList();
            var defaultCount = implementations.Count(i => i.IsDefault);
            if (defaultCount != 1)
            {
                throw new InvalidOperationException($"可插拔能力 {descriptor.Id} 必须恰好有一个默认实现，当前有 {defaultCount} 个。");
            }

            if (implementations.GroupBy(i => i.Id).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            {
                throw new InvalidOperationException($"可插拔实现标识 {duplicate.Key} 重复。");
            }

            var slot = new PluggableSlot(this, descriptor, implementations, saved.GetValueOrDefault(descriptor.Id));
            _slotsByType[descriptor.ContractType] = slot;
            _slotsById[descriptor.Id] = slot;
        }

        All = descriptors.OrderBy(d => d.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }

    /// <summary>
    /// 全部可插拔能力，按标识排列。
    /// </summary>
    public IReadOnlyList<PluggableDescriptor> All { get; }

    /// <summary>
    /// 实现增减、选择变化、实例重建或创建失败时触发，在发起变化的线程上调用。
    /// </summary>
    public event EventHandler<PluggableChangedEventArgs>? Changed;

    /// <summary>
    /// 插件实现出现二进制不兼容时触发，在出错的线程上调用，供 PluginManager 停用该插件。
    /// </summary>
    internal event EventHandler<PluggableIncompatibleEventArgs>? Incompatible;

    /// <summary>
    /// 日志。默认取主程序的日志，可在测试中替换。
    /// </summary>
    internal ILogger Logger
    {
        get => _logger ??= App.GetLogger<PluggableRegistry>();
        set => _logger = value;
    }

    /// <summary>
    /// 解析内置实现构造参数的服务容器。默认取主程序的服务容器，可在测试中替换。
    /// </summary>
    internal IServiceProvider Services
    {
        get => _services ??= App.ServiceProvider;
        set => _services = value;
    }

    /// <summary>
    /// 把每个可插拔能力接口注册为单例代理，服务容器中的类可以直接注入接口。
    /// </summary>
    public void RegisterProxies(IServiceCollection services)
    {
        foreach (var descriptor in All)
        {
            var contract = descriptor.ContractType;
            services.AddSingleton(contract, _ => GetProxy(contract));
        }
    }

    /// <summary>
    /// 获取可插拔能力的代理。
    /// </summary>
    public object GetProxy(Type contract) => GetProxy(contract, null);

    /// <summary>
    /// 获取可插拔能力的代理。
    /// </summary>
    public T GetProxy<T>() where T : class => (T)GetProxy(typeof(T), null);

    /// <summary>
    /// 获取固定使用指定实现的代理；该实现不可用时回退到默认实现。
    /// </summary>
    public T GetProxy<T>(string implementationId) where T : class => (T)GetProxy(typeof(T), implementationId);

    /// <summary>
    /// 当前可用的实现：内置在前，插件实现按插件名称排列。
    /// </summary>
    public IReadOnlyList<PluggableImplDescriptor> GetImplementations(string pluggableId)
        => GetSlot(pluggableId).GetImplementations();

    /// <summary>
    /// 当前的选择状态。
    /// </summary>
    public PluggableSelectionState GetSelection(string pluggableId) => GetSlot(pluggableId).GetSelection();

    /// <summary>
    /// 当前的选择状态。
    /// </summary>
    public PluggableSelectionState GetSelection<T>() where T : class => GetSlot(typeof(T)).GetSelection();

    /// <summary>
    /// 按接口取得可插拔能力的描述，供功能自己的设置界面创建选择控件。
    /// </summary>
    public PluggableDescriptor GetDescriptor<T>() where T : class => GetSlot(typeof(T)).Descriptor;

    /// <summary>
    /// 更改选择并保存到配置。下一次调用生效，旧实例在进行中的调用结束后释放。应在界面线程调用。
    /// </summary>
    /// <exception cref="ArgumentException">实现当前未登记。</exception>
    public void Select(string pluggableId, string implementationId)
    {
        var slot = GetSlot(pluggableId);
        if (!slot.Contains(implementationId))
        {
            throw new ArgumentException($"实现 {implementationId} 当前不可用。", nameof(implementationId));
        }

        slot.Select(implementationId);
        var existing = _config.Selections.FirstOrDefault(s => s.PluggableId == pluggableId);
        if (existing is null)
        {
            _config.Selections.Add(new PluggableSelection { PluggableId = pluggableId, ImplementationId = implementationId });
        }
        else
        {
            existing.ImplementationId = implementationId;
        }

        Logger.LogInformation("{Pluggable} 改用 {Implementation}", slot.Descriptor.DisplayName, implementationId);
        RaiseChanged(pluggableId);
    }

    /// <summary>
    /// 让当前实例在进行中的调用结束后释放，下一次调用重新创建。
    /// </summary>
    public void Reset<T>() where T : class
    {
        var slot = GetSlot(typeof(T));
        slot.Reset();
        RaiseChanged(slot.Descriptor.Id);
    }

    /// <summary>
    /// 按当前选择创建一个独立实例，调用方负责释放。
    /// </summary>
    /// <param name="args">补充或覆盖的构造参数。</param>
    public T CreateDetached<T>(object[] args) where T : class => (T)GetSlot(typeof(T)).CreateDetached(args);

    /// <summary>
    /// 扫描插件程序集并登记其中的实现。先整体校验再登记，校验失败时不登记任何实现。
    /// </summary>
    /// <param name="pluginId">插件标识，作为实现标识的前缀。</param>
    /// <param name="pluginName">插件名称，用于显示来源。</param>
    /// <param name="assembly">插件入口程序集。</param>
    /// <param name="services">插件的服务容器。</param>
    /// <returns>登记的实现。</returns>
    /// <exception cref="InvalidOperationException">实现声明不符合规则或标识重复。</exception>
    internal IReadOnlyList<PluggableImplDescriptor> AddPluginAssembly(string pluginId, string pluginName, Assembly assembly,
        IServiceProvider services)
    {
        var contracts = _slotsByType.ToDictionary(kv => kv.Key, kv => kv.Value.Descriptor);
        var implementations = PluggableScanner.ScanImplementations(assembly, contracts, pluginId, pluginName, services);
        var groups = implementations.GroupBy(i => i.PluggableId).ToList();
        foreach (var implementation in implementations)
        {
            if (implementations.Count(i => i.Id == implementation.Id) > 1 || _slotsById[implementation.PluggableId].Contains(implementation.Id))
            {
                throw new InvalidOperationException($"可插拔实现标识 {implementation.Id} 重复。");
            }
        }

        foreach (var group in groups)
        {
            _slotsById[group.Key].Add(group.ToList());
            RaiseChanged(group.Key);
        }

        return implementations;
    }

    /// <summary>
    /// 移除插件的全部实现，受影响的可插拔能力回退；等待这些实现的实例上的调用全部结束并释放。
    /// </summary>
    internal async Task RemovePluginAsync(string pluginId)
    {
        var released = new List<Task>();
        foreach (var slot in _slotsById.Values)
        {
            if (slot.RemovePlugin(pluginId) is { } tasks)
            {
                released.AddRange(tasks);
                RaiseChanged(slot.Descriptor.Id);
            }
        }

        await Task.WhenAll(released);
    }

    /// <summary>
    /// 创建实现的实例，构造参数从实现对应的服务容器解析，<paramref name="args"/> 补充或覆盖。
    /// </summary>
    internal object CreateInstance(PluggableImplDescriptor implementation, object[] args)
    {
        return ActivatorUtilities.CreateInstance(implementation.Services ?? Services, implementation.ImplementationType, args);
    }

    /// <summary>
    /// 插件实现创建实例失败：记录日志，界面显示回退；二进制不兼容时通知停用插件。
    /// </summary>
    internal void OnCreateFailed(PluggableImplDescriptor implementation, Exception exception)
    {
        Logger.LogError(exception, "创建 {Implementation} 失败，{Pluggable} 暂时改用默认实现",
            implementation.Id, implementation.PluggableId);
        ReportIfIncompatible(implementation, exception);
        RaiseChanged(implementation.PluggableId);
    }

    /// <summary>
    /// 调用实现时抛出异常：普通异常照常交给调用方处理，只在插件二进制不兼容时通知停用插件。
    /// </summary>
    internal void OnCallFailed(PluggableImplDescriptor implementation, Exception exception)
        => ReportIfIncompatible(implementation, exception);

    /// <summary>
    /// 插件实现出现二进制不兼容时触发 <see cref="Incompatible"/>。
    /// </summary>
    private void ReportIfIncompatible(PluggableImplDescriptor implementation, Exception exception)
    {
        if (implementation.PluginId is { } pluginId && BinaryCompatibility.IsIncompatible(exception))
        {
            Logger.LogError(exception, "插件 {Plugin} 与当前版本的 BetterGI 不兼容", pluginId);
            Incompatible?.Invoke(this, new PluggableIncompatibleEventArgs(pluginId, exception));
        }
    }

    /// <summary>
    /// 获取或创建代理。
    /// </summary>
    private object GetProxy(Type contract, string? implementationId)
    {
        var slot = GetSlot(contract);
        return _proxies.GetOrAdd((contract, implementationId),
            key => PluggableProxy.Create(key.Contract, this, slot, key.ImplementationId));
    }

    /// <summary>
    /// 按接口取得槽。
    /// </summary>
    private PluggableSlot GetSlot(Type contract)
        => _slotsByType.TryGetValue(contract, out var slot)
            ? slot
            : throw new InvalidOperationException($"{contract.FullName} 不是可插拔能力接口，缺少 [Pluggable] 特性。");

    /// <summary>
    /// 按标识取得槽。
    /// </summary>
    private PluggableSlot GetSlot(string pluggableId)
        => _slotsById.TryGetValue(pluggableId, out var slot)
            ? slot
            : throw new KeyNotFoundException($"未声明可插拔能力 {pluggableId}。");

    /// <summary>
    /// 触发 <see cref="Changed"/>。
    /// </summary>
    private void RaiseChanged(string pluggableId) => Changed?.Invoke(this, new PluggableChangedEventArgs(pluggableId));
}
