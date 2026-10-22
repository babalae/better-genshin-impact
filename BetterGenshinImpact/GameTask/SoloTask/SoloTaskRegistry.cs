using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务注册表。启动时扫描程序集中带 <see cref="SoloTaskInfoAttribute"/> 的类，校验后生成描述符。
/// 独立任务页、多实例、任务计划和一条龙都从这里获得任务清单。
/// </summary>
public sealed partial class SoloTaskRegistry
{
    /// <summary>
    /// 按任务标识索引的描述符。
    /// </summary>
    private readonly Dictionary<string, SoloTaskDescriptor> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// 按任务实现类型索引的描述符。
    /// </summary>
    private readonly Dictionary<Type, SoloTaskDescriptor> _byType = [];

    /// <summary>
    /// 全部已注册任务，按 Order 排列。
    /// </summary>
    public IReadOnlyList<SoloTaskDescriptor> All { get; }

    /// <summary>
    /// 扫描指定程序集并建立注册表。将来加载插件时，把插件程序集追加到列表即可。
    /// </summary>
    /// <param name="assemblies">要扫描的程序集。</param>
    /// <exception cref="InvalidOperationException">任务声明不符合合同。</exception>
    public SoloTaskRegistry(IEnumerable<Assembly> assemblies)
    {
        var settingsViewModels = new HashSet<Type>();
        foreach (var type in assemblies.SelectMany(GetLoadableTypes))
        {
            var info = type.GetCustomAttribute<SoloTaskInfoAttribute>();
            if (info is null)
            {
                continue;
            }

            var descriptor = CreateDescriptor(type, info);
            if (!_byId.TryAdd(descriptor.TaskId, descriptor))
            {
                throw new InvalidOperationException($"独立任务标识 {descriptor.TaskId} 重复注册：{type.FullName}。");
            }

            if (descriptor.SettingsViewModelType is { } vmType && !settingsViewModels.Add(vmType))
            {
                throw new InvalidOperationException($"设置 VM {vmType.Name} 被多个独立任务绑定。");
            }

            _byType[type] = descriptor;
        }

        // Order 同时决定分组先后（约定按百位分组），菜单按此顺序分段
        All = _byId.Values
            .OrderBy(d => d.Order)
            .ThenBy(d => d.TaskId, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 按任务标识获取描述符。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>描述符。</returns>
    /// <exception cref="KeyNotFoundException">任务未注册。</exception>
    public SoloTaskDescriptor Get(string taskId)
        => _byId.TryGetValue(taskId, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"未注册独立任务 {taskId}。");

    /// <summary>
    /// 尝试按任务标识获取描述符。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="descriptor">找到的描述符。</param>
    /// <returns>是否找到。</returns>
    public bool TryGet(string? taskId, out SoloTaskDescriptor descriptor)
    {
        if (taskId is not null && _byId.TryGetValue(taskId, out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <summary>
    /// 按任务实现类型查找描述符；未加特性的类返回 null。
    /// </summary>
    /// <param name="taskType">任务实现类型。</param>
    /// <returns>描述符或 null。</returns>
    public SoloTaskDescriptor? FindByType(Type taskType) => _byType.GetValueOrDefault(taskType);

    /// <summary>
    /// 返回任务显示名；未注册时返回标识本身。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>显示名。</returns>
    public string GetDisplayName(string? taskId)
        => TryGet(taskId, out var descriptor) ? descriptor.DisplayName : taskId ?? string.Empty;

    /// <summary>
    /// 返回指定入口可用的任务。
    /// </summary>
    /// <param name="scope">运行入口。</param>
    /// <returns>按分组和顺序排列的描述符。</returns>
    public IEnumerable<SoloTaskDescriptor> GetByScope(SoloTaskScopes scope) => All.Where(d => d.Scopes.HasFlag(scope));

    /// <summary>
    /// 校验任务类型并建立描述符。
    /// </summary>
    private static SoloTaskDescriptor CreateDescriptor(Type type, SoloTaskInfoAttribute info)
    {
        if (!TaskIdRegex().IsMatch(info.Id))
        {
            throw new InvalidOperationException($"独立任务标识 {info.Id} 不符合 snake_case 规则：{type.FullName}。");
        }

        if (type.IsAbstract || type.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException($"独立任务 {info.Id} 不能是抽象类或开放泛型：{type.FullName}。");
        }

        var contracts = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISoloTask<,>))
            .ToArray();
        if (contracts.Length != 1)
        {
            throw new InvalidOperationException($"独立任务 {info.Id} 必须恰好实现一个 ISoloTask<TParam, TData>：{type.FullName}。");
        }

        var constructors = type.GetConstructors();
        if (constructors.Length != 1 || constructors[0].GetParameters().All(p => p.ParameterType != typeof(GameRuntime)))
        {
            throw new InvalidOperationException($"独立任务 {info.Id} 必须只有一个接收 GameRuntime 的公共构造函数：{type.FullName}。");
        }

        var arguments = contracts[0].GetGenericArguments();
        return new SoloTaskDescriptor(type, info, arguments[0], arguments[1],
            type.GetCustomAttribute<SoloTaskSettingsViewAttribute>(), constructors[0]);
    }

    /// <summary>
    /// 读取程序集中可加载的类型，部分类型加载失败时跳过它们。
    /// </summary>
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// 任务标识规则：小写 snake_case；插件任务为 {pluginId}.{name}。
    /// </summary>
    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)?$")]
    private static partial Regex TaskIdRegex();
}
