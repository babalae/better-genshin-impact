using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Helpers.Win32;
using BetterGenshinImpact.Model;
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
using System.Threading.Tasks;
using System.Windows;
using Windows.UI.Xaml.Automation;
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

    // 中央仓库解压后文件夹名
    public static readonly string CenterRepoUnzipName = "bettergi-scripts-list-git";

    public static readonly string CenterRepoPath = Path.Combine(ReposPath, CenterRepoUnzipName);

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

    public async Task<(string, bool)> UpdateCenterRepoByGit(string repoUrl, CheckoutProgressHandler? onCheckoutProgress)
    {
        if (string.IsNullOrEmpty(repoUrl))
        {
            throw new ArgumentException("仓库URL不能为空", nameof(repoUrl));
        }

        var repoPath = Path.Combine(ReposPath, "bettergi-scripts-list-git");
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
                        // 远程URL已更改，需要删除重新克隆
                        _logger.LogInformation($"远程URL已更改: 从 {origin.Url} 到 {repoUrl}，将重新克隆");
                        repo?.Dispose();
                        CloneRepository(repoUrl, repoPath, "release", onCheckoutProgress);
                        updated = true;
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

                // 检查是否存在repo_update.json，如果存在则直接与它比对
                var repoUpdateJsonPath = Path.Combine(ReposPath, "repo_updated.json");
                string updatedContent;

                if (File.Exists(repoUpdateJsonPath))
                {
                    var repoUpdateContent = await File.ReadAllTextAsync(repoUpdateJsonPath);
                    updatedContent = AddUpdateMarkersToNewRepo(repoUpdateContent, newRepoJsonContent);
                }
                else
                {
                    // 如果没有repo_update.json，则使用备份的旧内容进行比对
                    updatedContent = AddUpdateMarkersToNewRepo(oldRepoJsonContent ?? "", newRepoJsonContent);
                }

                // 保存到同级目录
                var updatedRepoJsonPath = Path.Combine(ReposPath, "repo_updated.json");
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
            if (IsTruthy(oldNode["hasUpdate"]) || IsTruthy(oldNode["hasUpdated"]))
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
                        if (isLeafChild && (IsTruthy(newChildObj["hasUpdate"]) || IsTruthy(newChildObj["hasUpdated"])))
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

    public async Task DownloadRepoAndUnzip(string url)
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
        var paths = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(pathJson);
        if (paths is null || paths.Count == 0)
        {
            Toast.Warning("订阅脚本路径为空");
            return;
        }

        // 保存订阅信息
        var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
        scriptConfig.SubscribedScriptPaths.AddRange(paths);

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

        //顶层目录订阅时，不会删除其下，不在订阅中的文件夹
        List<string> newPaths = new List<string>();
        foreach (var path in paths)
        {
            //顶层节点，按库中的文件夹来
            if (path == "pathing")
            {
                // 判断仓库类型：Git 仓库或文件式仓库
                bool isGitRepo = IsGitRepository(repoPath);

                if (isGitRepo)
                {
                    // 从Git仓库读取
                    using var repo = new Repository(repoPath);
                    var commit = repo.Head.Tip;
                    if (commit == null)
                    {
                        throw new Exception("仓库HEAD未指向任何提交");
                    }

                    Tree repoTree = GetRepoSubdirectoryTree(repo);

                    var pathingEntry = repoTree["pathing"];
                    if (pathingEntry != null && pathingEntry.TargetType == TreeEntryTargetType.Tree)
                    {
                        var pathingTree = (Tree)pathingEntry.Target;
                        foreach (var entry in pathingTree)
                        {
                            if (entry.TargetType == TreeEntryTargetType.Tree)
                            {
                                newPaths.Add("pathing/" + entry.Name);
                            }
                        }
                    }
                    else
                    {
                        Toast.Warning($"未知的脚本路径：{path}");
                    }
                }
                else
                {
                    // 文件式仓库：从文件系统读取
                    var pathingDir = Path.Combine(repoPath, "pathing");
                    if (Directory.Exists(pathingDir))
                    {
                        // 获取该路径下的所有“仅第一层文件夹”
                        string[] directories = Directory.GetDirectories(pathingDir, "*", SearchOption.TopDirectoryOnly);
                        foreach (var dir in directories)
                        {
                            newPaths.Add("pathing/" + Path.GetFileName(dir));
                        }
                    }
                    else
                    {
                        Toast.Warning($"未知的脚本路径：{path}");
                    }
                }
            }
            else
            {
                newPaths.Add(path);
            }
        }

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

                UpdateSubscribedScriptPaths();
                Toast.Success("脚本订阅链接导入完成");
            }
            else
            {
                Toast.Warning($"未知的脚本路径：{path}");
            }
        }
    }

    // 更新订阅脚本路径列表，移除无效路径
    public void UpdateSubscribedScriptPaths()
    {
        var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
        var validRoots = PathMapper.Keys.ToHashSet();

        var allPaths = scriptConfig.SubscribedScriptPaths
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

        scriptConfig.SubscribedScriptPaths = pathsToKeep
            .OrderBy(path => path)
            .ToList();
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
