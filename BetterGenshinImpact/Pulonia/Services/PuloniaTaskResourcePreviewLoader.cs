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
    /// 仓库 JS 预览持有一次性预览目录，调用方在不再显示时释放返回的预览。
    /// </summary>
    public static async Task<PuloniaTaskResourcePreview> LoadAsync(PuloniaTaskResourceDescriptor resource,
        string? taskType, CancellationToken ct = default, ScriptRepositoryStore? repositories = null)
    {
        ScriptPreviewLease? lease = null;
        if (resource.Resource is { } reference && taskType == "javascript")
        {
            lease = await (repositories ?? ScriptRepositoryStore.Shared).PreparePreviewAsync(reference, ct).ConfigureAwait(false);
            resource = resource.WithFullPath(lease.Path);
        }
        try
        {
            return await LoadCoreAsync(resource, taskType, lease, ct).ConfigureAwait(false);
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 读取已有 JS 任务所固定项目的清单与设置定义：仓库资源读取节点保存的精确版本，本地资源按能力资源根目录解析路径。
    /// 编辑配置只需要设置定义，调用方读取后应立即释放返回的预览。
    /// </summary>
    /// <param name="task">JS 任务模型。</param>
    /// <param name="resourceBaseDirectory">本地相对路径的能力资源根目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="repositories">仓库存储；为空时使用共享实例。</param>
    /// <exception cref="InvalidOperationException">本地路径为空或包含尚未解析的变量。</exception>
    public static Task<PuloniaTaskResourcePreview> LoadJavaScriptTaskAsync(PuloniaTask task, string? resourceBaseDirectory,
        CancellationToken ct = default, ScriptRepositoryStore? repositories = null)
    {
        var fullPath = string.Empty;
        if (task.Resource is null)
        {
            // 路径变量只在运行准备时解析，编辑时无法确定实际项目
            if (string.IsNullOrWhiteSpace(task.Path) || task.Path.Contains('{') || task.Path.Contains('}'))
                throw new InvalidOperationException("项目路径为空或包含尚未解析的变量，无法读取脚本设置。");
            fullPath = Path.GetFullPath(task.Path, Path.GetFullPath(resourceBaseDirectory ?? AppContext.BaseDirectory));
        }
        // 仓库资源的本地目录由预览租约提供，这里只需保留资源身份
        var resource = new PuloniaTaskResourceDescriptor(task.Name, task.Resource?.RelativePath ?? task.Path!, fullPath,
            string.Empty, isDirectory: true, isRootDirectory: false, size: 0, lastWriteTime: DateTime.MinValue,
            childResourceCount: 0, task.Resource);
        return LoadAsync(resource, "javascript", ct, repositories);
    }

    /// <summary>
    /// 从本地路径或已准备的预览目录读取详情；预览租约随结果返回。
    /// </summary>
    private static async Task<PuloniaTaskResourcePreview> LoadCoreAsync(PuloniaTaskResourceDescriptor resource,
        string? taskType, ScriptPreviewLease? lease, CancellationToken ct)
    {
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
        return new PuloniaTaskResourcePreview(text, manifest.Name, markdownFilePath, settingItems, settingsError, lease);
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
