using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 统一读取编辑树的当前资源版本，不执行脚本、不修改资源或计划。
/// 调用方串行调用同一实例（任务计划页由 _resourceCheckGate 串行化），因此缓存使用普通字典。
/// </summary>
public sealed class PuloniaTaskResourceVersionService
{
    /// <summary>带数量、层级和重解析点保护的资源扫描入口。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;

    /// <summary>同一仓库版本内只读取一次资源指纹，定时检查不会反复读取正文。</summary>
    private readonly Dictionary<(string Repository, string Revision, string Path, string Type, bool Recursive, bool Directory), string> _versions = new();

    /// <summary>同一固定版本的声明版本只解析一次；空字符串表示正文没有声明可展示的版本号。</summary>
    private readonly Dictionary<(string Repository, string Revision, string Path, string Type), string> _declaredVersions = new();

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
        if (GetReference(task) is not { } reference)
            return await ReadLocalStateAsync(task, definitions, ct).ConfigureAwait(false);
        using var snapshot = await _catalog.Repositories.OpenCurrentAsync(reference.RepositoryId, ct).ConfigureAwait(false);
        return await ReadRepositoryStateAsync(task, reference, snapshot, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 一轮检查多个任务：每个仓库只打开一次当前快照，同一已确认版本最多打开一次归档，
    /// 声明版本先查缓存再打开归档。单项读取失败只记录在该项结果中，不影响其他任务。
    /// </summary>
    /// <param name="tasks">待检查的任务副本，读取期间不会被界面修改</param>
    /// <param name="definitions">本地资源解析路径所需的能力定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>与 <paramref name="tasks"/> 一一对应的检查结果</returns>
    public async Task<IReadOnlyList<PuloniaTaskResourceCheckResult>> ReadCurrentStatesAsync(IReadOnlyList<PuloniaTask> tasks,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct = default)
    {
        var results = new PuloniaTaskResourceCheckResult[tasks.Count];
        var repositoryTasks = new List<(int Index, ScriptResourceReference Reference)>();

        // 本地路径资源逐项读取；仓库资源先收集，再按仓库分组共用读取会话。
        for (var index = 0; index < tasks.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (GetReference(tasks[index]) is { } reference)
            {
                repositoryTasks.Add((index, reference));
                continue;
            }
            try { results[index] = new PuloniaTaskResourceCheckResult(await ReadLocalStateAsync(tasks[index], definitions, ct).ConfigureAwait(false)); }
            catch (Exception ex) when (IsResourceReadError(ex)) { results[index] = new PuloniaTaskResourceCheckResult(ex); }
        }

        foreach (var group in repositoryTasks.GroupBy(item => item.Reference.RepositoryId, StringComparer.Ordinal))
        {
            ScriptRepositorySnapshot snapshot;
            try { snapshot = await _catalog.Repositories.OpenCurrentAsync(group.Key, ct).ConfigureAwait(false); }
            catch (Exception ex) when (IsResourceReadError(ex))
            {
                // 来源不可读时，本仓库的全部任务共用同一个错误。
                foreach (var item in group) results[item.Index] = new PuloniaTaskResourceCheckResult(ex);
                continue;
            }
            // 本轮按已确认修订复用的归档读取会话；打开失败记为 null，本轮不再重试。
            var approvedSnapshots = new Dictionary<string, ScriptRepositorySnapshot?>(StringComparer.Ordinal);
            try
            {
                foreach (var (index, reference) in group)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        results[index] = new PuloniaTaskResourceCheckResult(await ReadRepositoryStateAsync(tasks[index], reference,
                            snapshot, approvedSnapshots, ct).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (IsResourceReadError(ex)) { results[index] = new PuloniaTaskResourceCheckResult(ex); }
                }
            }
            finally
            {
                snapshot.Dispose();
                foreach (var approved in approvedSnapshots.Values) approved?.Dispose();
            }
        }
        return results;
    }

    /// <summary>
    /// 资源缺失、不可读、路径或配置无效等可展示为节点检查错误的异常；其他异常属于程序错误，继续向上抛出。
    /// </summary>
    private static bool IsResourceReadError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or LibGit2Sharp.LibGit2SharpException;

    /// <summary>任务引用的仓库资源；本地路径资源返回 null。</summary>
    private static ScriptResourceReference? GetReference(PuloniaTask task) => task.Resource ?? task.Source?.Resource;

    /// <summary>在已打开的当前快照上读取仓库资源状态；指纹按仓库修订缓存，声明版本只用于展示。</summary>
    /// <param name="task">待检查的任务副本</param>
    /// <param name="reference">任务引用的仓库资源</param>
    /// <param name="snapshot">该仓库本轮的当前快照</param>
    /// <param name="approvedSnapshots">本轮复用的已确认版本读取会话；为 null 时按需单独打开</param>
    /// <param name="ct">取消令牌</param>
    private async Task<PuloniaTaskResourceVersionState> ReadRepositoryStateAsync(PuloniaTask task,
        ScriptResourceReference reference, ScriptRepositorySnapshot snapshot,
        Dictionary<string, ScriptRepositorySnapshot?>? approvedSnapshots, CancellationToken ct)
    {
        var type = task.Source?.TaskType ?? task.TaskType;
        var recursive = task.Source?.Recursive ?? true;
        var directory = task.Source?.Kind == "directory";
        var key = (reference.RepositoryId, snapshot.Revision, reference.RelativePath, type, recursive, directory);
        if (!_versions.TryGetValue(key, out var version))
        {
            version = await Task.Run(() => PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot,
                reference.RelativePath, type, recursive, directory), ct).ConfigureAwait(false);
            _versions[key] = version;
        }
        var currentDeclaredVersion = ReadDeclaredVersion(snapshot, reference.RelativePath, type, directory);
        // 旧版本由快照存储保留，提示缓存只保留当前来源版本，避免每次更新都累积指纹。
        foreach (var old in _versions.Keys.Where(k => k.Repository == reference.RepositoryId && k.Revision != snapshot.Revision).ToArray())
            _versions.Remove(old);
        var approvedDeclaredVersion = await TryReadApprovedDeclaredVersionAsync(reference, type, directory,
            approvedSnapshots, ct).ConfigureAwait(false);
        return new PuloniaTaskResourceVersionState(version, snapshot.Revision, approvedDeclaredVersion, currentDeclaredVersion);
    }

    /// <summary>读取本地路径资源与本地目录引用的当前指纹；不经过仓库存储。</summary>
    private async Task<PuloniaTaskResourceVersionState> ReadLocalStateAsync(PuloniaTask task,
        IReadOnlyList<PuloniaTaskDefinition> definitions, CancellationToken ct)
    {
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
            var version = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(path, ct).ConfigureAwait(false);
            var declared = await Task.Run(() => TryReadDeclaredVersion(() => Manifest.FromJson(
                File.ReadAllText(Path.Combine(path, "manifest.json"))).Version), ct).ConfigureAwait(false);
            return new PuloniaTaskResourceVersionState(version, null, null, declared);
        }
        var fileVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(path, ct).ConfigureAwait(false);
        var currentDeclared = task.TaskType == "pathing"
            ? await Task.Run(() => TryReadDeclaredVersion(() => JObject.Parse(File.ReadAllText(path))["info"]?["version"]?.Value<string>()), ct)
                .ConfigureAwait(false)
            : null;
        return new PuloniaTaskResourceVersionState(fileVersion, null, null, currentDeclared);
    }

    /// <summary>
    /// 从任务已经保留的精确版本读取声明版本；展示信息缺失不能影响内容更新检查。
    /// 已确认版本的正文不可变，先查缓存，命中时不再打开已保留归档。
    /// </summary>
    private async Task<string?> TryReadApprovedDeclaredVersionAsync(ScriptResourceReference reference, string taskType,
        bool directory, Dictionary<string, ScriptRepositorySnapshot?>? approvedSnapshots, CancellationToken ct)
    {
        if (directory) return null;
        if (_declaredVersions.TryGetValue((reference.RepositoryId, reference.ApprovedRevision, reference.RelativePath, taskType),
                out var cached))
            return cached.Length == 0 ? null : cached;
        ScriptRepositorySnapshot? owned = null;
        try
        {
            ScriptRepositorySnapshot? snapshot;
            if (approvedSnapshots is null)
                snapshot = owned = await _catalog.Repositories.OpenApprovedAsync(reference, ct).ConfigureAwait(false);
            else if (!approvedSnapshots.TryGetValue(reference.ApprovedRevision, out snapshot))
            {
                try { snapshot = await _catalog.Repositories.OpenApprovedAsync(reference, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { snapshot = null; }
                approvedSnapshots[reference.ApprovedRevision] = snapshot;
            }
            return snapshot is null ? null : ReadDeclaredVersion(snapshot, reference.RelativePath, taskType, directory);
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
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>从实际运行正文读取声明版本；目录引用没有唯一版本号，因此只返回 null。</summary>
    private string? ReadDeclaredVersion(ScriptRepositorySnapshot snapshot, string relativePath, string taskType,
        bool directory)
    {
        if (directory) return null;
        var key = (snapshot.Registration.Id, snapshot.Revision, relativePath, taskType);
        if (_declaredVersions.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;
        // 固定版本的正文不变，正文损坏得到的 null 同样缓存，定时检查不会反复解析。
        var value = TryReadDeclaredVersion(() => taskType switch
        {
            "javascript" => Manifest.FromJson(snapshot.ReadText("repo/" + relativePath + "/manifest.json")).Version,
            "pathing" => JObject.Parse(snapshot.ReadText("repo/" + relativePath))["info"]?["version"]?.Value<string>(),
            _ => null
        });
        _declaredVersions[key] = value ?? string.Empty;
        return value;
    }

    /// <summary>
    /// 读取仅用于展示的声明版本；正文不是有效 JSON 或无法读取时返回 null，
    /// 不能让一份损坏的 manifest 或路线遮蔽内容指纹检查结果，更新确认时再由内容校验拒绝。
    /// </summary>
    private static string? TryReadDeclaredVersion(Func<string?> read)
    {
        try { return NormalizeDeclaredVersion(read()); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
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

    /// <summary>
    /// 确认前固定最新来源并完成保留；失败时调用方不得修改计划引用。
    /// 修订与指纹核对、内容校验和保留在同一份固定来源上完成，不需要先释放来源锁再重新打开。
    /// </summary>
    public async Task<ScriptResourceReference?> PrepareUpdateAsync(PuloniaTask task, string expectedVersion,
        CancellationToken ct = default, string? expectedRevision = null)
    {
        if (GetReference(task) is not { } reference) return null;
        var type = task.Source?.TaskType ?? task.TaskType;
        var recursive = task.Source?.Recursive ?? true;
        var directory = task.Source?.Kind == "directory";
        return await _catalog.Repositories.RetainCurrentAsync(reference, type, snapshot =>
        {
            if (expectedRevision is not null && snapshot.Revision != expectedRevision)
                throw new InvalidOperationException("确认期间仓库版本再次变化，请重新检查。");
            var actual = PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot, reference.RelativePath,
                type, recursive, directory);
            if (actual != expectedVersion) throw new InvalidOperationException("确认期间资源再次变化，请重新检查。");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>拒绝未解析变量和空路径，不用错误路径生成可供确认的指纹。</summary>
    private static string ResolvePath(string? path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('{') || path.Contains('}'))
            throw new InvalidOperationException("资源路径为空或包含尚未解析的变量，请先检查节点配置。");
        return Path.GetFullPath(path, Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory));
    }
}
