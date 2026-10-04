using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>统一读取编辑树的当前资源版本，不执行脚本、不修改资源或计划。</summary>
public sealed class PuloniaTaskResourceVersionService
{
    /// <summary>带数量、层级和重解析点保护的资源扫描入口。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;

    /// <summary>同一仓库版本内只读取一次资源指纹，定时检查不会反复提取正文。</summary>
    private readonly ConcurrentDictionary<(string Repository, string Revision, string Path, string Type, bool Recursive, bool Directory), string> _versions = new();

    /// <summary>复用页面资源目录服务，确保检查与用户确认采用相同的文件清单。</summary>
    public PuloniaTaskResourceVersionService(PuloniaTaskResourceCatalog catalog) => _catalog = catalog;

    /// <summary>读取资源的当前指纹；普通分组和纯任务没有外部资源，返回 null。</summary>
    public async Task<string?> ReadCurrentVersionAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
        => (await ReadCurrentStateAsync(task, definitions, ct).ConfigureAwait(false)).Version;

    /// <summary>一次固定来源读取同时返回资源指纹和提交身份，确认操作不得接受未审阅的依赖版本。</summary>
    public async Task<(string? Version, string? Revision)> ReadCurrentStateAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
    {
        var reference = task.Resource ?? task.Source?.Resource;
        if (reference is not null)
        {
            using var snapshot = await _catalog.Repositories.OpenCurrentAsync(reference.RepositoryId, ct).ConfigureAwait(false);
            var type = task.Source?.TaskType ?? task.TaskType;
            var recursive = task.Source?.Recursive ?? true;
            var key = (reference.RepositoryId, snapshot.Revision, reference.RelativePath, type, recursive, task.Source?.Kind == "directory");
            if (_versions.TryGetValue(key, out var cached)) return (cached, snapshot.Revision);
            var version = await Task.Run(() => ScriptRepositoryStore.ComputeVersion(snapshot, reference.RelativePath,
                type, recursive, task.Source?.Kind == "directory"), ct).ConfigureAwait(false);
            _versions[key] = version;
            // 旧版本由快照存储保留，提示缓存只保留当前来源版本，避免每次更新都累积指纹。
            foreach (var old in _versions.Keys.Where(k => k.Repository == reference.RepositoryId && k.Revision != snapshot.Revision))
                _versions.TryRemove(old, out _);
            return (version, snapshot.Revision);
        }
        if (task is { TaskType: "group", Source.Kind: "directory" })
        {
            var directory = ResolvePath(task.Source.Path, null);
            var files = await _catalog.GetDirectoryFilesAsync(directory, "*.json", task.Source.Recursive, ct).ConfigureAwait(false);
            if (files.Count == 0)
                throw new IOException("引用目录中没有可运行的 JSON 资源，不能确认空版本。");
            return (await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(directory, files, ct).ConfigureAwait(false), null);
        }
        if (task.TaskType is not ("javascript" or "pathing" or "keymouse"))
            return (null, null);
        var definition = definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId == task.ResourceKey)
                         ?? definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId is null)
                         ?? throw new InvalidOperationException($"没有注册任务类型 {task.TaskType}。");
        var path = ResolvePath(task.Path, definition.ResourceBaseDirectory);
        if (task.TaskType == "javascript")
        {
            return (await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(path, ct).ConfigureAwait(false), null);
        }
        return (await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(path, ct).ConfigureAwait(false), null);
    }

    /// <summary>确认前固定最新来源并完成提取；失败时调用方不得修改计划引用。</summary>
    public async Task<ScriptResourceReference?> PrepareUpdateAsync(PuloniaTask task, string expectedVersion,
        CancellationToken ct = default, string? expectedRevision = null)
    {
        var reference = task.Resource ?? task.Source?.Resource;
        if (reference is null) return null;
        ScriptResourceReference updated;
        var type = task.Source?.TaskType ?? task.TaskType;
        using (var snapshot = await _catalog.Repositories.OpenCurrentAsync(reference.RepositoryId, ct).ConfigureAwait(false))
        {
            if (expectedRevision is not null && snapshot.Revision != expectedRevision)
                throw new InvalidOperationException("确认期间仓库版本再次变化，请重新检查。");
            var actual = await Task.Run(() => ScriptRepositoryStore.ComputeVersion(snapshot, reference.RelativePath,
                type, task.Source?.Recursive ?? true, task.Source?.Kind == "directory"), ct).ConfigureAwait(false);
            if (actual != expectedVersion) throw new InvalidOperationException("确认期间资源再次变化，请重新检查。");
            updated = reference.WithRevision(snapshot.Revision);
        }
        // 提取使用刚审阅的精确版本；先释放来源读取锁，再进入快照发布锁，避免重入死锁。
        await _catalog.Repositories.MaterializeAsync(updated, type, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>拒绝未解析变量和空路径，不用错误路径生成可供确认的指纹。</summary>
    private static string ResolvePath(string? path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}'))
            throw new InvalidOperationException("资源路径为空或包含尚未解析的变量，请先检查节点配置。");
        return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
    }
}
