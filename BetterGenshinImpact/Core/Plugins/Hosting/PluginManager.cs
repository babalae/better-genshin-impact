using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using BetterGenshinImpact.Infrastructure.Storage;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 插件宿主：发现 Plugins 目录下的插件，加载程序集，启用、停用（不需要重启），安装、卸载。
/// 启用时把插件程序集中的实现交给 <see cref="PluggableRegistry"/>，停用时移除；不直接调用任何业务代码。
/// </summary>
/// <remarks>
/// 宿主内部类型，不承诺兼容，插件不要使用。除标明的方法外只在界面线程调用。
/// 设计见 Docs/design/plugin-system.md 第 10、11、14 节。
/// </remarks>
public sealed class PluginManager
{
    /// <summary>
    /// 清单文件名。
    /// </summary>
    private const string ManifestFileName = "plugin.json";

    /// <summary>
    /// 可插拔能力注册表。
    /// </summary>
    private readonly PluggableRegistry _registry;

    /// <summary>
    /// 插件配置：启用列表。
    /// </summary>
    private readonly PluginConfig _config;

    /// <summary>
    /// 推迟到下次启动的删除，用于卸载已加载的插件。
    /// </summary>
    private readonly PendingCleanupStore _cleanup;

    /// <summary>
    /// 日志。
    /// </summary>
    private readonly ILogger<PluginManager> _logger;

    /// <summary>
    /// 判断主程序能否提供某个程序集，所有插件共用。
    /// </summary>
    private readonly HostAssemblyResolver _hostAssemblies = new();

    /// <summary>
    /// 是否已执行启动流程。
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// 创建插件宿主。
    /// </summary>
    public PluginManager(PluggableRegistry registry, AllConfig config, PendingCleanupStore cleanup,
        ILogger<PluginManager> logger)
    {
        _registry = registry;
        _config = config.PluginConfig;
        _cleanup = cleanup;
        _logger = logger;
        PluginsDirectory = Global.Absolute("Plugins");
        _registry.Incompatible += OnIncompatible;
    }

    /// <summary>
    /// 插件目录，即程序目录下的 Plugins。
    /// </summary>
    public string PluginsDirectory { get; }

    /// <summary>
    /// 已发现的插件，按名称排列。只在界面线程修改。
    /// </summary>
    public ObservableCollection<PluginEntry> Plugins { get; } = [];

    /// <summary>
    /// 是否可以安装、卸载插件。只有主实例可以：其他实例可能正在使用插件文件。
    /// </summary>
    public bool CanManagePackages => InstanceBootstrap.Current?.Context.IsRoot == true;

    /// <summary>
    /// 启动流程：主实例先应用待替换的升级文件，然后扫描插件目录，按配置启用插件。
    /// 在主窗口创建前、界面线程上调用。单个插件失败只影响它自己。
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (CanManagePackages)
        {
            ApplyPendingChanges();
        }

        Refresh();
        foreach (var id in _config.EnabledPlugins.ToList())
        {
            if (Find(id) is { } entry)
            {
                Enable(entry);
            }
            else
            {
                // 插件目录可能被临时移走，保留启用记录，放回后自动恢复
                _logger.LogWarning("已启用的插件 {Id} 不存在，跳过", id);
            }
        }
    }

    /// <summary>
    /// 重新扫描插件目录：发现新插件，刷新未加载插件的清单与兼容性。只读清单，不加载程序集。
    /// 已加载的插件保留原条目，程序集不会被替换。
    /// </summary>
    public void Refresh()
    {
        var existing = Plugins.ToDictionary(p => p.Directory, StringComparer.OrdinalIgnoreCase);
        var result = new List<PluginEntry>();
        // 已加载的插件先占用标识：它们的程序集已在进程中，后发现的同标识插件视为重复
        var claimed = new HashSet<string>(Plugins.Where(p => p.IsLoaded).Select(p => p.Manifest.Id), StringComparer.Ordinal);

        foreach (var directory in EnumeratePluginDirectories())
        {
            var (manifest, error) = ReadManifest(directory);
            if (existing.TryGetValue(directory, out var entry))
            {
                if (!entry.IsLoaded)
                {
                    entry.Manifest = manifest;
                    UpdateStaticState(entry, error, claimed);
                }
            }
            else
            {
                entry = new PluginEntry(directory, manifest);
                UpdateStaticState(entry, error, claimed);
            }

            result.Add(entry);
        }

        // 已加载但目录已不存在的插件（例如被手动删除）继续保留，直到进程退出
        result.AddRange(Plugins.Where(p => p.IsLoaded && !result.Contains(p)));

        Plugins.Clear();
        foreach (var entry in result.OrderBy(p => p.Manifest.Name, StringComparer.CurrentCulture))
        {
            Plugins.Add(entry);
        }
    }

    /// <summary>
    /// 启用插件：首次启用时加载程序集，然后把其中的实现交给注册表，并写入启用列表。
    /// 保存的选择指向该插件时，下一次调用即切换过去。
    /// </summary>
    /// <returns>是否启用成功；失败时 <see cref="PluginEntry.Error"/> 记录原因。</returns>
    public bool Enable(PluginEntry entry)
    {
        if (entry.State == PluginState.Enabled)
        {
            return true;
        }

        if (entry.State is not (PluginState.Disabled or PluginState.LoadFailed))
        {
            return false;
        }

        var id = entry.Manifest.Id;
        try
        {
            if (entry.Runtime is null)
            {
                entry.SetRuntime(Load(entry));
            }

            var runtime = entry.Runtime!;
            entry.Implementations = _registry.AddPluginAssembly(id, entry.Manifest.Name, runtime.Assembly, runtime.Services);
            entry.State = PluginState.Enabled;
            entry.Error = null;
            if (!_config.EnabledPlugins.Contains(id))
            {
                _config.EnabledPlugins.Add(id);
            }

            _logger.LogInformation("已启用插件 {Id} {Version}，提供 {Count} 个实现",
                id, entry.Manifest.Version, entry.Implementations.Count);
            return true;
        }
        catch (Exception e)
        {
            entry.State = PluginState.LoadFailed;
            entry.Error = e.GetBaseException().Message;
            _logger.LogError(e, "启用插件 {Id} 失败", id);
            return false;
        }
    }

    /// <summary>
    /// 停用插件：注册表移除它的实现，受影响的可插拔能力回退；等待它的实例上的调用结束并释放，然后从启用列表移除。
    /// 程序集仍保留在进程中，再次启用时复用。
    /// </summary>
    public async Task DisableAsync(PluginEntry entry)
    {
        var id = entry.Manifest.Id;
        if (entry.State == PluginState.Enabled)
        {
            entry.State = PluginState.Disabling;
            try
            {
                await _registry.RemovePluginAsync(id);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "停用插件 {Id} 时释放实例失败", id);
            }

            entry.Implementations = [];
            entry.State = PluginState.Disabled;
            _logger.LogInformation("已停用插件 {Id}", id);
        }

        _config.EnabledPlugins.Remove(id);
    }

    /// <summary>
    /// 检查安装包并读取清单，不修改任何文件。
    /// </summary>
    /// <exception cref="InvalidDataException">不是插件安装包，或包内路径不安全。</exception>
    public PluginPackage InspectPackage(string filePath) => PluginPackageReader.Read(filePath);

    /// <summary>
    /// 安装检查过的安装包。同标识的插件已加载时，新文件放到 Plugins/.pending，下次启动时替换；
    /// 否则直接替换插件目录并重新扫描。新安装的插件默认不启用。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不是主实例，或安装包不兼容。</exception>
    public async Task<PluginInstallResult> InstallAsync(PluginPackage package)
    {
        EnsureCanManagePackages();
        if (package.CompatibilityError is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var id = package.Manifest.Id;
        var staging = Path.Combine(PluginsDirectory, ".staging", Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => PluginPackageReader.Extract(package, staging));
            if (Plugins.FirstOrDefault(p => p.Manifest.Id == id && p.IsLoaded) is { } loaded)
            {
                var pending = Path.Combine(PluginsDirectory, ".pending", id);
                DeleteDirectory(pending);
                Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
                Directory.Move(staging, pending);
                loaded.PendingVersion = package.Manifest.Version;
                _logger.LogInformation("插件 {Id} 的新版本 {Version} 将在下次启动时替换", id, package.Manifest.Version);
                return new PluginInstallResult(loaded, RequiresRestart: true);
            }

            // 未加载过的同标识插件直接替换；目录名与标识不同的旧目录一并删除，避免标识重复
            var target = Path.Combine(PluginsDirectory, id);
            foreach (var old in Plugins.Where(p => p.Manifest.Id == id).ToList())
            {
                DeleteDirectory(old.Directory);
            }

            DeleteDirectory(target);
            Directory.Move(staging, target);
            _logger.LogInformation("已安装插件 {Id} {Version}", id, package.Manifest.Version);
            Refresh();
            var entry = Plugins.First(p => string.Equals(p.Directory, target, StringComparison.OrdinalIgnoreCase));
            return new PluginInstallResult(entry, RequiresRestart: false);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                StorageFileOperations.DeletePath(staging);
            }
        }
    }

    /// <summary>
    /// 卸载插件：先停用。未加载过的插件直接删除目录；已加载的插件登记到下次启动时删除。
    /// 插件数据目录 User/PluginData 保留。
    /// </summary>
    /// <returns>是否需要重启才能删除文件。</returns>
    /// <exception cref="InvalidOperationException">当前不是主实例。</exception>
    public async Task<bool> UninstallAsync(PluginEntry entry)
    {
        EnsureCanManagePackages();
        await DisableAsync(entry);
        var id = entry.Manifest.Id;
        DeleteDirectory(Path.Combine(PluginsDirectory, ".pending", id));
        entry.PendingVersion = null;

        if (!entry.IsLoaded)
        {
            DeleteDirectory(entry.Directory);
            Plugins.Remove(entry);
            _logger.LogInformation("已卸载插件 {Id}", id);
            return false;
        }

        _cleanup.Add([entry.Directory]);
        entry.State = PluginState.Uninstalled;
        _logger.LogInformation("已卸载插件 {Id}，文件将在下次启动时删除", id);
        return true;
    }

    /// <summary>
    /// 按标识查找插件（不含标识重复的条目）。
    /// </summary>
    public PluginEntry? Find(string pluginId)
        => Plugins.FirstOrDefault(p => p.Manifest.Id == pluginId && p.State != PluginState.Duplicate);

    /// <summary>
    /// 加载插件程序集，创建插件上下文和服务容器。
    /// </summary>
    private PluginRuntime Load(PluginEntry entry)
    {
        var manifest = entry.Manifest;
        var entryPath = Path.GetFullPath(Path.Combine(entry.Directory, manifest.Entry));
        var loadContext = new PluginLoadContext(manifest.Id, entryPath, _hostAssemblies);
        var assembly = loadContext.LoadFromAssemblyPath(entryPath);
        var logger = App.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger($"Plugin.{manifest.Id}");
        var context = new PluginContext(manifest, entry.Directory, logger);
        var services = new PluginServiceProvider(context, App.ServiceProvider);
        var settingsViewModelType = RegisterSettingsView(assembly);
        _logger.LogInformation("已加载插件 {Id} {Version}：{Path}，SHA256 {Hash}",
            manifest.Id, manifest.Version, entryPath, ComputeSha256(entryPath));
        return new PluginRuntime(loadContext, assembly, context, services, settingsViewModelType);
    }

    /// <summary>
    /// 查找插件程序集中带 <see cref="PluginSettingsViewAttribute"/> 的设置 VM，
    /// 为它在应用资源中注册以 VM 类型为键的隐式 DataTemplate（与独立任务设置界面相同的方式）。
    /// 模板在插件停用后保留，再次启用时复用。
    /// </summary>
    /// <returns>设置 VM 类型，没有设置界面时为 null。</returns>
    /// <exception cref="InvalidOperationException">声明了多个设置界面，或类型不符合要求。</exception>
    private static Type? RegisterSettingsView(Assembly assembly)
    {
        var declared = assembly.GetTypes()
            .Select(type => (Type: type, Info: type.GetCustomAttribute<PluginSettingsViewAttribute>(inherit: false)))
            .Where(x => x.Info is not null)
            .ToList();
        if (declared.Count == 0)
        {
            return null;
        }

        if (declared.Count > 1)
        {
            throw new InvalidOperationException($"插件只能声明一个设置界面，当前为 {declared.Count} 个。");
        }

        var (vmType, info) = declared[0];
        var viewType = info!.ViewType;
        if (!vmType.IsClass || vmType.IsAbstract)
        {
            throw new InvalidOperationException($"插件设置 VM 必须是非抽象类：{vmType.FullName}。");
        }

        if (!typeof(FrameworkElement).IsAssignableFrom(viewType) || viewType.IsAbstract || viewType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"插件设置视图必须是有无参构造函数的 FrameworkElement：{viewType.FullName}。");
        }

        // 模板只描述“VM 类型 → 视图类型”，视图的 DataContext 由 ContentControl 设为 VM
        if (Application.Current is { } application)
        {
            var template = new DataTemplate(vmType) { VisualTree = new FrameworkElementFactory(viewType) };
            template.Seal();
            application.Resources[new DataTemplateKey(vmType)] = template;
        }

        return vmType;
    }

    /// <summary>
    /// 更新未加载插件的不兼容、标识重复状态。
    /// </summary>
    /// <param name="entry">插件条目。</param>
    /// <param name="error">清单检查的结果。</param>
    /// <param name="claimed">已被占用的标识。</param>
    private static void UpdateStaticState(PluginEntry entry, string? error, HashSet<string> claimed)
    {
        if (entry.State == PluginState.Uninstalled)
        {
            return;
        }

        if (error is not null)
        {
            entry.State = PluginState.Incompatible;
            entry.Error = error;
            return;
        }

        if (!claimed.Add(entry.Manifest.Id))
        {
            entry.State = PluginState.Duplicate;
            entry.Error = $"与另一个插件的标识 {entry.Manifest.Id} 相同";
            return;
        }

        if (entry.State is PluginState.Incompatible or PluginState.Duplicate)
        {
            entry.State = PluginState.Disabled;
            entry.Error = null;
        }
    }

    /// <summary>
    /// 列出插件目录：跳过以 . 开头的目录和没有清单的目录（如 OnnxRuntime）。
    /// </summary>
    private IEnumerable<string> EnumeratePluginDirectories()
    {
        if (!Directory.Exists(PluginsDirectory))
        {
            return [];
        }

        return Directory.GetDirectories(PluginsDirectory)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Where(d => File.Exists(Path.Combine(d, ManifestFileName)))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 读取并检查清单；读取失败时返回以目录名为标识的占位清单和失败原因。
    /// </summary>
    private static (PluginManifest Manifest, string? Error) ReadManifest(string directory)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(directory, ManifestFileName));
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Global.ManifestJsonOptions)
                           ?? throw new InvalidDataException("清单为空");
            return (manifest, PluginCompatibility.Check(manifest, relative => File.Exists(Path.Combine(directory, relative))));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            var name = Path.GetFileName(directory);
            return (new PluginManifest { Id = name, Name = name }, $"无法读取清单：{e.Message}");
        }
    }

    /// <summary>
    /// 主实例启动时应用 Plugins/.pending 中的升级文件；目标目录被占用时保留到下次。并清理残留的解压目录。
    /// </summary>
    private void ApplyPendingChanges()
    {
        var pendingRoot = Path.Combine(PluginsDirectory, ".pending");
        if (Directory.Exists(pendingRoot))
        {
            foreach (var pending in Directory.GetDirectories(pendingRoot))
            {
                var target = Path.Combine(PluginsDirectory, Path.GetFileName(pending));
                try
                {
                    if (StorageFileOperations.DeletePath(target).SkippedFiles > 0)
                    {
                        _logger.LogWarning("插件目录 {Path} 被占用，升级推迟到下次启动", target);
                        continue;
                    }

                    Directory.Move(pending, target);
                    _logger.LogInformation("已应用插件升级 {Path}", target);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(e, "应用插件升级 {Path} 失败，推迟到下次启动", target);
                }
            }
        }

        StorageFileOperations.DeletePath(Path.Combine(PluginsDirectory, ".staging"));
    }

    /// <summary>
    /// 插件实现出现二进制不兼容：停用该插件并提示。事件可能来自任意线程，切回界面线程处理。
    /// </summary>
    private void OnIncompatible(object? sender, PluggableIncompatibleEventArgs e)
        => _ = Application.Current?.Dispatcher.InvokeAsync(() => HandleIncompatibleAsync(e));

    /// <summary>
    /// 停用不兼容的插件，状态改为加载失败，并提示用户更新插件。
    /// </summary>
    private async Task HandleIncompatibleAsync(PluggableIncompatibleEventArgs e)
    {
        try
        {
            if (Find(e.PluginId) is not { State: PluginState.Enabled } entry)
            {
                return;
            }

            await DisableAsync(entry);
            entry.State = PluginState.LoadFailed;
            entry.Error = $"与当前版本的 BetterGI 不兼容：{e.Exception.GetBaseException().Message}";
            await ThemedMessageBox.WarningAsync(
                $"插件“{entry.Manifest.Name}”与当前版本的 BetterGI 不兼容，已自动停用，相关功能改用内置实现。请更新该插件。\n\n{entry.Error}",
                "插件已停用");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "停用不兼容的插件 {Id} 失败", e.PluginId);
        }
    }

    /// <summary>
    /// 删除目录；有文件被占用时抛出异常。
    /// </summary>
    private static void DeleteDirectory(string directory)
    {
        if (StorageFileOperations.DeletePath(directory).SkippedFiles > 0)
        {
            throw new IOException($"无法删除 {directory}，部分文件被占用。");
        }
    }

    /// <summary>
    /// 只有主实例可以安装、卸载插件。
    /// </summary>
    private void EnsureCanManagePackages()
    {
        if (!CanManagePackages)
        {
            throw new InvalidOperationException("只有主实例可以安装或卸载插件。");
        }
    }

    /// <summary>
    /// 计算入口程序集的 SHA256，写入日志方便排查。
    /// </summary>
    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
