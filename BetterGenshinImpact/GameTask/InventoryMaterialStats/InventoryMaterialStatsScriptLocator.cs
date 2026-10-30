using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.InventoryMaterialStats;

/// <summary>
/// 定位已安装的「背包材料统计」JS 拷贝。
/// </summary>
public static class InventoryMaterialStatsScriptLocator
{
    public const string ManifestName = "背包统计采集系统";

    public const string DefaultFolderName = "背包材料统计";

    public const string ImagesRelativePath = "assets/images";

    /// <summary>
    /// 枚举 <c>User/JsScript</c> 下清单名匹配且含模板图的拷贝。
    /// </summary>
    public static IReadOnlyList<InventoryMaterialStatsScriptCopy> FindInstalledCopies()
    {
        var scriptRoot = Global.ScriptPath();
        if (!Directory.Exists(scriptRoot))
        {
            return [];
        }

        var result = new List<InventoryMaterialStatsScriptCopy>();
        foreach (var dir in Directory.GetDirectories(scriptRoot))
        {
            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName))
            {
                continue;
            }

            try
            {
                var project = new ScriptProject(folderName);
                if (!string.Equals(project.Manifest.Name, ManifestName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!HasPngTemplates(project.ProjectPath))
                {
                    continue;
                }

                result.Add(new InventoryMaterialStatsScriptCopy(folderName, project.ProjectPath, project.Manifest.Name));
            }
            catch (Exception)
            {
                // 非 JS 或清单损坏的目录跳过
            }
        }

        return result.OrderBy(c => c.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 解析已安装拷贝；没有脚本时返回 null（首次可只靠本地已识别图）。
    /// </summary>
    public static InventoryMaterialStatsScriptCopy? TryResolve(string? folderName)
    {
        var copies = FindInstalledCopies();
        if (copies.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(folderName))
        {
            var matched = copies.FirstOrDefault(c =>
                string.Equals(c.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return copies[0];
    }

    /// <summary>
    /// 按文件夹名解析一份已安装拷贝。
    /// </summary>
    public static InventoryMaterialStatsScriptCopy Resolve(string? folderName)
    {
        return TryResolve(folderName)
               ?? throw new InvalidOperationException(
                   "请先在脚本仓库订阅「背包材料统计」（清单名：背包统计采集系统），并确保 assets/images 下有材料模板图。");
    }

    public static bool HasPngTemplates(string projectPath)
    {
        var images = Path.Combine(projectPath, ImagesRelativePath);
        if (!Directory.Exists(images))
        {
            return false;
        }

        return Directory.EnumerateFiles(images, "*.png", SearchOption.AllDirectories).Any();
    }
}

/// <summary>
/// 一份已安装的背包材料统计 JS 拷贝。
/// </summary>
public sealed record InventoryMaterialStatsScriptCopy(string FolderName, string ProjectPath, string ManifestName);
