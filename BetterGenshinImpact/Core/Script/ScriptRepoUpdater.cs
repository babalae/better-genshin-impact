using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.WebView;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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
            try
            {
                GlobalSettings.SetOwnerValidation(false);
                if (!Directory.Exists(repoPath))
                {
                    // 如果仓库不存在，执行浅克隆操作
                    _logger.LogInformation($"浅克隆仓库: {repoUrl} 到 {repoPath}");

                    CloneRepository(repoUrl, repoPath, onCheckoutProgress);

                    // CloneRepository(repoUrl, repoPath);
                    updated = true;
                }
                else
                {
                    try
                    {
                        // 检测repo.json是否存在，存在则备份
                        var oldRepoJsonPath = Directory
                            .GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories).FirstOrDefault();
                        if (oldRepoJsonPath != null)
                        {
                            oldRepoJsonContent = File.ReadAllText(oldRepoJsonPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "备份repo.json失败，继续更新仓库");
                    }

                    using var repo = new Repository(repoPath);

                    // 检查远程URL是否需要更新
                    var origin = repo.Network.Remotes["origin"];
                    if (origin.Url != repoUrl)
                    {
                        // 远程URL已更改，需要更新
                        _logger.LogInformation($"更新远程URL: 从 {origin.Url} 到 {repoUrl}");
                        repo.Network.Remotes.Update("origin", r => r.Url = repoUrl);
                    }

                    // 获取远程分支信息
                    var remote = repo.Network.Remotes["origin"];
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);

                    // 使用浅拉取选项
                    var fetchOptions = new FetchOptions();
                    fetchOptions.ProxyOptions.ProxyType = ProxyType.None;

                    Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, "拉取最新更新");

                    // 获取当前分支
                    var branch = repo.Branches["refs/heads/origin/main"] ?? repo.Branches["main"];
                    if (branch == null)
                    {
                        throw new Exception("未找到main或master分支");
                    }

                    // 如果是本地分支，需要设置上游分支
                    if (!branch.IsRemote)
                    {
                        var trackingBranch = repo.Branches[$"origin/{branch.FriendlyName}"];
                        if (trackingBranch != null && branch.TrackedBranch == null)
                        {
                            branch = repo.Branches.Update(branch,
                                b => b.TrackedBranch = trackingBranch.CanonicalName);
                        }
                    }

                    // 检查是否有更新
                    var currentCommitSha = repo.Head.Tip.Sha;

                    // 合并或重置到最新
                    if (branch.TrackedBranch != null)
                    {
                        var trackingBranch = branch.TrackedBranch;
                        var mergeResult = Commands.Pull(
                            repo,
                            new Signature("BetterGI", "auto@bettergi.com", DateTimeOffset.Now),
                            new PullOptions());

                        // 检查是否有更新
                        updated = currentCommitSha != repo.Head.Tip.Sha;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git仓库更新失败");
                throw;
            }
        });
        // 如果仓库有更新且有备份内容，则标记新repo.json中的更新节点
        if (updated && !string.IsNullOrEmpty(oldRepoJsonContent))
        {
            try
            {
                var newRepoJsonPath = Directory.GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (newRepoJsonPath != null)
                {
                    var newRepoJsonContent = await File.ReadAllTextAsync(newRepoJsonPath);
                    var updatedContent = AddUpdateMarkersToNewRepo(oldRepoJsonContent, newRepoJsonContent);

                    // 保存到同级目录
                    var parentDir = Path.GetDirectoryName(repoPath);
                    var updatedRepoJsonPath = Path.Combine(parentDir!, "repo_updated.json");
                    
                    await File.WriteAllTextAsync(updatedRepoJsonPath, updatedContent);
                    _logger.LogInformation($"已标记repo.json中的更新节点并保存到: {updatedRepoJsonPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "标记repo.json更新节点失败");
            }
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
                        if (isLeafChild && !hasDirectUpdate && newChildObj["hasUpdate"] != null)
                        {
                            newNode["hasUpdate"] = true;
                            hasDirectUpdate = true;
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

    private static void SimpleCloneRepository(string repoUrl, string repoPath,
        CheckoutProgressHandler? onCheckoutProgress)
    {
        var options = new CloneOptions
        {
            Checkout = true,
            IsBare = false,
            RecurseSubmodules = false, // 不递归克隆子模块
            OnCheckoutProgress = onCheckoutProgress
        };
        // options.FetchOptions.Depth = 1; // 浅克隆，只获取最新的提交
        // 克隆仓库
        Repository.Clone(repoUrl, repoPath, options);
    }

    /// <summary>
    ///  
    /// 相当于 Repository.Clone(repoUrl, repoPath, options); 
    /// </summary>
    /// <param name="repoUrl"></param>
    /// <param name="repoPath"></param>
    /// <param name="onCheckoutProgress"></param>
    /// <exception cref="Exception"></exception>
    private void CloneRepository(string repoUrl, string repoPath, CheckoutProgressHandler? onCheckoutProgress)
    {
        // 1. 创建目录
        Directory.CreateDirectory(repoPath);

        // 2. 初始化 Git 仓库
        Repository.Init(repoPath);

        using var repo = new Repository(repoPath);
        GitConfig(repo);

        // 3. 添加远程源
        Remote remote = repo.Network.Remotes.Add("origin", repoUrl);

        // 4. 获取数据（使用浅克隆选项）
        var fetchOptions = new FetchOptions
        {
            TagFetchMode = TagFetchMode.None // 不获取标签
        };
        fetchOptions.ProxyOptions.ProxyType = ProxyType.None;

        // 5. 执行获取操作
        Commands.Fetch(repo, remote.Name, ["+refs/heads/*:refs/remotes/origin/*"], fetchOptions, "初始化拉取");

        // 6. 创建本地分支并跟踪远程分支
        var remoteBranch = repo.Branches["refs/remotes/origin/main"] ?? repo.Branches["refs/remotes/origin/master"];
        if (remoteBranch == null)
        {
            throw new Exception("远程仓库中未找到 main 或 master 分支");
        }

        // 7. 创建并检出本地分支
        var localBranch = repo.CreateBranch(remoteBranch.FriendlyName, remoteBranch.Tip);

        // 8. 设置本地分支跟踪远程分支
        repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);

        // 9. 检出分支
        CheckoutOptions checkoutOptions = new CheckoutOptions
        {
            OnCheckoutProgress = onCheckoutProgress
        };
        Commands.Checkout(repo, localBranch, checkoutOptions);
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
        var localRepoJsonPath = Directory.GetFiles(CenterRepoPath, "repo.json", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (localRepoJsonPath is null)
        {
            throw new Exception("本地仓库缺少 repo.json");
        }

        // 获取与 localRepoJsonPath 同名（无扩展名）的文件夹路径
        var folderName = Path.GetFileNameWithoutExtension(localRepoJsonPath);
        var folderPath = Path.Combine(Path.GetDirectoryName(localRepoJsonPath)!, folderName);
        if (!Directory.Exists(folderPath))
        {
            throw new Exception("本地仓库文件夹不存在");
        }

        return folderPath;
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
            await MessageBox.ErrorAsync("本地无仓库信息，请至少成功更新一次脚本仓库信息！");
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
                var scriptPath = Path.Combine(repoPath, path);
                if (Directory.Exists(scriptPath))
                {
                    // 获取该路径下的所有“仅第一层文件夹”
                    string[] directories = Directory.GetDirectories(scriptPath, "*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in directories)
                    {
                        newPaths.Add("pathing" + "/" + Path.GetFileName(dir));
                    }
                }
                else
                {
                    Toast.Warning($"未知的脚本路径：{path}");
                }
            }
            else
            {
                newPaths.Add(path);
            }
        }

        // 拷贝文件
        foreach (var path in newPaths)
        {
            var (first, remainingPath) = GetFirstFolderAndRemainingPath(path);
            if (PathMapper.TryGetValue(first, out var userPath))
            {
                var scriptPath = Path.Combine(repoPath, path);
                var destPath = Path.Combine(userPath, remainingPath);
                
                // 备份需要保存的文件
                List<string> backupFiles = new List<string>();
                if (first == "js") // 只对JS脚本进行备份
                {
                    backupFiles = BackupScriptFiles(path, repoPath);
                }
                
                if (Directory.Exists(scriptPath))
                {
                    if (Directory.Exists(destPath))
                    {
                        DirectoryHelper.DeleteDirectoryWithReadOnlyCheck(destPath);
                    }

                    CopyDirectory(scriptPath, destPath);

                    // 图标处理
                    DealWithIconFolder(destPath);
                }
                else if (File.Exists(scriptPath))
                {
                    // 目标文件所在文件夹不存在时创建它
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    if (File.Exists(destPath))
                    {
                        File.Delete(destPath);
                    }

                    File.Copy(scriptPath, destPath, true);
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
            _webWindow = new WebpageWindow
            {
                Title = "Genshin Copilot Scripts | BetterGI 脚本本地中央仓库",
                Width = 1366,
                Height = 768,
            };
            _webWindow.Closed += (s, e) => _webWindow = null;
            _webWindow.Panel!.DownloadFolderPath = MapPathingViewModel.PathJsonPath;
            _webWindow.NavigateToFile(Global.Absolute(@"Assets\Web\ScriptRepo\index.html"));
            _webWindow.Panel!.OnWebViewInitializedAction = () =>
            {
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
        
        try
        {
            // 确保Temp目录存在
            if (!Directory.Exists(tempBackupPath))
            {
                Directory.CreateDirectory(tempBackupPath);
            }

            // 获取脚本的manifest文件路径
            var scriptManifestPath = Path.Combine(repoPath, scriptPath, "manifest.json");
            if (!File.Exists(scriptManifestPath))
            {
                _logger.LogWarning($"脚本manifest文件不存在: {scriptManifestPath}");
                return backupFiles;
            }

            // 解析manifest文件获取savedFiles
            var manifestContent = File.ReadAllText(scriptManifestPath);
            
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
            foreach (var savedFile in manifest.SavedFiles)
            {
                var matchedFiles = GetMatchedFiles(scriptUserPath, savedFile);
                foreach (var matchedFile in matchedFiles)
                {
                    // 创建备份文件路径，使用相对路径作为文件名以避免路径冲突
                    var relativePath = Path.GetRelativePath(scriptUserPath, matchedFile);
                    var backupFileName = relativePath.Replace('/', '_').Replace('\\', '_');
                    var scriptPathSafe = scriptPath.Replace('/', '_').Replace('\\', '_');
                    var backupFileNameFull = $"{scriptPathSafe}_{backupFileName}";
                    var backupFilePath = Path.Combine(tempBackupPath, backupFileNameFull);
                    
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
        
        try
        {
            // 获取脚本的manifest文件路径
            var scriptManifestPath = Path.Combine(repoPath, scriptPath, "manifest.json");
            if (!File.Exists(scriptManifestPath))
            {
                _logger.LogWarning($"脚本manifest文件不存在: {scriptManifestPath}");
                return;
            }

            // 解析manifest文件获取savedFiles
            var manifestContent = File.ReadAllText(scriptManifestPath);
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
            
            // 恢复每个备份的文件
            foreach (var savedFile in manifest.SavedFiles)
            {
                var matchedFiles = GetMatchedFiles(scriptUserPath, savedFile);
                foreach (var matchedFile in matchedFiles)
                {
                    var relativePath = Path.GetRelativePath(scriptUserPath, matchedFile);
                    var backupFileName = relativePath.Replace('/', '_').Replace('\\', '_');
                    var scriptPathSafe = scriptPath.Replace('/', '_').Replace('\\', '_');
                    var backupFileNameFull = $"{scriptPathSafe}_{backupFileName}";
                    var backupFilePath = Path.Combine(tempBackupPath, backupFileNameFull);
                    
                    if (File.Exists(backupFilePath))
                    {
                        try
                        {
                            // 确保目标目录存在
                            var targetDir = Path.GetDirectoryName(matchedFile);
                            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            
                            File.Copy(backupFilePath, matchedFile, true);
                            
                            // 删除备份文件
                            File.Delete(backupFilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"恢复文件失败: {backupFilePath} -> {matchedFile}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"备份文件不存在: {backupFilePath}");
                    }
                }
            }

            // 清理Temp目录（如果为空）
            try
            {
                if (Directory.Exists(tempBackupPath) && !Directory.EnumerateFileSystemEntries(tempBackupPath).Any())
                {
                    Directory.Delete(tempBackupPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理Temp目录失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"恢复脚本文件时发生错误: {scriptPath}");
        }
    }
}
