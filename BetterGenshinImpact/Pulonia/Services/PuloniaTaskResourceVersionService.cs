using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>统一读取编辑树的当前资源版本，不执行脚本、不修改资源或计划。</summary>
public sealed class PuloniaTaskResourceVersionService
{
    /// <summary>带数量、层级和重解析点保护的资源扫描入口。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;

    /// <summary>复用页面资源目录服务，确保检查与用户确认采用相同的文件清单。</summary>
    public PuloniaTaskResourceVersionService(PuloniaTaskResourceCatalog catalog) => _catalog = catalog;

    /// <summary>读取资源的当前指纹；普通分组和纯任务没有外部资源，返回 null。</summary>
    public async Task<string?> ReadCurrentVersionAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
    {
        if (task is { TaskType: "group", Source.Kind: "directory" })
        {
            var directory = ResolvePath(task.Source.Path, null);
            var files = await _catalog.GetDirectoryFilesAsync(directory, "*.json", task.Source.Recursive, ct).ConfigureAwait(false);
            if (files.Count == 0)
                throw new IOException("引用目录中没有可运行的 JSON 资源，不能确认空版本。");
            return await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(directory, files, ct).ConfigureAwait(false);
        }
        if (task.TaskType is not ("javascript" or "pathing" or "keymouse"))
            return null;
        var definition = definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId == task.Path)
                         ?? definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId is null)
                         ?? throw new InvalidOperationException($"没有注册任务类型 {task.TaskType}。");
        var path = ResolvePath(task.Path, definition.ResourceBaseDirectory);
        if (task.TaskType == "javascript")
        {
            return await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(path, ct).ConfigureAwait(false);
        }
        return await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(path, ct).ConfigureAwait(false);
    }

    /// <summary>拒绝未解析变量和空路径，不用错误路径生成可供确认的指纹。</summary>
    private static string ResolvePath(string? path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}'))
            throw new InvalidOperationException("资源路径为空或包含尚未解析的变量，请先检查节点配置。");
        return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
    }
}
