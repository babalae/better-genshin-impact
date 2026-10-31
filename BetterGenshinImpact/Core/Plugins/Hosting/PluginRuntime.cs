using System;
using System.IO;
using System.Reflection;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 插件加载后的运行时信息：加载上下文、入口程序集、上下文和服务容器。程序集保留到进程退出。
/// </summary>
/// <param name="LoadContext">插件的程序集加载上下文。</param>
/// <param name="Assembly">入口程序集。</param>
/// <param name="Context">提供给插件的上下文。</param>
/// <param name="Services">插件实现解析构造参数使用的服务容器。</param>
/// <param name="SettingsViewModelType">带 <see cref="PluginSettingsViewAttribute"/> 的设置 VM 类型，没有设置界面时为 null。</param>
internal sealed record PluginRuntime(
    PluginLoadContext LoadContext,
    Assembly Assembly,
    PluginContext Context,
    IServiceProvider Services,
    Type? SettingsViewModelType);

/// <summary>
/// <see cref="IPluginContext"/> 的实现。
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    /// <summary>
    /// 创建插件上下文。
    /// </summary>
    /// <param name="manifest">插件清单。</param>
    /// <param name="pluginDirectory">插件安装目录。</param>
    /// <param name="logger">插件的日志。</param>
    public PluginContext(PluginManifest manifest, string pluginDirectory, ILogger logger)
    {
        Manifest = manifest;
        PluginDirectory = pluginDirectory;
        Logger = logger;
        Settings = new PluginSettingsStore(() => DataDirectory, logger);
    }

    /// <inheritdoc />
    public PluginManifest Manifest { get; }

    /// <inheritdoc />
    public string PluginDirectory { get; }

    /// <inheritdoc />
    public string DataDirectory
    {
        get
        {
            var directory = Global.Absolute(Path.Combine("User", "PluginData", Manifest.Id));
            System.IO.Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <inheritdoc />
    public ILogger Logger { get; }

    /// <inheritdoc />
    public IPluginSettingsStore Settings { get; }
}

/// <summary>
/// 插件的服务容器：先提供该插件自己的上下文、设置和日志，其余转到主程序的服务容器。
/// 主程序的内部服务没有兼容性承诺，插件注入的主程序类型在版本间可能变化。
/// </summary>
internal sealed class PluginServiceProvider(PluginContext context, IServiceProvider host)
    : IServiceProvider, IServiceProviderIsService
{
    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IPluginContext))
        {
            return context;
        }

        if (serviceType == typeof(ILogger))
        {
            return context.Logger;
        }

        if (serviceType == typeof(IPluginSettingsStore))
        {
            return context.Settings;
        }

        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceProviderIsService))
        {
            return this;
        }

        return host.GetService(serviceType);
    }

    /// <inheritdoc />
    public bool IsService(Type serviceType)
        => serviceType == typeof(IPluginContext)
           || serviceType == typeof(ILogger)
           || serviceType == typeof(IPluginSettingsStore)
           || serviceType == typeof(IServiceProvider)
           || host.GetService<IServiceProviderIsService>()?.IsService(serviceType) == true;
}
