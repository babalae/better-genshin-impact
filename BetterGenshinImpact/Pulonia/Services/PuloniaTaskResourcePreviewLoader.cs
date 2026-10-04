using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Core.Script.Repositories;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按需读取资源详情；索引阶段不调用本服务，避免解析不可见资源内容。
/// </summary>
public static class PuloniaTaskResourcePreviewLoader
{
    /// <summary>
    /// 加载一项资源的轻量详情；JS 只解析清单与设置定义，README 交给原生控件读取。
    /// </summary>
    public static async Task<PuloniaTaskResourcePreview> LoadAsync(PuloniaTaskResourceDescriptor resource,
        string? taskType, CancellationToken ct = default, ScriptRepositoryStore? repositories = null)
    {
        if (resource.Resource is { } reference && taskType == "javascript")
            resource = resource.WithFullPath(await (repositories ?? ScriptRepositoryStore.Shared)
                .PreparePreviewAsync(reference, ct).ConfigureAwait(false));
        var header = $"相对路径：{resource.Resource?.RelativePath ?? resource.RelativePath}\n最后修改：{resource.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        if (resource.IsDirectory && taskType is "pathing" or "keymouse")
            header += $"\n包含资源：{resource.ChildResourceCount} 项";
        else if (!resource.IsDirectory && (resource.Resource is null || resource.Size > 0))
            header += $"\n文件大小：{FormatFileSize(resource.Size)}";
        if (taskType != "javascript")
            return new PuloniaTaskResourcePreview(header, null);

        var manifestPath = Path.Combine(resource.FullPath, "manifest.json");
        var manifest = Manifest.FromJson(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));
        IReadOnlyList<SettingItem> settingItems = [];
        string? settingsError = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            settingItems = manifest.LoadSettingItems(resource.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                   or NotSupportedException)
        {
            settingsError = "无法解析脚本设置：" + ex.Message;
        }

        var readmePath = Path.Combine(resource.FullPath, "README.md");
        var markdownFilePath = File.Exists(readmePath) ? readmePath : null;
        var text = markdownFilePath is null ? "该脚本未提供 README.md。" : string.Empty;
        return new PuloniaTaskResourcePreview(text, manifest.Name, markdownFilePath, settingItems, settingsError);
    }

    /// <summary>
    /// 格式化文件大小供资源详情展示。
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024d:0.##} KB";
        return $"{bytes / 1024d / 1024d:0.##} MB";
    }
}
