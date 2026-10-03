using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 为任务创建弹窗维护按能力分类的轻量本地资源索引。
/// </summary>
public sealed class PuloniaTaskResourceCatalog
{
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
        bool forceRefresh = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
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
