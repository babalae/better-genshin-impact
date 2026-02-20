using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.Core.Script;

public class ScriptRepoUpdater : Singleton<ScriptRepoUpdater>
{
    private readonly ILogger<ScriptRepoUpdater> _logger = App.GetLogger<ScriptRepoUpdater>();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// 全局互斥锁，串行化所有对仓库目录和用户脚本目录的写操作，
    /// 防止自动更新、手动更新、ZIP 导入等并发冲突。
    /// </summary>
    private readonly SemaphoreSlim _repoWriteLock = new(1, 1);

    /// <summary>
    /// 指示后台自动更新是否正在进行。
    /// Dialog 可据此禁用手动操作按钮并显示进度提示。
    /// </summary>
    private volatile bool _isAutoUpdating;
    public bool IsAutoUpdating => _isAutoUpdating;

    /// <summary>
    /// 后台自动更新状态变化事件（开始/结束），
    /// 注意：可能在非 UI 线程触发，订阅方需自行 Dispatch。
    /// </summary>
    public event EventHandler? AutoUpdateStateChanged;

    // 仓储位置
    public static readonly string ReposPath = Global.Absolute("Repos");

    // 仓储临时目录 用于下载与解压
    public static readonly string ReposTempPath = Path.Combine(ReposPath, "Temp");

    // // 中央仓库信息地址
    // public static readonly List<string> CenterRepoInfoUrls =
    // [
    //     "https://raw.githubusercontent.com/babalae/bettergi-scripts-list/refs/heads/main/repo.json",
    //     "https://r2-script.bettergi.com/github_mirror/repo.json",
    // ];

    // 中央仓库默认文件夹名
    public static readonly string CenterRepoFolderName = "bettergi-scripts-list";

    /// <summary>
    /// 当前活跃的中央仓库路径（根据用户配置的渠道动态解析）
    /// </summary>
    public static string CenterRepoPath
    {
        get
        {
            try
            {
                var config = TaskContext.Instance().Config.ScriptConfig;
                var url = ResolveRepoUrl(config);
                var folderName = GetRepoFolderName(url);
                return Path.Combine(ReposPath, folderName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScriptRepoUpdater] CenterRepoPath 解析失败，回退到默认路径: {ex.Message}");
                return Path.Combine(ReposPath, CenterRepoFolderName);
            }
        }
    }

    public static readonly string CenterRepoPathOld = Path.Combine(ReposPath, "bettergi-scripts-list-main");

    public static readonly Dictionary<string, string> PathMapper = new Dictionary<string, string>
    {
        { "pathing", Global.Absolute("User\\AutoPathing") },
        { "js", Global.Absolute("User\\JsScript") },
        { "combat", Global.Absolute("User\\AutoFight") },
        { "tcg", Global.Absolute("User\\AutoGeniusInvokation") },
    };

    private WebpageWindow? _webWindow;

    // [Obsolete]
    // public void AutoUpdate()
    // {
    //     var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
    //
    //     if (!Directory.Exists(ReposPath))
    //     {
    //         Directory.CreateDirectory(ReposPath);
    //     }
    //
    //     // 判断更新周期是否到达
    //     if (DateTime.Now - scriptConfig.LastUpdateScriptRepoTime >=
    //         TimeSpan.FromDays(scriptConfig.AutoUpdateScriptRepoPeriod))
    //     {
    //         // 更新仓库
    //         Task.Run(async () =>
    //         {
    //             try
    //             {
    //                 var (repoPath, updated) = await UpdateCenterRepo();
    //                 Debug.WriteLine($"脚本仓库更新完成，路径：{repoPath}");
    //                 scriptConfig.LastUpdateScriptRepoTime = DateTime.Now;
    //                 if (updated)
    //                 {
    //                     scriptConfig.ScriptRepoHintDotVisible = true;
    //                 }
    //             }
    //             catch (Exception e)
    //             {
    //                 _logger.LogDebug(e, $"脚本仓库更新失败：{e.Message}");
    //             }
    //         });
    //     }
    // }

    /// <summary>
    /// 自动更新已订阅的脚本
    /// 在启动时先拉取最新仓库，然后检查已订阅的脚本是否有更新，
    /// 如果有则自动从仓库中检出最新版本到用户目录。
    /// 类似于 Web 端的"一键更新"功能
    /// </summary>
    public async Task AutoUpdateSubscribedScripts()
    {
        // 迁移旧 config.json 中的订阅路径到独立文件
        MigrateSubscribedPathsFromConfig();

        try
        {
            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;

            // 检查是否启用自动更新
            if (!scriptConfig.AutoUpdateSubscribedScripts)
            {
                _logger.LogDebug("已禁用自动更新订阅脚本");
                return;
            }

            _isAutoUpdating = true;
            AutoUpdateStateChanged?.Invoke(this, EventArgs.Empty);

            await _repoWriteLock.WaitAsync();
            try
            {
                var subscribedPaths = GetSubscribedPathsForCurrentRepo();
                if (subscribedPaths.Count == 0)
                {
                    _logger.LogDebug("没有已订阅的脚本，跳过自动更新");
                    return;
                }

                var (successCount, failCount) = await UpdateAllSubscribedScriptsCore(scriptConfig);

                if (successCount > 0)
                {
                    _logger.LogInformation("自动更新订阅脚本完成: 成功 {Success} 个, 失败 {Fail} 个", successCount, failCount);
                    UIDispatcherHelper.Invoke(() => Toast.Success($"已自动更新 {successCount} 个订阅脚本"));
                }
            }
            finally
            {
                _repoWriteLock.Release();
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "自动更新订阅脚本失败");
        }
        finally
        {
            _isAutoUpdating = false;
            AutoUpdateStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 手动一键更新已订阅的脚本（不检查 AutoUpdateSubscribedScripts 配置开关）。
    /// 更新所有订阅脚本。
    /// </summary>
    public async Task ManualUpdateSubscribedScripts()
    {
        try
        {
            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;

            var subscribedPaths = GetSubscribedPathsForCurrentRepo();
            if (subscribedPaths.Count == 0)
            {
                _logger.LogInformation("没有已订阅的脚本");
                UIDispatcherHelper.Invoke(() => Toast.Information("没有已订阅的脚本，请先在仓库中订阅脚本"));
                return;
            }

            await _repoWriteLock.WaitAsync();
            try
            {
                var (successCount, failCount) = await UpdateAllSubscribedScriptsCore(scriptConfig);
                _logger.LogInformation("一键更新订阅脚本完成: 成功 {Success} 个, 失败 {Fail} 个", successCount, failCount);
                UIDispatcherHelper.Invoke(() =>
                {
                    if (failCount == 0)
                        Toast.Success($"已更新 {successCount} 个订阅脚本");
                    else
                        Toast.Warning($"已更新 {successCount} 个订阅脚本，{failCount} 个失败");
                });
            }
            finally
            {
                _repoWriteLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "一键更新订阅脚本失败");
            UIDispatcherHelper.Invoke(() => Toast.Error($"更新订阅脚本失败，建议重置仓库后重试\n原因：{ex.Message}"));
        }
    }

    /// <summary>
    /// 自动/手动更新所有订阅脚本的通用核心逻辑。
    /// 更新全部订阅脚本，不检查 hasUpdate 标记。
    /// </summary>
    private async Task<(int successCount, int failCount)> UpdateAllSubscribedScriptsCore(ScriptConfig scriptConfig)
    {
        // 第一步：拉取最新仓库
        await UpdateCenterRepoSilently(scriptConfig);

        // 检查仓库是否存在
        if (!Directory.Exists(CenterRepoPath))
        {
            _logger.LogWarning("仓库文件夹不存在，请先更新仓库");
            UIDispatcherHelper.Invoke(() => Toast.Warning("仓库文件夹不存在，请先更新仓库"));
            return (0, 0);
        }

        // 重新加载订阅路径
        var subscribedPaths = GetSubscribedPathsForCurrentRepo();
        if (subscribedPaths.Count == 0)
        {
            return (0, 0);
        }

        // 查找仓库路径
        string repoPath;
        try
        {
            repoPath = FindCenterRepoPath();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查找中央仓库路径失败");
            UIDispatcherHelper.Invoke(() => Toast.Warning("查找仓库路径失败，请先更新仓库"));
            return (0, 0);
        }

        // 展开所有订阅路径，直接全部更新
        var expandedPaths = ExpandTopLevelPaths(subscribedPaths, repoPath);

        int successCount = 0;
        int failCount = 0;

        foreach (var path in expandedPaths)
        {
            try
            {
                var (first, remainingPath) = GetFirstFolderAndRemainingPath(path);
                if (!PathMapper.TryGetValue(first, out var userPath))
                {
                    _logger.LogDebug("未知的脚本路径类型: {Path}", path);
                    continue;
                }

                var destPath = Path.Combine(userPath, remainingPath);

                // 备份需要保存的文件（仅 JS 脚本）
                List<string> backupFiles = new();
                if (first == "js")
                {
                    backupFiles = BackupScriptFiles(path, repoPath);
                }

                // 删除旧文件/目录
                if (Directory.Exists(destPath))
                {
                    DirectoryHelper.DeleteDirectoryWithReadOnlyCheck(destPath);
                }
                else if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                // 从仓库检出最新文件
                CheckoutPath(repoPath, path, destPath);

                // 图标处理（仅对目录）
                if (Directory.Exists(destPath))
                {
                    DealWithIconFolder(destPath);
                }

                // 恢复备份的文件
                if (first == "js" && backupFiles.Count > 0)
                {
                    RestoreScriptFiles(path, repoPath);
                }

                // 解析 JS 脚本依赖
                if (first == "js")
                {
                    ResolveScriptDependencies(repoPath, destPath);
                }

                successCount++;
                _logger.LogInformation("更新脚本成功: {Path}", path);
            }
            catch (Exception ex)
            {
                failCount++;
                _logger.LogWarning(ex, "更新脚本失败: {Path}", path);
            }
        }

        if (successCount > 0)
        {
            UpdateSubscribedScriptPaths();
        }

        return (successCount, failCount);
    }

    /// <summary>
    /// 展开裸顶层路径为其子目录（如 "pathing" -> "pathing/xxx", "pathing/yyy"；"js" -> "js/aaa", "js/bbb"）。
    /// 这样可以避免后续检出时 destPath 等于整个用户目录而误删所有用户脚本。
    /// 非 PathMapper 顶层 key 或已包含子路径的条目原样保留。
    /// </summary>
    private List<string> ExpandTopLevelPaths(List<string> paths, string repoPath)
    {
        var topLevelKeys = PathMapper.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var path in paths)
        {
            // 仅当路径恰好是一个裸顶层 key（如 "pathing"、"js"、"combat"）时才展开
            if (!topLevelKeys.Contains(path))
            {
                result.Add(path);
                continue;
            }

            bool isGitRepo = IsGitRepository(repoPath);
            if (isGitRepo)
            {
                using var repo = new Repository(repoPath);
                var commit = repo.Head.Tip;
                if (commit != null)
                {
                    var repoTree = GetRepoSubdirectoryTree(repo);
                    var entry = repoTree[path];
                    if (entry?.TargetType == TreeEntryTargetType.Tree)
                    {
                        var subTree = (Tree)entry.Target;
                        foreach (var child in subTree)
                        {
                            if (child.TargetType == TreeEntryTargetType.Tree)
                            {
                                result.Add(path + "/" + child.Name);
                            }
                        }
                    }
                }
            }
            else
            {
                var dir = Path.Combine(repoPath, path);
                if (Directory.Exists(dir))
                {
                    foreach (var subDir in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    {
                        result.Add(path + "/" + Path.GetFileName(subDir));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 静默更新中央仓库（用于自动更新订阅脚本前同步最新仓库内容）。
    /// 注意：此方法设计为在 _repoWriteLock 持有期间调用，
    /// 内部直接调用 UpdateCenterRepoByGitCore 而非带锁包装的 UpdateCenterRepoByGit，以避免死锁。
    /// </summary>
    private async Task UpdateCenterRepoSilently(ScriptConfig scriptConfig)
    {
        try
        {
            // 获取仓库URL
            var repoUrl = ResolveRepoUrl(scriptConfig);
            if (string.IsNullOrEmpty(repoUrl))
            {
                _logger.LogDebug("无法确定仓库URL，跳过仓库更新");
                return;
            }

            _logger.LogInformation("自动更新订阅脚本：开始静默更新脚本仓库...");

            var (_, updated) = await UpdateCenterRepoByGitCore(repoUrl, null);

            if (updated)
            {
                _logger.LogInformation("自动更新订阅脚本：仓库有新内容");
                scriptConfig.ScriptRepoHintDotVisible = true;
            }
            else
            {
                _logger.LogDebug("自动更新订阅脚本：仓库已是最新");
            }

            scriptConfig.LastUpdateScriptRepoTime = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "静默更新仓库失败，将基于本地仓库继续检查订阅更新");
        }
    }

    /// <summary>
    /// 渠道名称到URL的映射
    /// </summary>
    public static readonly Dictionary<string, string> RepoChannels = new()
    {
        { "CNB", "https://cnb.cool/bettergi/bettergi-scripts-list" },
        { "GitCode", "https://gitcode.com/huiyadanli/bettergi-scripts-list" },
        { "GitHub", "https://github.com/babalae/bettergi-scripts-list" },
    };

    /// <summary>
    /// 根据用户配置中的渠道名称解析仓库URL
    /// </summary>
    private static string? ResolveRepoUrl(ScriptConfig scriptConfig)
    {
        var channelName = scriptConfig.SelectedChannelName;

        if (string.IsNullOrEmpty(channelName))
        {
            // 默认使用 CNB
            return RepoChannels["CNB"];
        }

        if (channelName == "自定义")
        {
            var customUrl = scriptConfig.CustomRepoUrl;
            if (!string.IsNullOrWhiteSpace(customUrl) && customUrl != "https://example.com/custom-repo")
            {
                return customUrl;
            }

            return null;
        }

        return RepoChannels.TryGetValue(channelName, out var url) ? url : RepoChannels["CNB"];
    }

    /// <summary>
    /// 从仓库 URL 中提取文件夹名称（用于按仓库分开存储）
    /// 优先查找持久化的 URL→文件夹名 映射，若无映射则根据 URL 结构推导
    /// </summary>
    internal static string GetRepoFolderName(string? repoUrl)
    {
        if (string.IsNullOrEmpty(repoUrl))
            return CenterRepoFolderName;

        var trimmedUrl = repoUrl.TrimEnd('/');

        // 优先查找已保存的映射（基于目录结构重合度确定的文件夹名）
        var mapping = LoadFolderMapping();
        if (mapping.TryGetValue(trimmedUrl, out var savedName) && !string.IsNullOrEmpty(savedName))
            return savedName;

        // 无映射，根据 URL 推导默认名称
        return DeriveBaseFolderName(trimmedUrl);
    }

    /// <summary>
    /// 根据 URL 推导基础文件夹名称（仅使用仓库名，不包含拥有者）
    /// </summary>
    private static string DeriveBaseFolderName(string trimmedUrl)
    {
        try
        {
            var uri = new Uri(trimmedUrl);

            var segments = uri.Segments
                .Select(s => s.TrimEnd('/'))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (segments.Length == 0)
                return CenterRepoFolderName;

            var repoName = segments.Last();

            // 去掉 .git 后缀
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repoName = repoName[..^4];

            return SanitizeFolderName(repoName);
        }
        catch
        {
            return CenterRepoFolderName;
        }
    }

    /// <summary>
    /// 净化文件夹名称，移除不合法字符
    /// </summary>
    private static string SanitizeFolderName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrEmpty(sanitized) ? CenterRepoFolderName : sanitized;
    }

    /// <summary>
    /// 获取当前活跃仓库对应的 repo_updated.json 路径（位于仓库文件夹内部）
    /// </summary>
    public static string RepoUpdatedJsonPath => Path.Combine(CenterRepoPath, "repo_updated.json");

    /// <summary>
    /// 根据仓库文件夹名获取对应的 repo_updated.json 路径（位于仓库文件夹内部）
    /// </summary>
    internal static string GetRepoUpdatedJsonPathForFolder(string repoFolderName)
    {
        return Path.Combine(ReposPath, repoFolderName, "repo_updated.json");
    }

    // URL → 文件夹名 映射文件路径
    private static readonly string FolderMappingPath = Path.Combine(ReposPath, "repo_folder_mapping.json");

    /// <summary>
    /// 缓存的 URL→文件夹名 映射，避免每次访问 CenterRepoPath 都读磁盘
    /// </summary>
    private static Dictionary<string, string>? _folderMappingCache;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// 从磁盘读取映射文件（不加锁，调用方负责加锁）
    /// </summary>
    private static Dictionary<string, string>? ReadFolderMappingFromDisk()
    {
        try
        {
            if (File.Exists(FolderMappingPath))
            {
                var json = File.ReadAllText(FolderMappingPath);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScriptRepoUpdater] 读取映射文件失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 加载 URL→文件夹名 映射（带内存缓存，线程安全）
    /// </summary>
    private static Dictionary<string, string> LoadFolderMapping()
    {
        lock (_cacheLock)
        {
            if (_folderMappingCache != null)
                return new Dictionary<string, string>(_folderMappingCache);

            _folderMappingCache = ReadFolderMappingFromDisk() ?? new();
            return new Dictionary<string, string>(_folderMappingCache);
        }
    }

    /// <summary>
    /// 保存 URL→文件夹名 映射（同时刷新内存缓存，线程安全）
    /// </summary>
    private static void SaveFolderMapping(string url, string folderName)
    {
        try
        {
            if (!Directory.Exists(ReposPath)) Directory.CreateDirectory(ReposPath);
            lock (_cacheLock)
            {
                var mapping = ReadFolderMappingFromDisk()
                    ?? (_folderMappingCache != null ? new Dictionary<string, string>(_folderMappingCache) : new());

                mapping[url.TrimEnd('/')] = folderName;
                // 先写磁盘，成功后再更新缓存，确保一致性
                var jsonOut = Newtonsoft.Json.JsonConvert.SerializeObject(mapping, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(FolderMappingPath, jsonOut);
                _folderMappingCache = mapping;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存仓库文件夹映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成不重复的文件夹名（使用数字后缀 _1, _2, ...）
    /// </summary>
    private static string GenerateUniqueFolderName(string baseName)
    {
        for (int i = 1; i < 100; i++)
        {
            var candidate = $"{baseName}_{i}";
            var candidatePath = Path.Combine(ReposPath, candidate);
            if (!Directory.Exists(candidatePath))
                return candidate;
        }
        // 极端情况：100个同名文件夹都被占用，使用更大的数字
        return $"{baseName}_{DateTime.Now.Ticks}";
    }

    /// <summary>
    /// 从映射中移除指定 URL 的条目（线程安全）
    /// </summary>
    private static void RemoveFolderMapping(string url)
    {
        try
        {
            lock (_cacheLock)
            {
                var mapping = ReadFolderMappingFromDisk();
                if (mapping == null) return;

                var trimmed = url.TrimEnd('/');
                if (!mapping.Remove(trimmed)) return;

                var jsonOut = Newtonsoft.Json.JsonConvert.SerializeObject(mapping, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(FolderMappingPath, jsonOut);
                _folderMappingCache = mapping;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"移除仓库映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 重置当前活跃仓库（带写锁，同时清理映射条目）
    /// </summary>
    public async Task ResetCurrentRepoAsync()
    {
        await _repoWriteLock.WaitAsync();
        try
        {
            var config = TaskContext.Instance().Config.ScriptConfig;
            var repoUrl = ResolveRepoUrl(config);
            var repoPath = CenterRepoPath;

            if (Directory.Exists(repoPath))
            {
                DirectoryHelper.DeleteReadOnlyDirectory(repoPath);
            }

            // 清理 URL → 文件夹名 映射
            if (!string.IsNullOrEmpty(repoUrl))
            {
                RemoveFolderMapping(repoUrl);
            }
        }
        finally
        {
            _repoWriteLock.Release();
        }
    }

    public async Task<(string, bool)> UpdateCenterRepoByGit(string repoUrl, CheckoutProgressHandler? onCheckoutProgress)
    {
        await _repoWriteLock.WaitAsync();
        try
        {
            return await UpdateCenterRepoByGitCore(repoUrl, onCheckoutProgress);
        }
        finally
        {
            _repoWriteLock.Release();
        }
    }

    private async Task<(string, bool)> UpdateCenterRepoByGitCore(string repoUrl, CheckoutProgressHandler? onCheckoutProgress)
    {
        if (string.IsNullOrEmpty(repoUrl))
        {
            throw new ArgumentException("仓库URL不能为空", nameof(repoUrl));
        }

        var repoPath = Path.Combine(ReposPath, GetRepoFolderName(repoUrl));
        var updated = false;

        // 备份相关变量
        string? oldRepoJsonContent = null;

        await Task.Run(() =>
        {
            Repository? repo = null;
            try
            {
                GlobalSettings.SetOwnerValidation(false);
                if (!Directory.Exists(repoPath))
                {
                    // 如果仓库不存在，执行浅克隆操作
                    _logger.LogInformation($"浅克隆仓库: {repoUrl} 到 {repoPath}");

                    CloneRepository(repoUrl, repoPath, "release", onCheckoutProgress);
                    SaveFolderMapping(repoUrl.TrimEnd('/'), Path.GetFileName(repoPath));
                    updated = true;
                }
                else
                {
                    try
                    {
                        // 检测repo.json是否存在，存在则备份
                        var oldRepoJsonPath = Path.Combine(repoPath, "repo.json");
                        if (File.Exists(oldRepoJsonPath))
                        {
                            oldRepoJsonContent = File.ReadAllText(oldRepoJsonPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "备份repo.json失败，继续更新仓库");
                    }

                    // 检查是否为有效的Git仓库
                    if (!Repository.IsValid(repoPath))
                    {
                        Toast.Error($"不是有效的Git仓库，将重新克隆");
                        UIDispatcherHelper.Invoke(() => Toast.Error("不是有效的Git仓库，将重新克隆"));
                        CloneRepository(repoUrl, repoPath, "release", onCheckoutProgress);
                        updated = true;
                        return;
                    }

                    repo = new Repository(repoPath);

                    // 检查远程URL是否需要更新
                    var origin = repo.Network.Remotes["origin"];
                    if (origin.Url != repoUrl)
                    {
                        // 远程URL已更改，克隆到临时文件夹后基于目录结构重合度决定存放位置
                        _logger.LogInformation($"远程URL已更改: 从 {origin.Url} 到 {repoUrl}");
                        repo?.Dispose();
                        repo = null;

                        var tempPath = repoPath + "_temp_" + Guid.NewGuid().ToString("N")[..8];
                        // Step 1: 克隆到临时文件夹
                        bool cloneSucceeded = false;
                        try
                        {
                            CloneRepository(repoUrl, tempPath, "release", onCheckoutProgress);
                            cloneSucceeded = true;
                        }
                        catch (Exception cloneEx)
                        {
                            _logger.LogError(cloneEx, "克隆到临时文件夹失败，保留原仓库");
                            if (Directory.Exists(tempPath))
                                DirectoryHelper.DeleteReadOnlyDirectory(tempPath);
                        }

                        // Step 2: 基于目录结构重合度决定存放位置
                        if (cloneSucceeded)
                        {
                            try
                            {
                                var newTempRepoJson = Directory.GetFiles(tempPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                                var newContent = newTempRepoJson != null ? File.ReadAllText(newTempRepoJson) : null;

                                double overlapRatio = 0;
                                if (!string.IsNullOrEmpty(oldRepoJsonContent) && !string.IsNullOrEmpty(newContent))
                                {
                                    overlapRatio = CalculateRepoOverlapRatio(oldRepoJsonContent, newContent);
                                }

                                if (overlapRatio >= 0.5)
                                {
                                    // 目录结构重合度高 → 同一仓库的不同镜像，替换原文件夹
                                    _logger.LogInformation("目录结构重合度 {Ratio:P0}，判定为同一仓库镜像，替换原文件夹", overlapRatio);
                                    DirectoryHelper.DeleteReadOnlyDirectory(repoPath);
                                    Directory.Move(tempPath, repoPath);
                                    SaveFolderMapping(repoUrl.TrimEnd('/'), Path.GetFileName(repoPath));
                                }
                                else
                                {
                                    // 目录结构重合度低 → 不同仓库，创建新文件夹
                                    var baseName = DeriveBaseFolderName(repoUrl.TrimEnd('/'));
                                    var uniqueName = GenerateUniqueFolderName(baseName);
                                    var newRepoPath = Path.Combine(ReposPath, uniqueName);
                                    _logger.LogInformation("目录结构重合度 {Ratio:P0}，判定为不同仓库，创建新文件夹: {Folder}", overlapRatio, uniqueName);
                                    Directory.Move(tempPath, newRepoPath);
                                    repoPath = newRepoPath;
                                    SaveFolderMapping(repoUrl.TrimEnd('/'), uniqueName);
                                }
                            }
                            catch (Exception moveEx)
                            {
                                _logger.LogError(moveEx, "处理临时文件夹失败，清理临时目录，保留原仓库");
                                if (Directory.Exists(tempPath))
                                    DirectoryHelper.DeleteReadOnlyDirectory(tempPath);
                                cloneSucceeded = false; // move 失败，视为未更新
                            }
                        }

                        updated = cloneSucceeded;
                        return;
                    }

                    // 直接获取远程分支的 Commit SHA
                    var remoteReferences = repo.Network.ListReferences(repoUrl, CreateCredentialsHandler());
                    var remoteBranch = remoteReferences.FirstOrDefault(r => r.CanonicalName == "refs/heads/release");

                    if (remoteBranch == null)
                    {
                        throw new Exception("未找到远程release分支");
                    }

                    var remoteCommitSha = remoteBranch.TargetIdentifier;
                    var currentCommitSha = repo.Branches["release"]?.Tip?.Sha;

                    // 比较本地和远程commit
                    if (currentCommitSha == remoteCommitSha)
                    {
                        _logger.LogInformation("本地仓库已是最新版本，无需更新");
                        updated = false;
                    }
                    else
                    {
                        _logger.LogInformation($"检测到远程更新: 本地 {currentCommitSha?[..7] ?? "无"} -> 远程 {remoteCommitSha[..7]}");
                        repo?.Dispose();
                        repo = null;
                        CloneRepository(repoUrl, repoPath, "release", onCheckoutProgress);
                        updated = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git仓库更新失败");
                UIDispatcherHelper.Invoke(() => Toast.Error("脚本仓库更新异常，直接删除后重新克隆\n原因：" + ex.Message));
                repo?.Dispose();
                repo = null;
                CloneRepository(repoUrl, repoPath, "release", onCheckoutProgress);
                updated = true;
            }
            finally
            {
                repo?.Dispose();
            }
        });

        // 标记新repo.json中的更新节点
        try
        {
            // 查找repo.json文件
            var newRepoJsonPath = Directory.GetFiles(repoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(newRepoJsonPath))
            {
                var newRepoJsonContent = await File.ReadAllTextAsync(newRepoJsonPath);

                // 检查是否存在repo_updated.json，如果存在则直接与它比对
                var repoFolderName = GetRepoFolderName(repoUrl);
                var repoUpdateJsonPath = GetRepoUpdatedJsonPathForFolder(repoFolderName);
                string updatedContent;

                if (File.Exists(repoUpdateJsonPath))
                {
                    var repoUpdateContent = await File.ReadAllTextAsync(repoUpdateJsonPath);

                    // 检查目录结构重合度，低于阈值则判定为不同仓库，不继承旧的更新标记
                    var overlapRatio = CalculateRepoOverlapRatio(repoUpdateContent, newRepoJsonContent);
                    if (overlapRatio < 0.5)
                    {
                        _logger.LogInformation("仓库目录结构重合度低 ({Ratio:P0})，判定为不同仓库，不继承旧的更新标记", overlapRatio);
                        updatedContent = newRepoJsonContent;
                    }
                    else
                    {
                        updatedContent = AddUpdateMarkersToNewRepo(repoUpdateContent, newRepoJsonContent);
                    }
                }
                else if (!string.IsNullOrEmpty(oldRepoJsonContent))
                {
                    // 如果没有repo_updated.json，则使用备份的旧内容进行比对
                    var overlapRatio = CalculateRepoOverlapRatio(oldRepoJsonContent, newRepoJsonContent);
                    if (overlapRatio < 0.5)
                    {
                        _logger.LogInformation("仓库目录结构重合度低 ({Ratio:P0})，判定为不同仓库，不继承旧的更新标记", overlapRatio);
                        updatedContent = newRepoJsonContent;
                    }
                    else
                    {
                        updatedContent = AddUpdateMarkersToNewRepo(oldRepoJsonContent, newRepoJsonContent);
                    }
                }
                else
                {
                    // 全新仓库，无旧内容可比对
                    updatedContent = newRepoJsonContent;
                }

                // 保存到按仓库区分的 repo_updated 文件
                var updatedRepoJsonPath = GetRepoUpdatedJsonPathForFolder(repoFolderName);
                await File.WriteAllTextAsync(updatedRepoJsonPath, updatedContent);
                _logger.LogInformation($"已标记repo.json中的更新节点并保存到: {updatedRepoJsonPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "标记repo.json更新节点失败");
        }

        return (repoPath, updated);
    }

    /// <summary>
    /// 计算两个 repo.json 的目录结构重合度（基于目录路径的 Overlap Coefficient）
    /// 用于判断是否为同一仓库的不同版本还是完全不同的仓库
    /// </summary>
    /// <param name="oldContent">旧版 repo.json 内容</param>
    /// <param name="newContent">新版 repo.json 内容</param>
    /// <returns>重合度 0.0 ~ 1.0，解析失败返回 -1.0</returns>
    private double CalculateRepoOverlapRatio(string oldContent, string newContent)
    {
        try
        {
            var oldJson = JObject.Parse(oldContent);
            var newJson = JObject.Parse(newContent);

            var oldPaths = new HashSet<string>();
            var newPaths = new HashSet<string>();

            CollectDirectoryPaths(oldJson["indexes"] as JArray, "", oldPaths);
            CollectDirectoryPaths(newJson["indexes"] as JArray, "", newPaths);

            if (oldPaths.Count == 0 && newPaths.Count == 0) return 1.0;
            if (oldPaths.Count == 0 || newPaths.Count == 0) return 0.0;

            var intersection = oldPaths.Intersect(newPaths).Count();
            var minCount = Math.Min(oldPaths.Count, newPaths.Count);

            // 使用 Overlap Coefficient: intersection / min(|A|, |B|)
            // 对仓库正常增长（目录只增不减）更加宽容
            var ratio = minCount > 0 ? (double)intersection / minCount : 0.0;
            _logger.LogDebug("仓库目录结构重合度(Overlap): {Ratio:P0} (旧 {OldCount} 个目录, 新 {NewCount} 个目录, 交集 {Intersection} 个)",
                ratio, oldPaths.Count, newPaths.Count, intersection);
            return ratio;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "计算仓库重合度失败（JSON 解析异常）");
            return -1.0;
        }
    }

    /// <summary>
    /// 递归收集 indexes 中的所有目录节点路径（仅 type == "directory" 的节点）
    /// 只用目录结构判断仓库重合度，不受具体文件增删影响
    /// </summary>
    private void CollectDirectoryPaths(JArray? nodes, string prefix, HashSet<string> paths)
    {
        if (nodes == null) return;

        foreach (var node in nodes.OfType<JObject>())
        {
            var name = node["name"]?.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            if (node["type"]?.ToString() != "directory") continue;

            var fullPath = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
            paths.Add(fullPath);

            if (node["children"] is JArray children && children.Count > 0)
            {
                CollectDirectoryPaths(children, fullPath, paths);
            }
        }
    }

    /// <summary>
    /// 在新repo.json中添加更新标记
    /// </summary>
    /// <param name="oldContent">旧版repo.json内容</param>
    /// <param name="newContent">新版repo.json内容</param>
    /// <returns>添加了hasUpdate标记的新repo.json内容</returns>
    private string AddUpdateMarkersToNewRepo(string oldContent, string newContent)
    {
        try
        {
            var oldJson = JObject.Parse(oldContent);
            var newJson = JObject.Parse(newContent);

            if (oldJson["indexes"] is JArray oldIndexes && newJson["indexes"] is JArray newIndexes)
            {
                foreach (var newIndex in newIndexes)
                {
                    if (newIndex is JObject newIndexObj)
                    {
                        MarkNodeUpdates(newIndexObj, oldIndexes);
                    }
                }
            }

            return newJson.ToString(Newtonsoft.Json.Formatting.Indented);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "标记repo.json更新失败，返回原内容");
            return newContent;
        }
    }

    /// <summary>
    /// 直接在新版节点上标记更新
    /// </summary>
    /// <param name="newNode">新版节点</param>
    /// <param name="oldNodes">老版节点数组</param>
    /// <returns>是否有更新（节点本身或子树）</returns>
    private bool MarkNodeUpdates(JObject newNode, JArray oldNodes)
    {
        var newName = newNode["name"]?.ToString();
        if (string.IsNullOrEmpty(newName))
            return false;

        // 在老版节点中查找对应的节点
        var oldNode = oldNodes.FirstOrDefault(n => n is JObject obj && obj["name"]?.ToString() == newName) as JObject;

        bool hasDirectUpdate = false;
        bool hasChildUpdate = false;

        // 检查节点本身是否有更新
        if (oldNode != null)
        {
            // 若历史上已标记，则保留该标记
            if (IsTruthy(oldNode["hasUpdate"]))
            {
                newNode["hasUpdate"] = true;
                hasDirectUpdate = true;
            }

            // 对比时间戳
            var oldTime = ParseLastUpdated(oldNode["lastUpdated"]?.ToString());
            var newTime = ParseLastUpdated(newNode["lastUpdated"]?.ToString());

            if (newTime > oldTime)
            {
                newNode["hasUpdate"] = true;
                hasDirectUpdate = true;
            }
        }
        else
        {
            newNode["hasUpdate"] = true;
            hasDirectUpdate = true;
        }

        // 处理子节点
        if (newNode["children"] is JArray newChildren && newChildren.Count > 0)
        {
            var oldChildren = oldNode?["children"] as JArray ?? new JArray();

            // 遍历新版的每个子节点
            foreach (var newChild in newChildren)
            {
                if (newChild is JObject newChildObj)
                {
                    bool childHasUpdate = MarkNodeUpdates(newChildObj, oldChildren);
                    if (childHasUpdate)
                    {
                        hasChildUpdate = true;

                        // 如果是叶子节点更新，父节点也标记更新
                        var isLeafChild = newChildObj["children"] == null ||
                                          !((JArray?)newChildObj["children"])?.Any() == true;
                        if (isLeafChild && IsTruthy(newChildObj["hasUpdate"]))
                        {
                            var parentTime = ParseLastUpdated(newNode["lastUpdated"]?.ToString());
                            var childTime = ParseLastUpdated(newChildObj["lastUpdated"]?.ToString());

                            newNode["hasUpdate"] = true;
                            hasDirectUpdate = true;

                            if (childTime > parentTime && newChildObj["lastUpdated"] != null)
                            {
                                newNode["lastUpdated"] = newChildObj["lastUpdated"];
                            }
                        }
                    }
                }
            }
        }

        return hasDirectUpdate || hasChildUpdate;
    }


    /// <summary>
    /// 解析lastUpdated时间戳
    /// </summary>
    /// <param name="timeString">时间字符串</param>
    /// <returns>DateTime对象</returns>
    private DateTime ParseLastUpdated(string? timeString)
    {
        if (string.IsNullOrEmpty(timeString))
            return DateTime.MinValue;

        try
        {
            if (DateTime.TryParse(timeString, out var result))
                return result;

            return DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private bool IsTruthy(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return false;
        if (token.Type == JTokenType.Boolean) return (bool)token;
        if (token.Type == JTokenType.String) return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static void SimpleCloneRepository(string repoUrl, string repoPath,
        CheckoutProgressHandler? onCheckoutProgress)
    {
        var options = new CloneOptions
        {
            BranchName = "release", // 指定分支
            Checkout = true,
            IsBare = false,
            RecurseSubmodules = false, // 不递归克隆子模块
            OnCheckoutProgress = onCheckoutProgress
        };
        // options.FetchOptions.Depth = 1; // 浅克隆，只获取最新的提交
        // 设置凭据处理器
        options.FetchOptions.CredentialsProvider = Instance.CreateCredentialsHandler();
        options.FetchOptions.OnTransferProgress = progress =>
        {
            onCheckoutProgress?.Invoke($"拉取对象 {progress.ReceivedObjects}/{progress.TotalObjects}", progress.ReceivedObjects, progress.TotalObjects);
            return true;
        };
        // 克隆仓库
        Repository.Clone(repoUrl, repoPath, options);
    }

    /// <summary>
    /// 克隆Git仓库（只检出repo.json）
    /// 相当于 Repository.Clone(repoUrl, repoPath, options);
    /// 用这个方法可以无视本地代理
    /// </summary>
    /// <param name="repoUrl"></param>
    /// <param name="repoPath"></param>
    /// <param name="branchName"></param>
    /// <param name="onCheckoutProgress"></param>
    /// <exception cref="Exception"></exception>
    private void CloneRepository(string repoUrl, string repoPath, string branchName, CheckoutProgressHandler? onCheckoutProgress)
    {
        DirectoryHelper.DeleteReadOnlyDirectory(repoPath);
        Directory.CreateDirectory(repoPath);
        Repository.Init(repoPath);

        var repo = new Repository(repoPath);

        try
        {
            GitConfig(repo);

            // 添加远程源
            Remote remote = repo.Network.Remotes.Add("origin", repoUrl);

            // 只拉取指定分支
            var fetchOptions = new FetchOptions
            {
                TagFetchMode = TagFetchMode.None,
                ProxyOptions = { ProxyType = ProxyType.None },
                Depth = 1, // 浅拉取，只获取最新的提交
                CredentialsProvider = CreateCredentialsHandler(), // 添加凭据处理器
                OnTransferProgress = progress =>
                {
                    onCheckoutProgress?.Invoke($"拉取对象 {progress.ReceivedObjects}/{progress.TotalObjects}", progress.ReceivedObjects, progress.TotalObjects);
                    return true;
                }
            };
            string refSpec = $"+refs/heads/{branchName}:refs/remotes/origin/{branchName}";
            Commands.Fetch(repo, remote.Name, new[] { refSpec }, fetchOptions, "初始化拉取");

            // 获取远程分支
            var remoteBranch = repo.Branches[$"origin/{branchName}"];
            if (remoteBranch == null)
                throw new Exception($"远程仓库中未找到 {branchName} 分支");

            // 创建本地分支
            var localBranch = repo.CreateBranch(branchName, remoteBranch.Tip);
            repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);

            // 手动检出HEAD到新分支
            repo.Refs.UpdateTarget(repo.Refs.Head, localBranch.CanonicalName);

            // 手动检出 repo.json 文件
            CheckoutRepoJson(repo, remoteBranch.Tip);
        }
        finally
        {
            repo?.Dispose();
        }
    }

    /// <summary>
    /// 从Git仓库检出repo.json文件到工作目录
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="commit"></param>
    private void CheckoutRepoJson(Repository repo, Commit commit)
    {
        try
        {
            // 查找repo.json文件
            var repoJsonEntry = commit.Tree.FirstOrDefault(e => e.Name == "repo.json");
            if (repoJsonEntry != null && repoJsonEntry.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)repoJsonEntry.Target;
                var repoJsonPath = Path.Combine(repo.Info.WorkingDirectory ?? repo.Info.Path, "repo.json");

                // 创建目录（如果需要）
                var dir = Path.GetDirectoryName(repoJsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 写入文件
                using (var contentStream = blob.GetContentStream())
                using (var fileStream = File.Create(repoJsonPath))
                {
                    contentStream.CopyTo(fileStream);
                }
            }
            else
            {
                _logger.LogWarning("未在仓库中找到 repo.json 文件");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检出 repo.json 失败");
        }
    }

    /// <summary>
    /// 检查指定路径是否为 Git 仓库（非文件式仓库）
    /// </summary>
    /// <param name="repoPath">仓库路径</param>
    /// <returns>如果是 Git 仓库返回 true，否则返回 false</returns>
    private bool IsGitRepository(string repoPath)
    {
        return Repository.IsValid(repoPath) && !Directory.Exists(Path.Combine(repoPath, "repo"));
    }

    /// <summary>
    /// 获取仓库中 repo/ 子目录的树对象
    /// </summary>
    private Tree GetRepoSubdirectoryTree(Repository repo)
    {
        var commit = repo.Head?.Tip;
        if (commit == null)
        {
            throw new Exception("仓库HEAD未指向任何提交");
        }

        // 脚本内容都在 repo/ 子目录下
        var repoEntry = commit.Tree["repo"];
        if (repoEntry == null || repoEntry.TargetType != TreeEntryTargetType.Tree)
        {
            throw new Exception("仓库结构错误：未找到 repo/ 子目录");
        }

        return (Tree)repoEntry.Target;
    }

    /// <summary>
    /// 从中央仓库读取文件内容
    /// </summary>
    /// <param name="relPath">相对于仓库根目录的路径</param>
    /// <returns>文件内容，如果文件不存在则返回null</returns>
    public string? ReadFileFromCenterRepo(string relPath)
    {
        try
        {
            var repoPath = CenterRepoPath;

            // 判断是否为 Git 仓库
            bool isGitRepo = IsGitRepository(repoPath);

            if (isGitRepo)
            {
                return ReadFileFromGitRepository(repoPath, relPath);
            }
            else
            {
                // 文件式仓库：从文件系统读取
                var filePath = Path.Combine(repoPath, "repo", relPath);
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath);
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"从中央仓库读取文件失败: {relPath}");
            return null;
        }
    }

    /// <summary>
    /// 从中央仓库读取二进制文件
    /// </summary>
    /// <param name="relPath">相对于仓库根目录的路径</param>
    /// <returns>文件字节数组，如果文件不存在则返回null</returns>
    public byte[]? ReadBinaryFileFromCenterRepo(string relPath)
    {
        try
        {
            var repoPath = CenterRepoPath;

            // 判断是否为 Git 仓库
            bool isGitRepo = IsGitRepository(repoPath);

            if (isGitRepo)
            {
                return ReadBinaryFileFromGitRepository(repoPath, relPath);
            }
            else
            {
                // 文件式仓库：从文件系统读取
                var filePath = Path.Combine(repoPath, "repo", relPath);
                if (File.Exists(filePath))
                {
                    return File.ReadAllBytes(filePath);
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"从中央仓库读取二进制文件失败: {relPath}");
            return null;
        }
    }

    /// <summary>
    /// 从Git仓库读取文件内容
    /// </summary>
    private string? ReadFileFromGitRepository(string repoPath, string filePath)
    {
        try
        {
            // 判断是否为 Git 仓库
            bool isGitRepo = IsGitRepository(repoPath);
            if (!isGitRepo)
            {
                return null;
            }

            using var repo = new Repository(repoPath);

            var manifestPath = $"repo/{filePath}";
            var pathParts = manifestPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Tree currentTree = repo.Head.Tip!.Tree;
            TreeEntry? entry = null;

            for (int i = 0; i < pathParts.Length; i++)
            {
                entry = currentTree[pathParts[i]];
                if (entry == null)
                {
                    return null;
                }

                if (i < pathParts.Length - 1)
                {
                    if (entry.TargetType != TreeEntryTargetType.Tree)
                    {
                        return null;
                    }
                    currentTree = (Tree)entry.Target;
                }
            }

            if (entry == null || entry.TargetType != TreeEntryTargetType.Blob)
            {
                return null;
            }

            var blob = (Blob)entry.Target;
            using var contentStream = blob.GetContentStream();
            using var reader = new StreamReader(contentStream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"从Git仓库读取文件失败: {filePath}");
            return null;
        }
    }

    /// <summary>
    /// 从Git仓库读取二进制文件内容
    /// </summary>
    private byte[]? ReadBinaryFileFromGitRepository(string repoPath, string filePath)
    {
        try
        {
            // 判断是否为 Git 仓库
            bool isGitRepo = IsGitRepository(repoPath);
            if (!isGitRepo)
            {
                return null;
            }

            using var repo = new Repository(repoPath);

            var manifestPath = $"repo/{filePath}";
            var pathParts = manifestPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Tree currentTree = repo.Head.Tip!.Tree;
            TreeEntry? entry = null;

            for (int i = 0; i < pathParts.Length; i++)
            {
                entry = currentTree[pathParts[i]];
                if (entry == null)
                {
                    return null;
                }

                if (i < pathParts.Length - 1)
                {
                    if (entry.TargetType != TreeEntryTargetType.Tree)
                    {
                        return null;
                    }
                    currentTree = (Tree)entry.Target;
                }
            }

            if (entry == null || entry.TargetType != TreeEntryTargetType.Blob)
            {
                return null;
            }

            var blob = (Blob)entry.Target;
            using var contentStream = blob.GetContentStream();
            using var memoryStream = new MemoryStream();
            contentStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"从Git仓库读取二进制文件失败: {filePath}");
            return null;
        }
    }

    /// <summary>
    /// 从仓库中检出指定路径的文件或文件夹到目标位置
    /// </summary>
    /// <param name="repoPath">仓库路径</param>
    /// <param name="sourcePath">仓库中的相对路径</param>
    /// <param name="destPath">目标路径</param>
    private void CheckoutPath(string repoPath, string sourcePath, string destPath)
    {
        // 判断仓库类型：检查是否为 Git 仓库且不存在 repo/ 子目录
        bool isGitRepo = IsGitRepository(repoPath);

        if (isGitRepo)
        {
            // 从Git仓库检出
            using var repo = new Repository(repoPath);
            var commit = repo.Head.Tip;

            if (commit == null)
            {
                _logger.LogError($"仓库HEAD未指向任何提交。HEAD: {repo.Head?.CanonicalName ?? "null"}");
                throw new Exception("仓库HEAD未指向任何提交");
            }

            // 递归查找路径
            TreeEntry? entry = null;
            var pathParts = sourcePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Tree currentTree = GetRepoSubdirectoryTree(repo);

            for (int i = 0; i < pathParts.Length; i++)
            {
                entry = currentTree[pathParts[i]];
                if (entry == null)
                {
                    // 调试信息：列出当前树中的所有条目
                    // var availableEntries = string.Join(", ", currentTree.Select(e => e.Name));
                    // _logger.LogError($"在路径 '{string.Join("/", pathParts.Take(i))}' 中未找到 '{pathParts[i]}'");
                    // _logger.LogError($"可用的条目: {availableEntries}");
                    // throw new Exception($"仓库中不存在路径: {sourcePath}");
                    return;
                }

                if (i < pathParts.Length - 1)
                {
                    if (entry.TargetType != TreeEntryTargetType.Tree)
                    {
                        throw new Exception($"路径中间部分不是目录: {string.Join("/", pathParts.Take(i + 1))}");
                    }
                    currentTree = (Tree)entry.Target;
                }
            }

            // 检出文件或目录
            if (entry == null)
            {
                // throw new Exception($"未找到路径: {sourcePath}");
                return;
            }

            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                // 检出单个文件
                var blob = (Blob)entry.Target;

                // 确保目标目录存在
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var contentStream = blob.GetContentStream())
                using (var fileStream = File.Create(destPath))
                {
                    contentStream.CopyTo(fileStream);
                }
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // 检出目录
                var tree = (Tree)entry.Target;
                CheckoutTree(tree, destPath, sourcePath);
            }
        }
        else
        {
            // 文件式仓库：从文件系统复制
            var scriptPath = Path.Combine(repoPath, sourcePath);

            if (Directory.Exists(scriptPath))
            {
                // 复制目录
                CopyDirectory(scriptPath, destPath);
            }
            else if (File.Exists(scriptPath))
            {
                // 复制文件
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.Copy(scriptPath, destPath, true);
            }
            else
            {
                throw new Exception($"仓库中不存在路径: {sourcePath}");
            }
        }
    }

    /// <summary>
    /// 递归检出树对象
    /// </summary>
    private void CheckoutTree(Tree tree, string destPath, string currentPath)
    {
        if (!Directory.Exists(destPath))
        {
            Directory.CreateDirectory(destPath);
        }

        foreach (var entry in tree)
        {
            var entryDestPath = Path.Combine(destPath, entry.Name);
            var entrySourcePath = $"{currentPath}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)entry.Target;
                using var contentStream = blob.GetContentStream();
                using var fileStream = File.Create(entryDestPath);
                contentStream.CopyTo(fileStream);
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                var subTree = (Tree)entry.Target;
                CheckoutTree(subTree, entryDestPath, entrySourcePath);
            }
        }
    }

    private void GitConfig(Repository repo)
    {
        // 设置 Git 配置
        // 1. 设置 core.longpaths 为 true
        repo.Config.Set("core.longpaths", true);

        // 2. 添加 safe.directory *
        repo.Config.Set("safe.directory", "*");

        // 3. 移除 http.proxy 和 https.proxy 配置
        repo.Config.Unset("http.proxy");
        repo.Config.Unset("https.proxy");
    }

    /// <summary>
    /// 创建凭据处理器（用于私有仓库身份验证）
    /// </summary>
    /// <returns>凭据处理器</returns>
    private CredentialsHandler? CreateCredentialsHandler()
    {
        // 从凭据管理器读取 Git 凭据
        var credential = CredentialManagerHelper.ReadCredential("BetterGenshinImpact.GitCredentials");


        // 返回凭据处理器
        return (url, usernameFromUrl, types) =>
        {
            _logger.LogInformation($"使用配置的Git凭据进行身份验证");
            return new UsernamePasswordCredentials
            {
                Username = credential?.UserName ?? "",
                Password = credential?.Password ?? ""
            };
        };
    }

    // [Obsolete]
    // public async Task<(string, bool)> UpdateCenterRepo()
    // {
    //     // 测速并获取信息
    //     var (fastUrl, jsonString) = await ProxySpeedTester.GetFastestUrlAsync(CenterRepoInfoUrls);
    //     if (string.IsNullOrEmpty(jsonString))
    //     {
    //         throw new Exception("从互联网下载最新的仓库信息失败");
    //     }
    //
    //     var (time, url, file) = ParseJson(jsonString);
    //
    //     var updated = false;
    //
    //     // 检查仓库是否存在，不存在则下载
    //     var needDownload = false;
    //     if (Directory.Exists(CenterRepoPath))
    //     {
    //         var p = Directory.GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
    //         if (p is null)
    //         {
    //             needDownload = true;
    //         }
    //     }
    //     else
    //     {
    //         needDownload = true;
    //     }
    //
    //     if (needDownload)
    //     {
    //         await DownloadRepoAndUnzip(url);
    //         updated = true;
    //     }
    //
    //     // 搜索本地的 repo.json
    //     var localRepoJsonPath = Directory.GetFiles(CenterRepoPath, file, SearchOption.AllDirectories).FirstOrDefault();
    //     if (localRepoJsonPath is null)
    //     {
    //         throw new Exception("本地仓库缺少 repo.json");
    //     }
    //
    //     var (time2, url2, file2) = ParseJson(await File.ReadAllTextAsync(localRepoJsonPath));
    //
    //     // 检查是否需要更新
    //     if (long.Parse(time) > long.Parse(time2))
    //     {
    //         await DownloadRepoAndUnzip(url2);
    //         updated = true;
    //     }
    //
    //     // 获取与 localRepoJsonPath 同名（无扩展名）的文件夹路径
    //     var folderName = Path.GetFileNameWithoutExtension(localRepoJsonPath);
    //     var folderPath = Path.Combine(Path.GetDirectoryName(localRepoJsonPath)!, folderName);
    //     if (!Directory.Exists(folderPath))
    //     {
    //         throw new Exception("本地仓库文件夹不存在");
    //     }
    //
    //     return (folderPath, updated);
    // }

    public string FindCenterRepoPath()
    {
        // 查找 repo.json 文件
        var repoJsonPath = Path.Combine(CenterRepoPath, "repo.json");
        string? repoJsonDir = null;

        if (File.Exists(repoJsonPath))
        {
            repoJsonDir = CenterRepoPath;
        }
        else
        {
            // 递归查找 repo.json
            var localRepoJsonPath = Directory
                .GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (localRepoJsonPath != null)
            {
                repoJsonDir = Path.GetDirectoryName(localRepoJsonPath);
            }
        }

        if (string.IsNullOrEmpty(repoJsonDir))
        {
            throw new Exception("本地仓库缺少 repo.json");
        }

        // 检查 repo.json 同级是否存在 repo/ 目录来判断仓库类型
        var repoSubDir = Path.Combine(repoJsonDir, "repo");
        if (Directory.Exists(repoSubDir))
        {
            // 存在 repo/ 目录，说明是文件式仓库
            return repoSubDir;
        }
        else
        {
            // 不存在 repo/ 目录，说明是 Git 仓库
            return repoJsonDir;
        }
    }

    private (string time, string url, string file) ParseJson(string jsonString)
    {
        var json = JObject.Parse(jsonString);
        var time = json["time"]?.ToString();
        var url = json["url"]?.ToString();
        var file = json["file"]?.ToString();
        // 检查是否有空值
        if (time is null || url is null || file is null)
        {
            throw new Exception("repo.json 解析失败");
        }

        return (time, url, file);
    }

    /// <summary>
    /// 统一的本地 zip 导入方法
    /// 解压后自动识别仓库内容，基于目录结构重合度决定覆盖已有仓库还是创建新文件夹，
    /// 并生成 repo_updated.json 更新标记
    /// </summary>
    /// <param name="zipFilePath">本地 zip 文件路径</param>
    /// <param name="onProgress">进度回调 (0-100, 描述文本)</param>
    /// <returns>导入后的仓库文件夹路径</returns>
    public async Task<string> ImportLocalRepoZip(string zipFilePath, Action<int, string>? onProgress = null)
    {
        await _repoWriteLock.WaitAsync();
        try
        {
            return await ImportLocalRepoZipCore(zipFilePath, onProgress);
        }
        finally
        {
            _repoWriteLock.Release();
        }
    }

    private async Task<string> ImportLocalRepoZipCore(string zipFilePath, Action<int, string>? onProgress = null)
    {
        var tempUnzipDir = Path.Combine(ReposTempPath, "importZipFile");
        string targetFolderName = CenterRepoFolderName;

        try
        {
            // 阶段1: 准备 (0-10%)
            onProgress?.Invoke(0, "正在准备导入环境...");
            DirectoryHelper.DeleteReadOnlyDirectory(ReposTempPath);
            Directory.CreateDirectory(tempUnzipDir);
            onProgress?.Invoke(10, "准备完成，开始解压文件...");

            // 阶段2: 解压 (10-50%)
            await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, tempUnzipDir, true));
            onProgress?.Invoke(50, "文件解压完成，正在验证仓库结构...");

            // 阶段3: 查找 repo.json (50-55%)
            var repoJsonPath = Directory.GetFiles(tempUnzipDir, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (repoJsonPath == null)
            {
                throw new FileNotFoundException("未找到 repo.json 文件，不是有效的脚本仓库压缩包。");
            }

            var repoDir = Path.GetDirectoryName(repoJsonPath)!;
            var newRepoJsonContent = await File.ReadAllTextAsync(repoJsonPath);
            onProgress?.Invoke(55, "仓库结构验证通过，正在分析仓库内容...");

            // 阶段4: 基于目录结构重合度决定目标文件夹 (55-70%)
            string? bestMatchFolder = null;
            double bestOverlap = 0;

            // 扫描已有仓库，找目录结构重合度最高的
            if (Directory.Exists(ReposPath))
            {
                foreach (var existingDir in Directory.GetDirectories(ReposPath))
                {
                    try
                    {
                        var dirName = Path.GetFileName(existingDir);
                        if (dirName == "Temp") continue;

                        // 尝试读取已有仓库的 repo.json 或 repo_updated.json
                        var existingRepoUpdated = Path.Combine(existingDir, "repo_updated.json");
                        var existingRepoJson = Directory.GetFiles(existingDir, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                        var existingContent = File.Exists(existingRepoUpdated)
                            ? await File.ReadAllTextAsync(existingRepoUpdated)
                            : (existingRepoJson != null ? await File.ReadAllTextAsync(existingRepoJson) : null);

                        if (!string.IsNullOrEmpty(existingContent))
                        {
                            var overlap = CalculateRepoOverlapRatio(existingContent, newRepoJsonContent);
                            if (overlap > bestOverlap)
                            {
                                bestOverlap = overlap;
                                bestMatchFolder = dirName;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Zip导入：扫描仓库目录 {Dir} 时出错，跳过", existingDir);
                    }
                }
            }

            onProgress?.Invoke(65, "内容分析完成，正在确定目标位置...");

            string targetPath;
            string? oldRepoContent = null;

            if (bestOverlap >= 0.5 && bestMatchFolder != null)
            {
                // 高重合度 → 覆盖已有仓库
                targetFolderName = bestMatchFolder;
                targetPath = Path.Combine(ReposPath, targetFolderName);
                _logger.LogInformation("Zip导入：目录结构重合度 {Ratio:P0}，覆盖已有仓库 {Folder}", bestOverlap, targetFolderName);

                // 读取旧的 repo_updated.json 用于生成更新标记
                var oldUpdatedPath = Path.Combine(targetPath, "repo_updated.json");
                if (File.Exists(oldUpdatedPath))
                {
                    oldRepoContent = await File.ReadAllTextAsync(oldUpdatedPath);
                }
                else
                {
                    var oldRepoJson = Directory.GetFiles(targetPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                    if (oldRepoJson != null)
                        oldRepoContent = await File.ReadAllTextAsync(oldRepoJson);
                }

                DirectoryHelper.DeleteReadOnlyDirectory(targetPath);
            }
            else if (bestOverlap < 0.5 && bestMatchFolder != null)
            {
                // 低重合度 → 全新仓库，创建新文件夹
                var baseName = CenterRepoFolderName;
                // 如果默认文件夹不存在，就用默认名
                if (!Directory.Exists(Path.Combine(ReposPath, baseName)))
                {
                    targetFolderName = baseName;
                }
                else
                {
                    targetFolderName = GenerateUniqueFolderName(baseName);
                }
                targetPath = Path.Combine(ReposPath, targetFolderName);
                _logger.LogInformation("Zip导入：目录结构重合度 {Ratio:P0}，创建新文件夹 {Folder}", bestOverlap, targetFolderName);
            }
            else
            {
                // 没有已有仓库，使用默认文件夹名
                targetPath = Path.Combine(ReposPath, targetFolderName);
                if (Directory.Exists(targetPath))
                {
                    // 读取旧内容用于生成更新标记
                    var oldUpdatedPath = Path.Combine(targetPath, "repo_updated.json");
                    if (File.Exists(oldUpdatedPath))
                        oldRepoContent = await File.ReadAllTextAsync(oldUpdatedPath);

                    DirectoryHelper.DeleteReadOnlyDirectory(targetPath);
                }
            }

            onProgress?.Invoke(70, "正在复制仓库文件...");

            // 阶段5: 拷贝仓库到目标位置 (70-90%)
            DirectoryHelper.CopyDirectory(repoDir, targetPath);
            onProgress?.Invoke(90, "仓库复制完成，正在生成更新标记...");

            // 阶段6: 生成 repo_updated.json (90-95%)
            try
            {
                var updatedJsonPath = Path.Combine(targetPath, "repo_updated.json");
                if (!string.IsNullOrEmpty(oldRepoContent))
                {
                    var overlapWithOld = CalculateRepoOverlapRatio(oldRepoContent, newRepoJsonContent);
                    if (overlapWithOld >= 0.5)
                    {
                        var updatedContent = AddUpdateMarkersToNewRepo(oldRepoContent, newRepoJsonContent);
                        await File.WriteAllTextAsync(updatedJsonPath, updatedContent);
                        _logger.LogInformation("Zip导入：已生成更新标记 repo_updated.json");
                    }
                    else
                    {
                        // 目录结构差异太大，直接使用新内容
                        await File.WriteAllTextAsync(updatedJsonPath, newRepoJsonContent);
                    }
                }
                else
                {
                    // 全新导入，直接使用新内容
                    await File.WriteAllTextAsync(updatedJsonPath, newRepoJsonContent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zip导入：生成 repo_updated.json 失败");
            }

            onProgress?.Invoke(95, "正在清理临时文件...");
        }
        finally
        {
            // 阶段7: 清理 (95-100%)
            DirectoryHelper.DeleteReadOnlyDirectory(ReposTempPath);
        }

        onProgress?.Invoke(100, "导入完成");
        _logger.LogInformation("Zip导入完成，目标文件夹: {Folder}", targetFolderName);
        return Path.Combine(ReposPath, targetFolderName);
    }

    public async Task DownloadRepoAndUnzip(string url)
    {
        await _repoWriteLock.WaitAsync();
        try
        {
            await DownloadRepoAndUnzipCore(url);
        }
        finally
        {
            _repoWriteLock.Release();
        }
    }

    private async Task DownloadRepoAndUnzipCore(string url)
    {
        // 下载
        var res = await _httpClient.GetAsync(url);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception("下载失败");
        }

        var bytes = await res.Content.ReadAsByteArrayAsync();

        // 获取文件名
        var contentDisposition = res.Content.Headers.ContentDisposition;
        var fileName = contentDisposition is { FileName: not null }
            ? contentDisposition.FileName.Trim('"')
            : "temp.zip";

        // 创建临时目录
        if (!Directory.Exists(ReposTempPath))
        {
            Directory.CreateDirectory(ReposTempPath);
        }

        // 保存下载的文件
        var zipPath = Path.Combine(ReposTempPath, fileName);
        await File.WriteAllBytesAsync(zipPath, bytes);

        // 删除旧文件夹
        if (Directory.Exists(CenterRepoPath))
        {
            DirectoryHelper.DeleteReadOnlyDirectory(CenterRepoPath);
        }

        // 使用 System.IO.Compression 解压
        ZipFile.ExtractToDirectory(zipPath, ReposPath, true);
    }

    public async Task ImportScriptFromClipboard(string clipboardText)
    {
        // 获取剪切板内容
        try
        {
            await ImportScriptFromUri(clipboardText, true);
        }
        catch (Exception e)
        {
            // 剪切板内容可能获取会失败
            Console.WriteLine(e);
        }
    }

    public async Task ImportScriptFromUri(string uri, bool formClipboard)
    {
        // 检查剪切板内容是否符合特定的URL格式
        if (!string.IsNullOrEmpty(uri) && uri.Trim().ToLower().StartsWith("bettergi://script?import="))
        {
            Debug.WriteLine($"脚本订购内容：{uri}");
            // 执行相关操作
            var pathJson = ParseUri(uri);
            if (!string.IsNullOrEmpty(pathJson))
            {
                var uiMessageBox = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "脚本订阅",
                    Content =
                        $"检测到{(formClipboard ? "剪切板上存在" : "")}脚本订阅链接，解析后需要导入的脚本为：{pathJson}。\n是否导入并覆盖此文件或者文件夹下的脚本？",
                    CloseButtonText = "关闭",
                    PrimaryButtonText = "确认导入",
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                uiMessageBox.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(uiMessageBox);

                var result = await uiMessageBox.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    await ImportScriptFromPathJson(pathJson);
                }

                // ContentDialog dialog = new()
                // {
                //     Title = "脚本订阅",
                //     Content = $"检测到{(formClipboard ? "剪切板上存在" : "")}脚本订阅链接，解析后需要导入的脚本为：{pathJson}。\n是否导入并覆盖此文件或者文件夹下的脚本？",
                //     CloseButtonText = "关闭",
                //     PrimaryButtonText = "确认导入",
                // };
                //
                // var result = await dialog.ShowAsync();
                // if (result == ContentDialogResult.Primary)
                // {
                //     await ImportScriptFromPathJson(pathJson);
                // }
            }

            if (formClipboard)
            {
                // 清空剪切板内容
                Clipboard.Clear();
            }
        }
    }

    private string? ParseUri(string uriString)
    {
        var uri = new Uri(uriString);

        // 获取 query 参数
        string query = uri.Query;
        Debug.WriteLine($"Query: {query}");

        // 解析 query 参数
        var queryParams = System.Web.HttpUtility.ParseQueryString(query);
        var import = queryParams["import"];
        if (string.IsNullOrEmpty(import))
        {
            Debug.WriteLine("未找到 import 参数");
            return null;
        }

        // Base64 解码后再使用url解码
        byte[] data = Convert.FromBase64String(import);
        return System.Web.HttpUtility.UrlDecode(System.Text.Encoding.UTF8.GetString(data));
    }

    public async Task ImportScriptFromPathJson(string pathJson)
    {
        await _repoWriteLock.WaitAsync();
        try
        {
            await ImportScriptFromPathJsonCore(pathJson);
        }
        finally
        {
            _repoWriteLock.Release();
        }
    }

    private async Task ImportScriptFromPathJsonCore(string pathJson)
    {
        var paths = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(pathJson);
        if (paths is null || paths.Count == 0)
        {
            Toast.Warning("订阅脚本路径为空");
            return;
        }

        // 保存订阅信息（按当前仓库存储到文件）
        AddSubscribedPathsForCurrentRepo(paths);

        Toast.Information("获取最新仓库信息中...");

        string repoPath;
        try
        {
            repoPath = FindCenterRepoPath();
        }
        catch
        {
            await ThemedMessageBox.ErrorAsync("本地无仓库信息，请至少成功更新一次脚本仓库信息！");
            return;
        }


        // // 收集将被覆盖的文件和文件夹
        // var filesToOverwrite = new List<string>();
        // foreach (var path in paths)
        // {
        //     var first = GetFirstFolder(path);
        //     if (PathMapper.TryGetValue(first, out var userPath))
        //     {
        //         var scriptPath = Path.Combine(repoPath, path);
        //         var destPath = Path.Combine(userPath, path.Replace(first, ""));
        //         if (Directory.Exists(scriptPath))
        //         {
        //             if (Directory.Exists(destPath))
        //             {
        //                 filesToOverwrite.Add(destPath);
        //             }
        //         }
        //         else if (File.Exists(scriptPath))
        //         {
        //             if (File.Exists(destPath))
        //             {
        //                 filesToOverwrite.Add(destPath);
        //             }
        //         }
        //     }
        //     else
        //     {
        //         Toast.Warning($"未知的脚本路径：{path}");
        //     }
        // }
        //
        // // 提示用户确认
        // if (filesToOverwrite.Count > 0)
        // {
        //     var message = "以下文件和文件夹将被覆盖:\n" + string.Join("\n", filesToOverwrite) + "\n是否覆盖所有文件和文件夹？";
        //     var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        //     {
        //         Title = "确认覆盖",
        //         Content = message,
        //         CloseButtonText = "取消",
        //         PrimaryButtonText = "确认覆盖",
        //         WindowStartupLocation = WindowStartupLocation.CenterOwner,
        //         Owner = Application.Current.MainWindow,
        //     };
        //
        //     var result = await uiMessageBox.ShowDialogAsync();
        //     if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
        //     {
        //         return;
        //     }
        // }

        //顶层目录订阅时，展开 "pathing" 等顶层路径为具体子目录
        List<string> newPaths = ExpandTopLevelPaths(paths, repoPath);

        // 从 Git 仓库检出文件到用户文件夹
        foreach (var path in newPaths)
        {
            var (first, remainingPath) = GetFirstFolderAndRemainingPath(path);
            if (PathMapper.TryGetValue(first, out var userPath))
            {
                var destPath = Path.Combine(userPath, remainingPath);

                // 备份需要保存的文件
                List<string> backupFiles = new List<string>();
                if (first == "js") // 只对JS脚本进行备份
                {
                    backupFiles = BackupScriptFiles(path, repoPath);
                }

                // 如果目标路径存在，先删除
                if (Directory.Exists(destPath))
                {
                    DirectoryHelper.DeleteDirectoryWithReadOnlyCheck(destPath);
                }
                else if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                // 从 Git 仓库检出文件或目录
                CheckoutPath(repoPath, path, destPath);

                // 图标处理（仅对目录）
                if (Directory.Exists(destPath))
                {
                    DealWithIconFolder(destPath);
                }

                // 恢复备份的文件
                if (first == "js" && backupFiles.Count > 0) // 只对JS脚本进行恢复
                {
                    RestoreScriptFiles(path, repoPath);
                }

                // Resolving dependencies for JS scripts
                if (first == "js")
                {
                    ResolveScriptDependencies(repoPath, destPath);
                }

                UpdateSubscribedScriptPaths();
                Toast.Success("脚本订阅链接导入完成");
            }
            else
            {
                Toast.Warning($"未知的脚本路径：{path}");
            }
        }
    }

    // ========== 文件级订阅路径存储 ==========
    // 订阅数据存储在 User/subscriptions/{repoFolderName}.json，与仓库目录和主配置解耦

    /// <summary>
    /// 订阅文件存储目录
    /// </summary>
    public static readonly string SubscriptionsPath = Global.Absolute(@"User\Subscriptions");

    /// <summary>
    /// 获取当前活跃仓库的文件夹名称
    /// </summary>
    private static string GetCurrentRepoFolderName()
    {
        return Path.GetFileName(CenterRepoPath);
    }

    /// <summary>
    /// 获取指定仓库的订阅文件路径
    /// </summary>
    private static string GetSubscriptionFilePath(string repoFolderName)
    {
        return Path.Combine(SubscriptionsPath, $"{repoFolderName}.json");
    }

    /// <summary>
    /// 获取当前仓库的已订阅脚本路径列表
    /// </summary>
    public static List<string> GetSubscribedPathsForCurrentRepo()
    {
        var filePath = GetSubscriptionFilePath(GetCurrentRepoFolderName());
        return ReadSubscriptionFile(filePath);
    }

    /// <summary>
    /// 设置当前仓库的已订阅脚本路径列表
    /// </summary>
    private static void SetSubscribedPathsForCurrentRepo(List<string> paths)
    {
        var filePath = GetSubscriptionFilePath(GetCurrentRepoFolderName());
        WriteSubscriptionFile(filePath, paths);
    }

    /// <summary>
    /// 向当前仓库的已订阅路径中追加新路径（自动去重）。
    /// 注意：内部的读-合并-写不是原子操作，调用方应持有 _repoWriteLock 以避免并发丢失更新。
    /// </summary>
    private static void AddSubscribedPathsForCurrentRepo(List<string> paths)
    {
        var existing = GetSubscribedPathsForCurrentRepo();
        var merged = existing.Union(paths).ToList();
        SetSubscribedPathsForCurrentRepo(merged);
    }

    /// <summary>
    /// 订阅文件读写锁，允许并发读、互斥写
    /// </summary>
    private static readonly ReaderWriterLockSlim _subscriptionRwLock = new();

    /// <summary>
    /// 从订阅文件读取路径列表
    /// </summary>
    private static List<string> ReadSubscriptionFile(string filePath)
    {
        _subscriptionRwLock.EnterReadLock();
        try
        {
            if (!File.Exists(filePath))
                return new List<string>();

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, ConfigService.JsonOptions) ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScriptRepoUpdater] 读取订阅文件失败: {filePath}，订阅数据可能已损坏: {ex.Message}");
            return new List<string>();
        }
        finally
        {
            _subscriptionRwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// 将路径列表写入订阅文件
    /// </summary>
    private static void WriteSubscriptionFile(string filePath, List<string> paths)
    {
        _subscriptionRwLock.EnterWriteLock();
        try
        {
            if (!Directory.Exists(SubscriptionsPath))
                Directory.CreateDirectory(SubscriptionsPath);

            if (paths.Count == 0)
            {
                // 空列表时删除文件
                if (File.Exists(filePath))
                    File.Delete(filePath);
                return;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(paths, ConfigService.JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScriptRepoUpdater] 写入订阅文件失败: {filePath}: {ex.Message}");
            throw; // 传播异常让调用方决定如何处理
        }
        finally
        {
            _subscriptionRwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 在启动时从旧 config.json 中的 subscribedScriptPaths 迁移到独立订阅文件。
    /// 通过框架反序列化读取旧数据，迁移后清空配置属性让框架自动保存。
    /// </summary>
    public void MigrateSubscribedPathsFromConfig()
    {
        try
        {
            // 如果订阅目录已存在且非空，说明已迁移过
            if (Directory.Exists(SubscriptionsPath) && Directory.GetFiles(SubscriptionsPath, "*.json").Length > 0)
                return;

            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
            var oldPaths = scriptConfig.SubscribedScriptPaths;
            if (oldPaths.Count == 0)
                return;

            // 默认归入当前仓库
            var repoFolderName = GetCurrentRepoFolderName();

            // 如果存在多个仓库，尝试按 repo.json 分配路径
            if (Directory.Exists(ReposPath))
            {
                var repoDirs = Directory.GetDirectories(ReposPath)
                    .Where(d => !Path.GetFileName(d).Equals("Temp", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (repoDirs.Count > 1)
                {
                    var repoPathSets = new Dictionary<string, HashSet<string>>();
                    foreach (var repoDir in repoDirs)
                    {
                        var repoJsonFile = Directory.GetFiles(repoDir, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                        if (string.IsNullOrEmpty(repoJsonFile)) continue;
                        try
                        {
                            var json = File.ReadAllText(repoJsonFile);
                            var jsonObj = JObject.Parse(json);
                            if (jsonObj["indexes"] is JArray indexes)
                            {
                                var pathSet = new HashSet<string>();
                                CollectAllPathsFromIndexes(indexes, "", pathSet);
                                repoPathSets[Path.GetFileName(repoDir)] = pathSet;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    if (repoPathSets.Count > 1)
                    {
                        // 按仓库聚合后批量写入
                        var repoSubscriptions = new Dictionary<string, List<string>>();
                        foreach (var path in oldPaths)
                        {
                            var targetRepo = repoFolderName; // 默认归入当前仓库
                            foreach (var (repoName, pathSet) in repoPathSets)
                            {
                                if (pathSet.Contains(path))
                                {
                                    targetRepo = repoName;
                                    break;
                                }
                            }

                            if (!repoSubscriptions.ContainsKey(targetRepo))
                                repoSubscriptions[targetRepo] = new List<string>();
                            repoSubscriptions[targetRepo].Add(path);
                        }

                        foreach (var (repoName, paths) in repoSubscriptions)
                        {
                            WriteSubscriptionFile(GetSubscriptionFilePath(repoName), paths);
                        }

                        // 清空配置属性，框架自动保存
                        scriptConfig.SubscribedScriptPaths = new List<string>();
                        _logger.LogInformation("已完成订阅路径迁移到独立文件（多仓库分配）");
                        return;
                    }
                }
            }

            // 单仓库：直接写入
            WriteSubscriptionFile(GetSubscriptionFilePath(repoFolderName), new List<string>(oldPaths));

            // 清空配置属性，框架自动保存
            scriptConfig.SubscribedScriptPaths = new List<string>();
            _logger.LogInformation("已完成订阅路径迁移到独立文件: {Count} 个路径", oldPaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 config.json 迁移订阅路径到独立文件失败");
        }
    }

    /// <summary>
    /// 递归收集 indexes 中所有路径（用于迁移时匹配）
    /// </summary>
    private static void CollectAllPathsFromIndexes(JArray nodes, string currentPath, HashSet<string> result)
    {
        foreach (var node in nodes)
        {
            if (node is JObject nodeObj)
            {
                var name = nodeObj["name"]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    var fullPath = string.IsNullOrEmpty(currentPath) ? name : $"{currentPath}/{name}";
                    result.Add(fullPath);

                    if (nodeObj["children"] is JArray children)
                    {
                        CollectAllPathsFromIndexes(children, fullPath, result);
                    }
                }
            }
        }
    }

    // 更新订阅脚本路径列表，移除无效路径（仅处理当前仓库的订阅）
    public void UpdateSubscribedScriptPaths()
    {
        var validRoots = PathMapper.Keys.ToHashSet();

        var allPaths = GetSubscribedPathsForCurrentRepo()
            .Distinct()
            .OrderBy(path => path)
            .ToList();

        var pathsToKeep = new HashSet<string>();

        foreach (var path in allPaths)
        {
            if (string.IsNullOrEmpty(path) || !path.Contains('/'))
                continue;

            var root = path.Split('/')[0];
            if (!validRoots.Contains(root))
                continue;

            var (_, remainingPath) = GetFirstFolderAndRemainingPath(path);
            var userPath = Path.Combine(PathMapper[root], remainingPath);
            if (!Directory.Exists(userPath) && !File.Exists(userPath))
                continue;

            // 检查是否已被父路径覆盖
            bool isCoveredByParent = pathsToKeep.Any(p =>
                path.StartsWith(p + "/") || path == p);

            if (!isCoveredByParent)
            {
                pathsToKeep.Add(path);
            }
        }

        // 添加父节点自动订阅逻辑
        try
        {
            // 获取所有可用路径
            var allAvailablePaths = new HashSet<string>();
            var repoJsonPath = Directory.GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(repoJsonPath))
            {
                var jsonContent = File.ReadAllText(repoJsonPath);
                var jsonObj = JObject.Parse(jsonContent);

                if (jsonObj["indexes"] is JArray indexes)
                {
                    // 递归收集所有路径
                    void CollectPaths(JArray nodes, string currentPath)
                    {
                        foreach (var node in nodes)
                        {
                            if (node is JObject nodeObj)
                            {
                                var name = nodeObj["name"]?.ToString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var fullPath = string.IsNullOrEmpty(currentPath) ? name : $"{currentPath}/{name}";
                                    allAvailablePaths.Add(fullPath);

                                    if (nodeObj["children"] is JArray children)
                                    {
                                        CollectPaths(children, fullPath);
                                    }
                                }
                            }
                        }
                    }

                    CollectPaths(indexes, "");
                }
            }

            // 如果父节点的所有直接子节点都已被订阅，则将父节点也加入订阅
            if (allAvailablePaths.Count > 0)
            {
                // 构建父子关系映射，只记录直接子节点
                var parentChildMap = new Dictionary<string, List<string>>();

                // 遍历所有路径，找到每个节点的父节点
                foreach (var path in allAvailablePaths)
                {
                    var pathParts = path.Split('/');
                    if (pathParts.Length > 1)
                    {
                        var parentPath = string.Join("/", pathParts.Take(pathParts.Length - 1));
                        if (!parentChildMap.ContainsKey(parentPath))
                        {
                            parentChildMap[parentPath] = new List<string>();
                        }

                        if (!parentChildMap[parentPath].Contains(path))
                        {
                            parentChildMap[parentPath].Add(path);
                        }
                    }
                }

                // 递归检查父节点，直到没有新的父节点需要添加
                bool hasNewPaths;
                do
                {
                    hasNewPaths = false;
                    var pathsToAdd = new HashSet<string>();

                    // 检查每个父节点
                    foreach (var kvp in parentChildMap)
                    {
                        var parentPath = kvp.Key;
                        var directChildren = kvp.Value;

                        // 检查所有直接子节点是否都已被订阅
                        bool allDirectChildrenSubscribed = directChildren.All(child =>
                            pathsToKeep.Contains(child));

                        // 如果所有直接子节点都已被订阅，且父节点本身未被订阅，则添加父节点
                        if (allDirectChildrenSubscribed && !pathsToKeep.Contains(parentPath))
                        {
                            pathsToAdd.Add(parentPath);
                            hasNewPaths = true;
                        }
                    }

                    // 将需要添加的父节点加入订阅列表
                    foreach (var pathToAdd in pathsToAdd)
                    {
                        pathsToKeep.Add(pathToAdd);
                    }
                } while (hasNewPaths);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加父节点时发生错误");
        }

        SetSubscribedPathsForCurrentRepo(pathsToKeep
            .OrderBy(path => path)
            .ToList());
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // 创建目标目录
        Directory.CreateDirectory(destDir);

        // 拷贝文件
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        // 拷贝子目录
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
            // 图标处理
            DealWithIconFolder(destSubDir);
        }
    }

    /// <summary>
    /// 解析并下载脚本依赖 (packages)
    /// </summary>
    /// <param name="repoPath">仓库路径</param>
    /// <param name="localScriptPath">本地脚本路径</param>
    private void ResolveScriptDependencies(string repoPath, string localScriptPath)
    {
        try
        {
            var processedFiles = new HashSet<string>();
            var processingQueue = new Queue<string>();
            
            // 确定根目录
            string baseDestDir;
            if (File.Exists(localScriptPath))
            {
                processingQueue.Enqueue(localScriptPath);
                baseDestDir = Path.GetDirectoryName(localScriptPath) ?? localScriptPath;
            }
            else if (Directory.Exists(localScriptPath))
            {
                baseDestDir = localScriptPath;
                // 初始加入目录下的所有 JS 文件
                foreach (var f in Directory.GetFiles(localScriptPath, "*.js", SearchOption.AllDirectories))
                {
                    processingQueue.Enqueue(f);
                }
            }
            else
            {
                return;
            }

            // 清理 packages
            var targetPackagesDir = Path.Combine(baseDestDir, "packages");
            if (Directory.Exists(targetPackagesDir))
            {
                try
                {
                    Directory.Delete(targetPackagesDir, true);
                    // _logger.LogInformation($"已清理旧依赖目录: {targetPackagesDir}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"清理依赖目录失败: {ex.Message}");
                }
            }

            // 捕获导入的变量名
            var regex = new System.Text.RegularExpressions.Regex(
                @"(import\s+([\w\d_$]+)\s+from\s+['""]|import\s+(?:[\w\s{},*]*?from\s+)?['""]|export\s+(?:[\w\s{},*]*?from\s+)?['""]|import\s+['""]|require\s*\(\s*['""])([^'""\n]+)(['""])");

            while (processingQueue.Count > 0)
            {
                var currentFile = processingQueue.Dequeue();
                
                // 避免重复处理
                if (processedFiles.Contains(currentFile)) continue;
                processedFiles.Add(currentFile);

                try
                {
                    if (!File.Exists(currentFile)) continue;

                    var content = File.ReadAllText(currentFile);
                    var matches = regex.Matches(content);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var originalPath = match.Groups[3].Value;
                        string? packagePath = null;

                        // 识别是否为 packages 引用
                        int packageIndex = originalPath.IndexOf("packages/", StringComparison.OrdinalIgnoreCase);
                        if (packageIndex >= 0)
                        {
                            packagePath = originalPath.Substring(packageIndex).Replace('\\', '/');
                        }
                        // 识别是否为 packages 内部的相对引用
                        else if (originalPath.StartsWith("."))
                        {
                            // 检查当前文件是否在 packages 目录下
                            var localPackagesDir = Path.Combine(baseDestDir, "packages");
                            if (currentFile.StartsWith(localPackagesDir, StringComparison.OrdinalIgnoreCase))
                            {
                                // 计算当前文件对应的 repo 路径
                                var relToScript = Path.GetRelativePath(baseDestDir, currentFile);
                                var relDir = Path.GetDirectoryName(relToScript); // e.g. packages/utils
                                
                                if (relDir != null)
                                {
                                    var depPackagePath = Path.Combine(relDir, originalPath).Replace('\\', '/');
                                    // 规范化路径
                                    depPackagePath = Path.GetFullPath(Path.Combine(baseDestDir, depPackagePath));
                                    depPackagePath = Path.GetRelativePath(baseDestDir, depPackagePath).Replace('\\', '/');

                                    if (depPackagePath.StartsWith("packages/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        packagePath = depPackagePath;
                                    }
                                }
                            }
                        }

                        if (packagePath != null)
                        {
                            var destPath = Path.Combine(baseDestDir, packagePath);
                            bool isCode = packagePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

                            // 如果文件不存在，下载
                            if (!File.Exists(destPath))
                            {
                                bool downloaded = false;
                                
                                // 尝试精确下载
                                if (DownloadAndQueue(repoPath, packagePath, destPath, processingQueue)) 
                                {
                                    downloaded = true;
                                }
                                // 尝试 .js
                                else if (isCode || packagePath.IndexOf('.') == -1) 
                                {
                                    // 尝试补充 .js
                                    if (DownloadAndQueue(repoPath, packagePath + ".js", destPath + ".js", processingQueue)) downloaded = true;
                                }

                                if (!downloaded)
                                {
                                     _logger.LogWarning($"依赖未找到: {packagePath} (in {Path.GetFileName(currentFile)})");
                                }
                            }
                            else
                            {
                                // 文件已存在
                                if (isCode)
                                {
                                    if (!processedFiles.Contains(destPath) && !processingQueue.Contains(destPath)) processingQueue.Enqueue(destPath);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"分析文件依赖出错: {currentFile}, {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析脚本依赖主流程失败");
        }
    }

    private bool DownloadAndQueue(string repoPath, string sourcePath, string destPath, Queue<string> queue)
    {
        if (CheckoutRepoRootPath(repoPath, sourcePath, destPath))
        {
            queue.Enqueue(destPath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 从仓库根目录检出指定路径
    /// </summary>
    /// <returns>是否成功检出</returns>
    private bool CheckoutRepoRootPath(string repoPath, string sourcePath, string destPath)
    {
         bool isGitRepo = IsGitRepository(repoPath);

        if (isGitRepo)
        {
            using var repo = new Repository(repoPath);
            var commit = repo.Head.Tip;

            if (commit == null) throw new Exception("仓库HEAD未指向任何提交");

            TreeEntry? entry = null;
            var pathParts = sourcePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            Tree currentTree = commit.Tree; // Start from ROOT tree

            for (int i = 0; i < pathParts.Length; i++)
            {
                entry = currentTree[pathParts[i]];
                if (entry == null) return false; // Path not found

                if (i < pathParts.Length - 1)
                {
                    if (entry.TargetType != TreeEntryTargetType.Tree)
                         // 路径中间部分不是目录，说明路径错误
                         return false;
                    currentTree = (Tree)entry.Target;
                }
            }

            if (entry == null) return false;

            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)entry.Target;
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var contentStream = blob.GetContentStream();
                using var fileStream = File.Create(destPath);
                contentStream.CopyTo(fileStream);
                return true;
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                var tree = (Tree)entry.Target;
                CheckoutTree(tree, destPath, sourcePath);
                return true;
            }
            return false;
        }
        else
        {
            var potentialRoot = Directory.GetParent(repoPath)?.FullName;
            if (potentialRoot != null)
            {
                var scriptPath = Path.Combine(potentialRoot, sourcePath);
                 if (Directory.Exists(scriptPath))
                {
                    CopyDirectory(scriptPath, destPath);
                    return true;
                }
                else if (File.Exists(scriptPath))
                {
                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(scriptPath, destPath, true);
                    return true;
                }
            }
            return false;
        }
    }

    private static (string firstFolder, string remainingPath) GetFirstFolderAndRemainingPath(string path)
    {
        // 检查路径是否为空或仅包含部分字符
        if (string.IsNullOrEmpty(path))
        {
            return (string.Empty, string.Empty);
        }

        // 使用路径分隔符分割路径
        string[] parts = path.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // 返回第一个文件夹和剩余路径
        return parts.Length > 0
            ? (parts[0], string.Join(Path.DirectorySeparatorChar, parts.Skip(1)))
            : (string.Empty, string.Empty);
    }

    public void OpenLocalRepoInWebView()
    {
        UpdateSubscribedScriptPaths();
        if (_webWindow is not { IsVisible: true })
        {
            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;

            // 计算宽高（默认0.7屏幕宽高）
            double width = scriptConfig.WebviewWidth == 0
                ? SystemParameters.WorkArea.Width * 0.7
                : scriptConfig.WebviewWidth;

            double height = scriptConfig.WebviewHeight == 0
                ? SystemParameters.WorkArea.Height * 0.7
                : scriptConfig.WebviewHeight;

            // 计算位置（默认居中）
            double left = scriptConfig.WebviewLeft == 0
                ? (SystemParameters.WorkArea.Width - width) / 2
                : scriptConfig.WebviewLeft;

            double top = scriptConfig.WebviewTop == 0
                ? (SystemParameters.WorkArea.Height - height) / 2
                : scriptConfig.WebviewTop;
            
            WindowState state = scriptConfig.WebviewState;
            var screen = SystemParameters.WorkArea;
            bool isSmallScreen = screen.Width <= 1600 || screen.Height <= 900;
            // 如果未设置或非法值，则默认 Normal，小屏则直接最大化
            if (isSmallScreen)
            {
                state = WindowState.Maximized;
            }
            else if (!Enum.IsDefined(typeof(WindowState), scriptConfig.WebviewState))
            {
                state = WindowState.Normal;
            }
            else
            {
                state = scriptConfig.WebviewState;
            }
            
            _webWindow = new WebpageWindow
            {
                Title = "Genshin Copilot Scripts | BetterGI 脚本本地中央仓库",
                Width = width,
                Height = height,
                Left = left,
                Top = top,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowState = state
            };
            // 关闭时保存窗口位置与大小
            _webWindow.Closed += (s, e) =>
            {
                if (_webWindow != null)
                {
                    scriptConfig.WebviewLeft = _webWindow.Left;
                    scriptConfig.WebviewTop = _webWindow.Top;
                    scriptConfig.WebviewWidth = _webWindow.Width;
                    scriptConfig.WebviewHeight = _webWindow.Height;
                    scriptConfig.WebviewState = _webWindow.WindowState;
                }

                _webWindow = null;
            };

            _webWindow.Panel!.DownloadFolderPath = MapPathingViewModel.PathJsonPath;
            // _webWindow.NavigateToFile(Global.Absolute(@"Assets\Web\ScriptRepo\index.html"));
            _webWindow.Panel!.OnWebViewInitializedAction = () =>
            {
                var assetsPath = Global.Absolute(@"Assets\Web\ScriptRepo");
                _webWindow.Panel!.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "bettergi.local",
                    assetsPath,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // _webWindow.Panel!.WebView.CoreWebView2.Navigate("https://bettergi.local/index.html");

                _webWindow.Panel!.WebView.CoreWebView2.AddHostObjectToScript("repoWebBridge", new RepoWebBridge());

                // 允许内部外链使用默认浏览器打开
                _webWindow.Panel!.WebView.CoreWebView2.NewWindowRequested += (sender, e) =>
                {
                    var psi = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = e.Uri
                    };
                    Process.Start(psi);

                    e.Handled = true;
                };
            };

            _webWindow.NavigateToUri(new Uri("https://bettergi.local/index.html"));
            _webWindow.Show();
        }
        else
        {
            _webWindow.Activate();
        }
    }

    public void OpenScriptRepoWindow()
    {
        var scriptRepoWindow = new ScriptRepoWindow { Owner = Application.Current.MainWindow };
        scriptRepoWindow.ShowDialog();
    }

    /// <summary>
    /// 处理带有 icon.ico 和 desktop.ini 的文件夹
    /// </summary>
    /// <param name="folderPath"></param>
    private void DealWithIconFolder(string folderPath)
    {
        if (Directory.Exists(folderPath)
            && File.Exists(Path.Combine(folderPath, "desktop.ini")))
        {
            // 使用 Vanara 库中的 SetFileAttributes 函数设置文件夹属性
            if (Kernel32.SetFileAttributes(folderPath, FileFlagsAndAttributes.FILE_ATTRIBUTE_READONLY))
            {
                Debug.WriteLine($"成功将文件夹设置为只读: {folderPath}");
            }
            else
            {
                Debug.WriteLine($"无法设置文件夹为只读: {folderPath}");
            }
        }
    }

    /// <summary>
    /// 根据通配符或正则表达式获取匹配的文件列表
    /// </summary>
    /// <param name="basePath">基础路径</param>
    /// <param name="pattern">通配符模式或正则表达式</param>
    /// <returns>匹配的文件路径列表</returns>
    private List<string> GetMatchedFiles(string basePath, string pattern)
    {
        var matchedFiles = new List<string>();

        try
        {
            // 检查是否是正则表达式（以^开头或包含特殊字符）
            bool isRegex = pattern.StartsWith("^") || pattern.Contains(".*") || pattern.Contains("\\d") || pattern.Contains("\\w");

            if (isRegex)
            {
                // 使用正则表达式匹配
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var allFiles = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    var relativePath = Path.GetRelativePath(basePath, file);
                    if (regex.IsMatch(relativePath))
                    {
                        matchedFiles.Add(file);
                    }
                }
            }
            else
            {
                // 使用通配符匹配
                var searchPattern = Path.GetFileName(pattern);
                var searchDir = Path.GetDirectoryName(pattern);

                if (string.IsNullOrEmpty(searchDir))
                {
                    // 只在当前目录搜索
                    var files = Directory.GetFiles(basePath, searchPattern);
                    matchedFiles.AddRange(files);
                }
                else
                {
                    // 在指定子目录搜索
                    var searchPath = Path.Combine(basePath, searchDir);
                    if (Directory.Exists(searchPath))
                    {
                        var files = Directory.GetFiles(searchPath, searchPattern);
                        matchedFiles.AddRange(files);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取匹配文件时发生错误: {pattern}");
        }

        return matchedFiles;
    }

    /// <summary>
    /// 备份脚本中需要保存的文件到Temp目录
    /// </summary>
    /// <param name="scriptPath">脚本在中央仓库中的路径</param>
    /// <param name="repoPath">中央仓库路径</param>
    /// <returns>备份的文件路径列表</returns>
    private List<string> BackupScriptFiles(string scriptPath, string repoPath)
    {
        var backupFiles = new List<string>();
        var tempBackupPath = Global.Absolute("User\\Temp");
        var scriptPathSafe = scriptPath;
        var backupScriptDir = Path.Combine(tempBackupPath, scriptPathSafe);
        try
        {
            if (!Directory.Exists(backupScriptDir))
            {
                Directory.CreateDirectory(backupScriptDir);
            }

            // 获取脚本的manifest文件内容
            string? manifestContent = null;

            // 判断仓库类型
            bool isGitRepo = IsGitRepository(repoPath);

            if (isGitRepo)
            {
                // 从Git仓库读取
                manifestContent = ReadFileFromGitRepository(repoPath, $"{scriptPath}/manifest.json");
                if (manifestContent == null)
                {
                    _logger.LogWarning($"脚本manifest文件不存在: {scriptPath}/manifest.json");
                    return backupFiles;
                }
            }
            else
            {
                // 文件式仓库：从文件系统读取
                var scriptManifestPath = Path.Combine(repoPath, scriptPath, "manifest.json");
                if (!File.Exists(scriptManifestPath))
                {
                    _logger.LogWarning($"脚本manifest文件不存在: {scriptManifestPath}");
                    return backupFiles;
                }
                manifestContent = File.ReadAllText(scriptManifestPath);
            }

            // 解析manifest文件获取savedFiles
            var manifest = Manifest.FromJson(manifestContent);

            if (manifest.SavedFiles == null || manifest.SavedFiles.Length == 0)
            {
                _logger.LogInformation($"脚本 {scriptPath} 没有需要保存的文件");
                return backupFiles;
            }

            // 获取脚本在用户目录中的路径
            var (first, remainingPath) = GetFirstFolderAndRemainingPath(scriptPath);
            if (!PathMapper.TryGetValue(first, out var userPath))
            {
                _logger.LogWarning($"未知的脚本路径映射: {scriptPath}");
                return backupFiles;
            }

            var scriptUserPath = Path.Combine(userPath, remainingPath);

            // 备份每个需要保存的文件
            foreach (var savedFileRaw in manifest.SavedFiles)
            {
                // 自动补全所有目录路径为以/结尾
                var savedFile = savedFileRaw;
                var fullPath = Path.Combine(scriptUserPath, savedFile.TrimEnd('/', '\\'));
                bool isDir = savedFile.EndsWith("/") || Directory.Exists(fullPath);
                if (!savedFile.EndsWith("/") && Directory.Exists(fullPath))
                {
                    savedFile += "/";
                    isDir = true;
                }

                if (isDir)
                {
                    var dirPath = Path.Combine(scriptUserPath, savedFile.TrimEnd('/', '\\'));
                    if (Directory.Exists(dirPath))
                    {
                        var destDir = Path.Combine(backupScriptDir, savedFile.TrimEnd('/', '\\'));
                        CopyDirectory(dirPath, destDir);
                        backupFiles.Add(destDir);
                    }
                    else
                    {
                        _logger.LogWarning($"需要备份的文件夹不存在: {dirPath}");
                    }
                }
                else
                {
                    var matchedFiles = GetMatchedFiles(scriptUserPath, savedFile);
                    foreach (var matchedFile in matchedFiles)
                    {
                        var relativePath = Path.GetRelativePath(scriptUserPath, matchedFile);
                        var backupFilePath = Path.Combine(backupScriptDir, relativePath);
                        var backupFileDir = Path.GetDirectoryName(backupFilePath);
                        if (!string.IsNullOrEmpty(backupFileDir) && !Directory.Exists(backupFileDir))
                        {
                            Directory.CreateDirectory(backupFileDir);
                        }

                        try
                        {
                            File.Copy(matchedFile, backupFilePath, true);
                            backupFiles.Add(backupFilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"备份文件失败: {matchedFile}");
                        }
                    }

                    if (matchedFiles.Count == 0)
                    {
                        _logger.LogWarning($"没有找到匹配的文件: {savedFile}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"备份脚本文件时发生错误: {scriptPath}");
        }

        return backupFiles;
    }

    /// <summary>
    /// 恢复备份的文件到指定位置并清理Temp目录
    /// </summary>
    /// <param name="scriptPath">脚本在中央仓库中的路径</param>
    /// <param name="repoPath">中央仓库路径</param>
    private void RestoreScriptFiles(string scriptPath, string repoPath)
    {
        var tempBackupPath = Global.Absolute("User\\Temp");
        var scriptPathSafe = scriptPath;
        var backupScriptDir = Path.Combine(tempBackupPath, scriptPathSafe);
        try
        {
            // 获取脚本的manifest文件内容
            string? manifestContent = null;

            // 判断仓库类型
            bool isGitRepo = IsGitRepository(repoPath);

            if (isGitRepo)
            {
                // 从Git仓库读取
                manifestContent = ReadFileFromGitRepository(repoPath, $"{scriptPath}/manifest.json");
                if (manifestContent == null)
                {
                    _logger.LogWarning($"脚本manifest文件不存在: {scriptPath}/manifest.json");
                    return;
                }
            }
            else
            {
                // 文件式仓库：从文件系统读取
                var scriptManifestPath = Path.Combine(repoPath, scriptPath, "manifest.json");
                if (!File.Exists(scriptManifestPath))
                {
                    _logger.LogWarning($"脚本manifest文件不存在: {scriptManifestPath}");
                    return;
                }
                manifestContent = File.ReadAllText(scriptManifestPath);
            }

            // 解析manifest文件获取savedFiles
            var manifest = Manifest.FromJson(manifestContent);

            if (manifest.SavedFiles == null || manifest.SavedFiles.Length == 0)
            {
                _logger.LogInformation($"脚本 {scriptPath} 没有需要恢复的文件");
                return;
            }

            // 获取脚本在用户目录中的路径
            var (first, remainingPath) = GetFirstFolderAndRemainingPath(scriptPath);
            if (!PathMapper.TryGetValue(first, out var userPath))
            {
                _logger.LogWarning($"未知的脚本路径映射: {scriptPath}");
                return;
            }

            var scriptUserPath = Path.Combine(userPath, remainingPath);

            // 还原所有备份文件
            if (Directory.Exists(backupScriptDir))
            {
                foreach (var file in Directory.GetFiles(backupScriptDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(backupScriptDir, file);
                    var restorePath = Path.Combine(scriptUserPath, relativePath);
                    var restoreDir = Path.GetDirectoryName(restorePath);
                    if (!string.IsNullOrEmpty(restoreDir) && !Directory.Exists(restoreDir))
                    {
                        Directory.CreateDirectory(restoreDir);
                    }

                    try
                    {
                        File.Copy(file, restorePath, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"恢复文件失败: {file} -> {restorePath}");
                    }
                }
            }
            else
            {
                _logger.LogWarning($"备份目录不存在: {backupScriptDir}");
            }

            // 清理Temp目录下该脚本的备份
            try
            {
                if (Directory.Exists(backupScriptDir))
                {
                    Directory.Delete(backupScriptDir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理Temp脚本备份目录失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"恢复脚本文件时发生错误: {scriptPath}");
        }
    }
}
