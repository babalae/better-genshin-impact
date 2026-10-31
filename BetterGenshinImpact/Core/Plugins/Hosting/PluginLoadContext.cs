using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 单个插件的程序集加载上下文，不可回收。主程序能提供的程序集一律交给默认上下文，
/// 保证 IOcrService、Mat 等类型在插件和主程序之间是同一个类型；主程序没有的依赖从插件目录加载。
/// 不可回收的原因见 Docs/design/plugin-system.md 第 10.4 节。
/// </summary>
internal sealed class PluginLoadContext(string pluginId, string entryPath, HostAssemblyResolver host)
    : AssemblyLoadContext($"plugin:{pluginId}", isCollectible: false)
{
    /// <summary>
    /// 按插件的 deps.json 解析私有依赖。
    /// </summary>
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // BetterGI、OpenCvSharp、WPF-UI 等返回 null，由默认上下文加载（单文件发布时从单文件包中加载）
        if (host.CanResolve(assemblyName))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

/// <summary>
/// 判断主程序（默认加载上下文）能否提供某个程序集。不依赖磁盘文件：单文件发布时主程序的程序集在单文件包中。
/// </summary>
internal sealed class HostAssemblyResolver
{
    /// <summary>
    /// 按程序集简单名称缓存判断结果。
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 默认上下文能否加载该程序集。
    /// </summary>
    public bool CanResolve(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name)
        {
            return false;
        }

        return _cache.GetOrAdd(name, static simpleName =>
        {
            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(simpleName));
                return true;
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return false;
            }
        });
    }
}
