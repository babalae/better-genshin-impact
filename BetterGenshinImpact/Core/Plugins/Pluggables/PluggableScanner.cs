using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 扫描程序集中的可插拔能力和实现，并按 Docs/design/plugin-system.md 第 8.6 节校验。
/// 校验失败抛出 <see cref="InvalidOperationException"/>：主程序集在启动时直接失败，插件只让该插件加载失败。
/// </summary>
internal static partial class PluggableScanner
{
    /// <summary>
    /// 扫描带 <see cref="PluggableAttribute"/> 的接口。
    /// </summary>
    /// <param name="assemblies">主程序集。</param>
    public static IReadOnlyList<PluggableDescriptor> ScanContracts(IEnumerable<Assembly> assemblies)
    {
        var result = new List<PluggableDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in assemblies.SelectMany(a => GetTypes(a, strict: false)))
        {
            if (!type.IsInterface || type.GetCustomAttribute<PluggableAttribute>(inherit: false) is not { } info)
            {
                continue;
            }

            if (!NameRegex().IsMatch(info.Id))
            {
                throw new InvalidOperationException($"可插拔能力标识 {info.Id} 不是小写 snake_case：{type.FullName}。");
            }

            if (!ids.Add(info.Id))
            {
                throw new InvalidOperationException($"可插拔能力标识 {info.Id} 重复声明：{type.FullName}。");
            }

            ValidateContract(type);
            result.Add(new PluggableDescriptor(info.Id, info.DisplayName, info.Description, type));
        }

        return result;
    }

    /// <summary>
    /// 扫描带 <see cref="PluggableImplAttribute"/> 的实现类。
    /// </summary>
    /// <param name="assembly">主程序集或插件程序集。</param>
    /// <param name="contracts">已知的可插拔能力，按接口索引。</param>
    /// <param name="pluginId">插件标识，主程序集为 null。</param>
    /// <param name="pluginName">插件名称，主程序集为 null。</param>
    /// <param name="services">插件的服务容器，主程序集为 null。</param>
    public static IReadOnlyList<PluggableImplDescriptor> ScanImplementations(Assembly assembly,
        IReadOnlyDictionary<Type, PluggableDescriptor> contracts, string? pluginId, string? pluginName,
        IServiceProvider? services)
    {
        var result = new List<PluggableImplDescriptor>();
        // 插件程序集中有类型加载失败说明它与当前主程序不兼容，直接报错，不跳过
        foreach (var type in GetTypes(assembly, strict: pluginId is not null))
        {
            if (type.GetCustomAttribute<PluggableImplAttribute>(inherit: false) is not { } info)
            {
                continue;
            }

            if (!type.IsClass || type.IsAbstract)
            {
                throw new InvalidOperationException($"可插拔实现必须是非抽象类：{type.FullName}。");
            }

            var implemented = type.GetInterfaces().Where(contracts.ContainsKey).ToList();
            if (implemented.Count != 1)
            {
                throw new InvalidOperationException($"可插拔实现必须且只能实现一个可插拔能力接口，当前为 {implemented.Count} 个：{type.FullName}。");
            }

            if (!NameRegex().IsMatch(info.Name))
            {
                throw new InvalidOperationException($"可插拔实现名称 {info.Name} 不是小写 snake_case：{type.FullName}。");
            }

            if (pluginId is not null && info.IsDefault)
            {
                throw new InvalidOperationException($"插件中的实现不能设为默认实现：{type.FullName}。");
            }

            if (type.GetConstructors().Length == 0)
            {
                throw new InvalidOperationException($"可插拔实现必须有 public 构造函数：{type.FullName}。");
            }

            var contract = contracts[implemented[0]];
            result.Add(new PluggableImplDescriptor(
                $"{pluginId ?? "builtin"}.{info.Name}",
                contract.Id,
                info.DisplayName,
                info.Description,
                type,
                info.IsDefault,
                pluginId,
                pluginName) { Services = services });
        }

        return result;
    }

    /// <summary>
    /// 校验接口规则（第 7.5 节规则 1、2）：public，只含方法，返回类型不能是 ValueTask 或 IAsyncEnumerable。
    /// 代理需要知道每次调用何时结束，才能在切换后安全释放旧实例。
    /// </summary>
    private static void ValidateContract(Type contract)
    {
        if (!contract.IsVisible || contract.ContainsGenericParameters)
        {
            throw new InvalidOperationException($"可插拔能力必须是 public 的非泛型接口：{contract.FullName}。");
        }

        foreach (var type in contract.GetInterfaces().Prepend(contract))
        {
            if (type.GetProperties().Length > 0 || type.GetEvents().Length > 0)
            {
                throw new InvalidOperationException($"可插拔能力接口只能包含方法，不能有属性或事件：{type.FullName}。");
            }

            foreach (var method in type.GetMethods())
            {
                var returnType = method.ReturnType;
                var definition = returnType.IsGenericType ? returnType.GetGenericTypeDefinition() : null;
                if (returnType == typeof(ValueTask) || definition == typeof(ValueTask<>) || definition == typeof(IAsyncEnumerable<>))
                {
                    throw new InvalidOperationException(
                        $"可插拔能力接口的方法不能返回 {returnType.Name}，请改用 Task 或 Task<T>：{type.FullName}.{method.Name}。");
                }
            }
        }
    }

    /// <summary>
    /// 读取程序集中的类型。
    /// </summary>
    /// <param name="assembly">程序集。</param>
    /// <param name="strict">为 true 时有类型加载失败就抛出异常；否则跳过无法加载的类型。</param>
    private static IEnumerable<Type> GetTypes(Assembly assembly, bool strict)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            if (strict)
            {
                throw new InvalidOperationException(
                    $"程序集 {assembly.GetName().Name} 中有类型无法加载：{e.LoaderExceptions.FirstOrDefault()?.Message}", e);
            }

            return e.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// 可插拔能力标识和实现名称的规则：小写 snake_case。
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex NameRegex();
}
