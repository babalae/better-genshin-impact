using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace BetterGenshinImpact.Infrastructure.WebView;

/// <summary>
/// WebView2 环境入口的实现：校验普通环境定义，每个进程每个环境只创建一次，记录创建参数；
/// 同时把普通环境的 Profile 和已不再使用的环境目录登记为存储区域。云原神实例的区域由 Service 层登记
/// </summary>
public sealed partial class WebViewEnvironmentProvider : IWebViewEnvironmentProvider, IStorageAreaProvider
{
    /// <summary>
    /// 云原神实例数据根目录名，不能用作普通环境的 Id
    /// </summary>
    private const string CloudGameFolderName = "CloudGame";

    /// <summary>
    /// 禁用 Chromium 的遮挡与后台降频，否则会影响自动化：
    /// 前两项让窗口被遮挡时照常渲染（否则 WGC 一直拿到旧帧）；
    /// 后两项让页面在后台不降优先级、不限流定时器（SDK 的 tapKey / click 用 setTimeout 控制按住时长）。
    /// 每个实例的用户数据目录独立，这些参数不会与 HtmlMask 等功能的 WebView2 环境冲突
    /// </summary>
    private const string CloudGameBrowserArguments =
        "--disable-features=CalculateNativeWinOcclusion " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding " +
        "--disable-background-timer-throttling";

    /// <summary>
    /// 当前进程的云原神实例名；不是网页版实例进程时为 null
    /// </summary>
    private readonly string? _cloudGameInstanceName;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<WebViewEnvironmentProvider> _logger;

    /// <summary>
    /// 已创建或正在创建的普通环境，按环境 Id
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> _environments =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="cloudGameInstanceName">当前进程的云原神实例名，不是网页版实例进程时为 null</param>
    /// <param name="logger">日志</param>
    /// <exception cref="InvalidOperationException">环境或 Profile 定义不合法</exception>
    public WebViewEnvironmentProvider(string? cloudGameInstanceName, ILogger<WebViewEnvironmentProvider> logger)
    {
        _cloudGameInstanceName = cloudGameInstanceName;
        _logger = logger;
        ValidateDefinitions();
    }

    /// <inheritdoc />
    public Task<CoreWebView2Environment> GetAsync(WebViewEnvironmentDefinition environment)
    {
        if (!WebViewEnvironments.All.Contains(environment))
        {
            throw new InvalidOperationException($"WebView2 环境「{environment.Id}」未在 WebViewEnvironments.All 中定义");
        }

        var lazy = _environments.GetOrAdd(environment.Id, _ => new Lazy<Task<CoreWebView2Environment>>(
            () => CreateAsync(environment.Id, environment.DataFolder, environment.BrowserArguments)));
        var task = lazy.Value;

        // 创建失败（例如运行时尚未安装）不缓存，安装后再次打开页面即可重试
        _ = task.ContinueWith(_ => _environments.TryRemove(KeyValuePair.Create(environment.Id, lazy)),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    /// <inheritdoc />
    public Task<CoreWebView2Environment> CreateCloudGameAsync(string instanceName)
    {
        // 子实例不操作其他实例：只有同名的网页版实例进程可以打开该实例的数据目录
        if (_cloudGameInstanceName is null
            || !string.Equals(_cloudGameInstanceName.Trim(), instanceName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"只有云原神实例「{instanceName}」的进程可以创建它的网页环境");
        }

        return CreateAsync($"{CloudGameFolderName}/{instanceName.Trim()}",
            WebViewDataFolders.GetCloudGameFolder(instanceName), CloudGameBrowserArguments);
    }

    /// <inheritdoc />
    public void ReportProcessFailed(string source, CoreWebView2ProcessFailedEventArgs e)
    {
        _logger.LogWarning("网页进程异常退出：{Source}，类型 {Kind}，原因 {Reason}，退出码 {ExitCode}，进程 {Description}",
            source, e.ProcessFailedKind, e.Reason, e.ExitCode, e.ProcessDescription);
    }

    /// <summary>
    /// 创建环境，并记录环境 Id、数据目录、浏览器参数和运行时版本，排查问题时可以直接对照
    /// </summary>
    private async Task<CoreWebView2Environment> CreateAsync(string id, string dataFolder, string browserArguments)
    {
        Directory.CreateDirectory(dataFolder);
        var options = new CoreWebView2EnvironmentOptions(browserArguments);
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
        _logger.LogInformation("已创建 WebView2 环境 {Id}：数据目录 {DataFolder}，浏览器参数 {Arguments}，运行时 {Version}",
            id, dataFolder, string.IsNullOrEmpty(browserArguments) ? "（无）" : browserArguments, environment.BrowserVersionString);
        return environment;
    }

    /// <summary>
    /// 校验定义：环境 Id 唯一、只含字母数字和短横线、不是 CloudGame；每个 Profile 都属于已定义的环境且名称唯一
    /// </summary>
    private static void ValidateDefinitions()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var environment in WebViewEnvironments.All)
        {
            if (!EnvironmentIdRegex().IsMatch(environment.Id)
                || environment.Id.Equals(CloudGameFolderName, StringComparison.OrdinalIgnoreCase)
                || !ids.Add(environment.Id))
            {
                throw new InvalidOperationException($"WebView2 环境 Id 不合法或重复：{environment.Id}");
            }
        }

        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in WebViewProfiles.All)
        {
            if (!WebViewEnvironments.All.Contains(profile.Environment)
                || !EnvironmentIdRegex().IsMatch(profile.ProfileName)
                || !profiles.Add($"{profile.Environment.Id}/{profile.ProfileName}"))
            {
                throw new InvalidOperationException(
                    $"WebView2 Profile 不合法、重复或所属环境未定义：{profile.Environment.Id}/{profile.ProfileName}");
            }
        }
    }

    /// <summary>
    /// 环境 Id 与 Profile 名称的格式
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex EnvironmentIdRegex();

    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        var areas = new List<StorageArea>();
        foreach (var profile in WebViewProfiles.All)
        {
            // 网页进程长期占用这些文件，统一推迟到下次启动、任何 WebView 创建之前删除
            areas.Add(profile.ClearsAllData
                ? new StorageArea($"webview:{profile.Environment.Id}:{profile.ProfileName}", profile.DisplayName,
                    StorageGroup.WebData, [profile.ProfileFolder])
                {
                    Description = profile.Description,
                    CleanupEffect = "会清除网页中保存的设置，下次启动时清理",
                    Timing = StorageCleanupTiming.NextStartup,
                    RequiresConfirmation = true,
                    IncludedInQuickCleanup = false,
                }
                : new StorageArea($"webview:{profile.Environment.Id}:{profile.ProfileName}", profile.DisplayName,
                    StorageGroup.WebData, WebViewDataFolders.GetCacheFolders(profile.ProfileFolder))
                {
                    Description = profile.Description,
                    CleanupEffect = "下次启动时清理",
                    Timing = StorageCleanupTiming.NextStartup,
                });
        }

        var unused = ListUnusedEnvironmentFolders();
        if (unused.Count > 0)
        {
            areas.Add(new StorageArea("webview:unused", "已不再使用的网页数据", StorageGroup.WebData, unused)
            {
                Description = "旧功能留下的网页目录",
                CleanupEffect = "不影响现有功能",
            });
        }

        return areas;
    }

    /// <summary>
    /// Data/WebView2 下既不在定义中、也不是 CloudGame 的条目，视为已删除环境的遗留数据
    /// </summary>
    private static IReadOnlyList<string> ListUnusedEnvironmentFolders()
    {
        var known = WebViewEnvironments.All.Select(environment => environment.Id)
            .Append(CloudGameFolderName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            return Directory.Exists(AppPaths.WebView)
                ? Directory.GetFileSystemEntries(AppPaths.WebView).Where(path => !known.Contains(Path.GetFileName(path))).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
