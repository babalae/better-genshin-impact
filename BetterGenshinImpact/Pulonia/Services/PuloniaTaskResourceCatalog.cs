using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Core.Script.Repositories;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 为任务创建弹窗维护按能力分类的轻量本地资源索引。
/// </summary>
public sealed class PuloniaTaskResourceCatalog
{
    /// <summary>创建、预览与更新检查共用的仓库读取存储。</summary>
    public ScriptRepositoryStore Repositories { get; }

    /// <summary>使用共享仓库服务，测试可以传入独立的版本存储。</summary>
    public PuloniaTaskResourceCatalog(ScriptRepositoryStore? repositories = null)
        => Repositories = repositories ?? ScriptRepositoryStore.Shared;

    /// <summary>
    /// 单类资源最多索引的项目数。
    /// </summary>
    public const int MaxResourceCount = 10000;

    /// <summary>
    /// 单次扫描最多访问的目录数。
    /// </summary>
    private const int MaxVisitedDirectoryCount = 20000;

    /// <summary>
    /// 保护索引刷新与缓存替换。
    /// </summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// 以任务类型和完整根目录为键的进程内索引缓存。
    /// </summary>
    private readonly Dictionary<(string TaskType, string Root), PuloniaTaskResourceIndex> _cache = [];

    /// <summary>
    /// 读取能力资源索引；普通查询复用缓存，显式刷新才重新扫描磁盘。
    /// </summary>
    public async Task<PuloniaTaskResourceIndex> GetIndexAsync(PuloniaTaskDefinition definition,
        bool forceRefresh = false, CancellationToken ct = default, string? repositoryId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // 新路线和 JS 从已拉取来源建立索引，录制等能力继续使用原有本地资源目录。
        if (definition.TaskType is "pathing" or "javascript")
            return await GetRepositoryIndexAsync(definition.TaskType, forceRefresh, ct, repositoryId).ConfigureAwait(false);
        var root = string.IsNullOrWhiteSpace(definition.ResourceBaseDirectory)
            ? string.Empty
            : Path.GetFullPath(definition.ResourceBaseDirectory);
        var key = (definition.TaskType, root);
        if (!forceRefresh && TryGetCached(key, out var cached))
            return cached;

        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && TryGetCached(key, out cached))
                return cached;
            var index = await Task.Run(() => BuildIndex(definition, root, ct), ct).ConfigureAwait(false);
            lock (_cache)
                _cache[key] = index;
            return index;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>从固定版本的 repo.json 建立轻量清单，不遍历正文、不制作快照。</summary>
    private async Task<PuloniaTaskResourceIndex> GetRepositoryIndexAsync(string taskType, bool forceRefresh,
        CancellationToken ct, string? repositoryId)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var resources = new List<PuloniaTaskResourceDescriptor>();
            var retainedKeys = new HashSet<(string TaskType, string Root)>();
            var skipped = 0;
            var truncated = false;
            var repositories = await Repositories.GetRepositoriesAsync(ct).ConfigureAwait(false);
            if (repositoryId is not null && !repositories.Any(r => r.Id == repositoryId))
                throw new IOException("所选仓库已移除，请刷新仓库列表。");
            // 添加页面明确传入当前仓库，只为该来源读取索引；未选择的仓库不会被展开或解析。
            foreach (var repository in repositories.Where(r => repositoryId is null || r.Id == repositoryId))
            {
                try
                {
                    // 当前读取会话持有来源锁，必须逐仓库使用并释放，不能同时持有多个会话。
                    using var snapshot = await Repositories.OpenCurrentAsync(repository.Id, ct).ConfigureAwait(false);
                    var key = (taskType, repository.Id + "/" + snapshot.Revision + "/" + repository.Name);
                    retainedKeys.Add(key);
                    if (forceRefresh || !TryGetCached(key, out var index))
                    {
                        index = await Task.Run(() => BuildRepositoryIndex(snapshot, taskType, ct), ct).ConfigureAwait(false);
                        lock (_cache) _cache[key] = index;
                    }
                    var room = MaxResourceCount - resources.Count;
                    resources.AddRange(index.Resources.Take(room));
                    truncated |= index.IsTruncated || index.Resources.Count > room;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or FormatException or Newtonsoft.Json.JsonException or LibGit2Sharp.LibGit2SharpException)
                {
                    if (repositoryId is not null) throw new IOException("无法读取所选仓库：" + ex.Message, ex);
                    skipped++;
                }
            }
            lock (_cache)
            {
                // 仓库更新后的旧索引由当前打开弹窗持有，缓存仅保留最新版本，避免长期累积。
                foreach (var old in _cache.Keys.Where(k => k.TaskType == taskType && !retainedKeys.Contains(k)
                             && (repositoryId is null || k.Root.StartsWith(repositoryId + "/", StringComparison.Ordinal))).ToArray())
                    _cache.Remove(old);
            }
            return new PuloniaTaskResourceIndex(resources, truncated, skipped);
        }
        finally { _refreshGate.Release(); }
    }

    /// <summary>复用仓库 Web 索引的 name/type/children 协议，JS 项目元数据无需读取 manifest 正文。</summary>
    private static PuloniaTaskResourceIndex BuildRepositoryIndex(ScriptRepositorySnapshot snapshot, string taskType, CancellationToken ct)
    {
        var prefix = taskType == "javascript" ? "js" : "pathing";
        var indexes = JObject.Parse(snapshot.ReadText("repo.json"))["indexes"] as JArray
                      ?? throw new IOException("仓库索引缺少 indexes 清单。");
        var root = indexes.OfType<JObject>().FirstOrDefault(n => n.Value<string>("name") == prefix);
        if (root is null) return new PuloniaTaskResourceIndex([], false, 0);
        var projects = new List<(string Path, DateTime Time)>();
        var files = new List<(string Path, long Size, DateTime Time)>();
        var visited = 0;
        var truncated = false;
        Visit(root, "", 0);
        var resources = new List<PuloniaTaskResourceDescriptor>();
        if (taskType == "javascript")
        {
            foreach (var project in projects) AddResource(project.Path, true, false, 0, 0, project.Time);
        }
        else
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal) { [""] = files.Count };
            foreach (var file in files)
            {
                var parent = file.Path;
                while (parent.LastIndexOf('/') is var separator && separator >= 0)
                {
                    parent = parent[..separator];
                    counts[parent] = counts.GetValueOrDefault(parent) + 1;
                }
            }
            // 先发布父目录再发布文件，达到索引上限时仍有可见的仓库根节点。
            foreach (var item in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
                AddResource(item.Key, true, item.Key.Length == 0, 0, item.Value, snapshot.LastWriteTime);
            foreach (var file in files) AddResource(file.Path, false, false, file.Size, 0, file.Time);
        }
        return new PuloniaTaskResourceIndex(resources, truncated, 0);

        // 只遍历当前资源类型的索引节点；其他资源正文和 hasUpdate 标记均不参与计划状态。
        void Visit(JObject node, string relative, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > PuloniaTaskValidator.MaxTreeDepth || ++visited > 100000)
                throw new IOException("仓库索引超过层级或节点数量上限。");
            var children = node["children"] as JArray;
            var type = node.Value<string>("type");
            var time = DateTime.TryParse(node.Value<string>("lastUpdated"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var updated) ? updated : snapshot.LastWriteTime;
            if (taskType == "javascript" && depth > 0 && type == "directory"
                && (node["version"] is not null || children is null || children.Count == 0
                    || children.OfType<JObject>().Any(c => c.Value<string>("name") == "manifest.json")))
            {
                if (projects.Count >= MaxResourceCount) { truncated = true; return; }
                projects.Add((relative, time));
                return;
            }
            if (taskType == "pathing" && type == "file" && relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (files.Count >= MaxResourceCount) { truncated = true; return; }
                files.Add((relative, node.Value<long?>("size") ?? 0, time));
                return;
            }
            if (children is null) return;
            foreach (var child in children.OfType<JObject>())
            {
                var name = child.Value<string>("name") ?? throw new IOException("索引资源名称为空。");
                if (name.Contains('/') || ScriptRepositorySnapshot.NormalizePath(name) != name)
                    throw new IOException("索引资源名称包含无效路径。");
                Visit(child, relative.Length == 0 ? name : relative + "/" + name, depth + 1);
            }
        }

        // 展示路径包含来源前缀，树形目录与同名资源均不会跨仓库合并。
        void AddResource(string relative, bool directory, bool rootDirectory, long size, int count, DateTime time)
        {
            if (resources.Count >= MaxResourceCount) { truncated = true; return; }
            var reference = new ScriptResourceReference
            {
                RepositoryId = snapshot.Registration.Id, ApprovedRevision = snapshot.Revision,
                RelativePath = prefix + (relative.Length == 0 ? "" : "/" + relative)
            };
            var display = rootDirectory ? snapshot.Registration.Name : directory ? Path.GetFileName(relative)
                : Path.GetFileNameWithoutExtension(relative);
            resources.Add(new PuloniaTaskResourceDescriptor(display,
                snapshot.Registration.Id + (relative.Length == 0 ? "" : "/" + relative), string.Empty,
                snapshot.Registration.Name + (relative.Length == 0 ? "" : " / " + relative),
                directory, rootDirectory, size, time, count, reference));
        }
    }

    /// <summary>
    /// 为确认目录引用或展开任务完整枚举文件；超过上限或读取失败时拒绝返回部分清单。
    /// </summary>
    public Task<IReadOnlyList<string>> GetDirectoryFilesAsync(string directory, string pattern,
        bool recursive, CancellationToken ct = default)
        => Task.Run<IReadOnlyList<string>>(() => EnumerateDirectoryFiles(directory, pattern, recursive, ct), ct);

    /// <summary>
    /// 尝试读取一份已经建立的索引。
    /// </summary>
    private bool TryGetCached((string TaskType, string Root) key, out PuloniaTaskResourceIndex index)
    {
        lock (_cache)
            return _cache.TryGetValue(key, out index!);
    }

    /// <summary>
    /// 按任务类型建立资源索引，不读取路线 JSON 正文和 JS README。
    /// </summary>
    private static PuloniaTaskResourceIndex BuildIndex(PuloniaTaskDefinition definition, string root,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new PuloniaTaskResourceIndex([], false, 0);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            return new PuloniaTaskResourceIndex([], false, 1);
        return definition.TaskType == "javascript"
            ? BuildJavaScriptIndex(root, ct)
            : BuildJsonResourceIndex(definition.TaskType, root, ct);
    }

    /// <summary>
    /// 索引包含 manifest.json 的 JS 项目，找到项目后不再深入其内部依赖目录。
    /// </summary>
    private static PuloniaTaskResourceIndex BuildJavaScriptIndex(string root, CancellationToken ct)
    {
        var projectPaths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var visitedDirectoryCount = 0;
        var skippedDirectoryCount = 0;
        var truncated = false;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (++visitedDirectoryCount > MaxVisitedDirectoryCount || projectPaths.Count >= MaxResourceCount)
            {
                truncated = true;
                break;
            }
            if (directory != root && File.Exists(Path.Combine(directory, "manifest.json")))
            {
                projectPaths.Add(directory);
                continue;
            }
            foreach (var child in EnumerateChildDirectories(directory, ref skippedDirectoryCount)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                pending.Push(child);
        }

        var resources = projectPaths.Select(path => new PuloniaTaskResourceDescriptor(
                Path.GetFileName(path), Path.GetRelativePath(root, path), path,
                Path.GetDirectoryName(Path.GetRelativePath(root, path)) ?? string.Empty,
                true, false, 0, Directory.GetLastWriteTime(path), 0))
            .OrderBy(resource => resource.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PuloniaTaskResourceIndex(resources, truncated, skippedDirectoryCount);
    }

    /// <summary>
    /// 索引路线或录制 JSON 文件及其目录，目录项携带可批量添加的文件数量。
    /// </summary>
    private static PuloniaTaskResourceIndex BuildJsonResourceIndex(string taskType, string root,
        CancellationToken ct)
    {
        var directoryPaths = new List<string> { root };
        var filePaths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        var visitedDirectoryCount = 0;
        var skippedDirectoryCount = 0;
        var truncated = false;
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (++visitedDirectoryCount > MaxVisitedDirectoryCount)
            {
                truncated = true;
                break;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    if (filePaths.Count >= MaxResourceCount)
                    {
                        truncated = true;
                        break;
                    }
                    filePaths.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skippedDirectoryCount++;
                continue;
            }
            if (truncated)
                break;
            foreach (var child in EnumerateChildDirectories(directory, ref skippedDirectoryCount)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                directoryPaths.Add(child);
                pending.Push(child);
            }
        }

        var childCounts = BuildDirectoryChildCounts(root, directoryPaths, filePaths);
        var rootDisplayName = taskType == "pathing" ? "全部路线" : "全部录制";
        var resources = new List<PuloniaTaskResourceDescriptor>(directoryPaths.Count + filePaths.Count);
        foreach (var path in directoryPaths)
        {
            var isRoot = string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
            var relative = isRoot ? "." : Path.GetRelativePath(root, path);
            resources.Add(new PuloniaTaskResourceDescriptor(
                isRoot ? rootDisplayName : Path.GetFileName(path), relative, path,
                isRoot ? "资源根目录" : Path.GetDirectoryName(relative) ?? string.Empty,
                true, isRoot, 0, Directory.GetLastWriteTime(path), childCounts.GetValueOrDefault(path)));
        }
        foreach (var path in filePaths)
        {
            var relative = Path.GetRelativePath(root, path);
            var info = new FileInfo(path);
            resources.Add(new PuloniaTaskResourceDescriptor(Path.GetFileNameWithoutExtension(path), relative,
                path, Path.GetDirectoryName(relative) ?? string.Empty, false, false, info.Length,
                info.LastWriteTime, 0));
        }
        resources.Sort((left, right) =>
        {
            if (left.IsRootDirectory != right.IsRootDirectory)
                return left.IsRootDirectory ? -1 : 1;
            if (left.IsDirectory != right.IsDirectory)
                return left.IsDirectory ? -1 : 1;
            return StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
        });
        return new PuloniaTaskResourceIndex(resources, truncated, skippedDirectoryCount);
    }

    /// <summary>
    /// 统计每个已索引目录下的递归 JSON 文件数量。
    /// </summary>
    private static Dictionary<string, int> BuildDirectoryChildCounts(string root,
        IReadOnlyList<string> directories, IReadOnlyList<string> files)
    {
        var counts = directories.ToDictionary(path => path, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var parent = Path.GetDirectoryName(file);
            while (parent is not null && IsWithinRoot(root, parent))
            {
                if (counts.ContainsKey(parent))
                    counts[parent]++;
                if (string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                    break;
                parent = Path.GetDirectoryName(parent);
            }
        }
        return counts;
    }

    /// <summary>
    /// 枚举可进入的一级子目录并跳过重解析点。
    /// </summary>
    private static IEnumerable<string> EnumerateChildDirectories(string directory, ref int skippedDirectoryCount)
    {
        string[] children;
        try
        {
            children = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skippedDirectoryCount++;
            return [];
        }

        var result = new List<string>(children.Length);
        foreach (var child in children)
        {
            try
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    result.Add(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skippedDirectoryCount++;
            }
        }
        return result;
    }

    /// <summary>
    /// 判断目录仍位于规范化资源根目录内部。
    /// </summary>
    private static bool IsWithinRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
            StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// 完整枚举一个待确认目录，保证批量添加不会因安全上限静默缺项。
    /// </summary>
    private static IReadOnlyList<string> EnumerateDirectoryFiles(string directory, string pattern,
        bool recursive, CancellationToken ct)
    {
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"资源目录不存在：{root}");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("资源目录不能直接指向重解析点。");

        var files = new List<string>();
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        var directoryCount = 1;
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in Directory.EnumerateFiles(current.Directory, pattern, SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (files.Count >= MaxResourceCount)
                    throw new IOException($"资源目录超过 {MaxResourceCount} 项，不能只添加部分内容。");
                files.Add(file);
            }
            if (!recursive)
                continue;
            if (current.Depth >= PuloniaTaskValidator.MaxTreeDepth)
                throw new IOException("资源目录层级超过任务树上限。");
            foreach (var child in Directory.EnumerateDirectories(current.Directory)
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (++directoryCount > MaxVisitedDirectoryCount)
                    throw new IOException("资源目录数量超过安全扫描上限。");
                pending.Push((child, current.Depth + 1));
            }
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
