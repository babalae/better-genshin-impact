using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>统一读取编辑树的当前资源版本，不执行脚本、不修改资源或计划。</summary>
public sealed class PuloniaTaskResourceVersionService
{
    /// <summary>带数量、层级和重解析点保护的资源扫描入口。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;

    /// <summary>同一仓库版本内只读取一次资源指纹，定时检查不会反复提取正文。</summary>
    private readonly ConcurrentDictionary<(string Repository, string Revision, string Path, string Type, bool Recursive, bool Directory), string> _versions = new();

    /// <summary>同一固定版本的声明版本只解析一次；空字符串表示正文没有声明可展示的版本号。</summary>
    private readonly ConcurrentDictionary<(string Repository, string Revision, string Path, string Type), string> _declaredVersions = new();

    /// <summary>复用页面资源目录服务，确保检查与用户确认采用相同的文件清单。</summary>
    public PuloniaTaskResourceVersionService(PuloniaTaskResourceCatalog catalog) => _catalog = catalog;

    /// <summary>读取资源的当前指纹；普通分组和纯任务没有外部资源，返回 null。</summary>
    public async Task<string?> ReadCurrentVersionAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
        => (await ReadCurrentStateAsync(task, definitions, ct).ConfigureAwait(false)).CurrentContentVersion;

    /// <summary>一次固定来源读取同时返回资源指纹、仓库修订和声明版本，确认操作不得接受未审阅的依赖版本。</summary>
    public async Task<PuloniaTaskResourceVersionState> ReadCurrentStateAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
    {
        var reference = task.Resource ?? task.Source?.Resource;
        if (reference is not null)
        {
            string? currentDeclaredVersion;
            string? version;
            string? revision;
            using var snapshot = await _catalog.Repositories.OpenCurrentAsync(reference.RepositoryId, ct).ConfigureAwait(false);
            var type = task.Source?.TaskType ?? task.TaskType;
            var recursive = task.Source?.Recursive ?? true;
            var key = (reference.RepositoryId, snapshot.Revision, reference.RelativePath, type, recursive, task.Source?.Kind == "directory");
            revision = snapshot.Revision;
            if (!_versions.TryGetValue(key, out version))
            {
                version = await Task.Run(() => ScriptRepositoryStore.ComputeVersion(snapshot, reference.RelativePath,
                    type, recursive, task.Source?.Kind == "directory"), ct).ConfigureAwait(false);
                _versions[key] = version;
            }
            currentDeclaredVersion = ReadDeclaredVersion(snapshot, reference.RelativePath, type,
                task.Source?.Kind == "directory");
            // 旧版本由快照存储保留，提示缓存只保留当前来源版本，避免每次更新都累积指纹。
            foreach (var old in _versions.Keys.Where(k => k.Repository == reference.RepositoryId && k.Revision != snapshot.Revision))
                _versions.TryRemove(old, out _);
            var approvedDeclaredVersion = await TryReadApprovedDeclaredVersionAsync(reference, type,
                task.Source?.Kind == "directory", ct).ConfigureAwait(false);
            return new PuloniaTaskResourceVersionState(version, revision, approvedDeclaredVersion, currentDeclaredVersion);
        }
        if (task is { TaskType: "group", Source.Kind: "directory" })
        {
            var directory = ResolvePath(task.Source.Path, null);
            var files = await _catalog.GetDirectoryFilesAsync(directory, "*.json", task.Source.Recursive, ct).ConfigureAwait(false);
            if (files.Count == 0)
                throw new IOException("引用目录中没有可运行的 JSON 资源，不能确认空版本。");
            return new PuloniaTaskResourceVersionState(
                await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(directory, files, ct).ConfigureAwait(false),
                null, null, null);
        }
        if (task.TaskType is not ("javascript" or "pathing" or "keymouse"))
            return new PuloniaTaskResourceVersionState(null, null, null, null);
        var definition = definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId == task.ResourceKey)
                         ?? definitions.FirstOrDefault(item => item.TaskType == task.TaskType && item.ResourceId is null)
                         ?? throw new InvalidOperationException($"没有注册任务类型 {task.TaskType}。");
        var path = ResolvePath(task.Path, definition.ResourceBaseDirectory);
        if (task.TaskType == "javascript")
        {
            var declared = await Task.Run(() => NormalizeDeclaredVersion(Manifest.FromJson(
                File.ReadAllText(Path.Combine(path, "manifest.json"))).Version), ct).ConfigureAwait(false);
            return new PuloniaTaskResourceVersionState(
                await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(path, ct).ConfigureAwait(false),
                null, null, declared);
        }
        var currentDeclared = task.TaskType == "pathing"
            ? await Task.Run(() => NormalizeDeclaredVersion(JObject.Parse(File.ReadAllText(path))["info"]?["version"]?.Value<string>()), ct)
                .ConfigureAwait(false)
            : null;
        return new PuloniaTaskResourceVersionState(
            await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(path, ct).ConfigureAwait(false),
            null, null, currentDeclared);
    }

    /// <summary>从任务已经保留的精确版本读取声明版本；展示信息缺失不能影响内容更新检查。</summary>
    private async Task<string?> TryReadApprovedDeclaredVersionAsync(ScriptResourceReference reference, string taskType,
        bool directory, CancellationToken ct)
    {
        try
        {
            using var snapshot = await _catalog.Repositories.OpenApprovedAsync(reference, ct).ConfigureAwait(false);
            return ReadDeclaredVersion(snapshot, reference.RelativePath, taskType, directory);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 声明版本仅用于展示，旧快照缺失或元数据损坏不能遮蔽内容指纹检查结果。
            return null;
        }
    }

    /// <summary>从实际运行正文读取声明版本；目录引用没有唯一版本号，因此只返回 null。</summary>
    private string? ReadDeclaredVersion(ScriptRepositorySnapshot snapshot, string relativePath, string taskType,
        bool directory)
    {
        if (directory) return null;
        var key = (snapshot.Registration.Id, snapshot.Revision, relativePath, taskType);
        if (_declaredVersions.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;
        string? value = taskType switch
        {
            "javascript" => Manifest.FromJson(snapshot.ReadText("repo/" + relativePath + "/manifest.json")).Version,
            "pathing" => JObject.Parse(snapshot.ReadText("repo/" + relativePath))["info"]?["version"]?.Value<string>(),
            _ => null
        };
        value = NormalizeDeclaredVersion(value);
        _declaredVersions[key] = value ?? string.Empty;
        return value;
    }

    /// <summary>限制声明版本的展示长度并清理换行，防止仓库元数据破坏紧凑任务树布局。</summary>
    private static string? NormalizeDeclaredVersion(string? value)
    {
        value = value?.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 64 ? value : value[..64];
    }

    /// <summary>使用一次完整检查结果确认新版本，调用方不需要重新拆分内容指纹和仓库修订。</summary>
    public Task<ScriptResourceReference?> PrepareUpdateAsync(PuloniaTask task,
        PuloniaTaskResourceVersionState expectedState, CancellationToken ct = default)
    {
        var expectedVersion = expectedState.CurrentContentVersion
                              ?? throw new InvalidOperationException("当前任务没有可更新的资源版本。");
        return PrepareUpdateAsync(task, expectedVersion, ct, expectedState.CurrentRepositoryRevision);
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
