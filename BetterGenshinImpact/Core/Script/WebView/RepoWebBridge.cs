using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Message;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json.Linq;
using System.Net;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.Core.Script.WebView;

/// <summary>
/// 给 WebView 提供的桥接类
/// 用于调用 C# 方法
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public sealed class RepoWebBridge
{
    private static readonly HashSet<string> AllowedTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".js", ".ts",
        ".vue", ".css", ".html", ".csv", ".xml",
        ".yaml", ".yml", ".ini", ".config"
    };
    
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"
    };
    
    public async Task<string> GetRepoJson()
    {
        try
        {
            if (!Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                throw new InvalidOperationException("仓库文件夹不存在，请至少成功更新一次仓库！");
            }

            string localRepoJsonPath = GetRepoJsonPath();
            return await File.ReadAllTextAsync(localRepoJsonPath);
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync(ex.Message, "获取仓库信息失败");
            return string.Empty;
        }
    }

    public async void ImportUri(string url)
    {
        try
        {
            await ScriptRepoUpdater.Instance.ImportScriptFromUri(url, false);
            WeakReferenceMessenger.Default.Send(new RefreshDataMessage("Refresh"));
        }
        catch (Exception e)
        {
            await ThemedMessageBox.ErrorAsync(e.Message, "订阅脚本链接失败！");
        }
    }

    public async Task<string> GetUserConfigJson()
    {
        string userConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "User", "config.json");
        
        if (!File.Exists(userConfigPath))
        {
            await ThemedMessageBox.ErrorAsync($"用户配置文件不存在: {userConfigPath}", "获取用户配置失败");
            return string.Empty;
        }

        return await File.ReadAllTextAsync(userConfigPath);
    }

    /// <summary>
    /// 获取当前仓库的已订阅脚本路径列表（JSON 数组）。
    /// 相比 GetUserConfigJson() 更轻量，仅返回当前仓库的订阅路径。
    /// </summary>
    public string GetSubscribedScriptPaths()
    {
        try
        {
            var paths = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo();
            if (paths.Count > 0)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(paths);
            }

            return "[]";
        }
        catch
        {
            return "[]";
        }
    }

    public Task<string> GetFile(string relPath)
    {
        try
        {
            // URL 解码路径（处理中文文件名）
            relPath = WebUtility.UrlDecode(relPath);
        
            string filePath = Path.Combine(ScriptRepoUpdater.CenterRepoPath, "repo", relPath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            
            // 验证解析后的路径在允许的目录范围内
            string normalizedBasePath = Path.GetFullPath(Path.Combine(ScriptRepoUpdater.CenterRepoPath, "repo"));
            string normalizedFilePath = Path.GetFullPath(filePath);
            if (!normalizedFilePath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("404");
            }

            string extension = Path.GetExtension(filePath).ToLower();
    
            if (AllowedTextExtensions.Contains(extension)) 
            {
                // 读取文本文件
                string? content = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(relPath);
                return Task.FromResult(string.IsNullOrEmpty(content) ? "404" : content);
            }
            else if (AllowedImageExtensions.Contains(extension))
            {
                // 读取图片文件，返回 Base64 编码
                byte[]? bytes = ScriptRepoUpdater.Instance.ReadBinaryFileFromCenterRepo(relPath);
                if (bytes == null || bytes.Length == 0)
                {
                    return Task.FromResult("404");
                }

                string base64 = Convert.ToBase64String(bytes);
                return Task.FromResult(base64);
            }

            return Task.FromResult("404");
        }
        catch
        {
            return Task.FromResult("404");
        }
    }

    private static string GetMimeType(string extension)
    {
        return extension.ToLower() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }

    public async Task<bool> UpdateSubscribed(string path)
    {
        try
        {
            if (!Directory.Exists(ScriptRepoUpdater.CenterRepoPath))
            {
                throw new InvalidOperationException("仓库文件夹不存在，请至少成功更新一次仓库！");
            }

            string localRepoJsonPath = GetRepoJsonPath();
            string json = await File.ReadAllTextAsync(localRepoJsonPath);
            
            var jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json);
            if (jsonObj?["indexes"] is not JArray indexes) return false;

            string[] pathParts = path.Split('/');
            ProcessPathRecursively(indexes, pathParts, 0);
            
            string modifiedJson = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(localRepoJsonPath, modifiedJson);
            
            return true;
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync(ex.Message, "信息更新失败");
            return false;
        }
    }
    
    public async Task<bool> ClearUpdate()
    {
        try
        {
            string? repoJsonPath = Directory
                .GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(repoJsonPath))
            {
                throw new FileNotFoundException("找不到原始 repo.json 文件");
            }

            string targetPath = ScriptRepoUpdater.RepoUpdatedJsonPath;

            File.Copy(repoJsonPath, targetPath, overwrite: true);

            return true;
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync($"清空更新标记失败: {ex.Message}", "操作失败");
            return false;
        }
    }

    // 设置新手引导标志位
    public bool SetGuideStatus(bool status)
    {
        try
        {
            var scriptConfig = TaskContext.Instance().Config.ScriptConfig;
            scriptConfig.GuideStatus = status;
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }
    
    // 获取新手引导标志位
    public bool GetGuideStatus()
    {
        try
        {
            return TaskContext.Instance().Config.ScriptConfig.GuideStatus;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }
    }

    private static string GetRepoJsonPath()
    {
        string updatedRepoJsonPath = ScriptRepoUpdater.RepoUpdatedJsonPath;

        if (File.Exists(updatedRepoJsonPath))
        {
            return updatedRepoJsonPath;
        }

        string? repoJson = Directory
            .GetFiles(ScriptRepoUpdater.CenterRepoPath, "repo.json", SearchOption.AllDirectories)
            .FirstOrDefault();

        return repoJson ?? throw new FileNotFoundException("repo.json 仓库索引文件不存在，请至少成功更新一次仓库！");
    }

    internal static void ProcessPathRecursively(JArray array, string[] pathParts, int currentIndex)
    {
        foreach (JObject item in array.OfType<JObject>())
        {
            if (item["name"]?.ToString() != pathParts[currentIndex]) continue;
            
            if (currentIndex == pathParts.Length - 1)
            {
                ResetHasUpdateFlag(item);
            }
            else if (item["children"] is JArray children)
            {
                ProcessPathRecursively(children, pathParts, currentIndex + 1);
            }
            break;
        }
    }

    internal static void ResetHasUpdateFlag(JObject node)
    {
        if (node["hasUpdate"] is { Type: JTokenType.Boolean } hasUpdate && 
            (bool)hasUpdate)
        {
            node["hasUpdate"] = false;
        }
        
        if (node["children"] is JArray children)
        {
            foreach (JObject child in children.OfType<JObject>())
            {
                ResetHasUpdateFlag(child);
            }
        }
    }
}
