using System.Text.Json;
using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>把大型 repo.json 扁平化为可分页搜索的内存索引。</summary>
public sealed class McpRepositoryIndex
{
    private static readonly object CacheLock = new();
    private static CacheEntry? _cache;

    public string SourceFile { get; }
    public string? RepositoryTime { get; }
    public string? DownloadUrl { get; }
    public IReadOnlyList<McpRepositoryNode> Nodes { get; }
    public IReadOnlyDictionary<string, McpRepositoryNode> ByPath { get; }

    private McpRepositoryIndex(
        string sourceFile,
        string? repositoryTime,
        string? downloadUrl,
        IReadOnlyList<McpRepositoryNode> nodes)
    {
        SourceFile = sourceFile;
        RepositoryTime = repositoryTime;
        DownloadUrl = downloadUrl;
        Nodes = nodes;
        ByPath = nodes.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
    }

    public static McpRepositoryIndex LoadCurrent()
    {
        var sourceFile = ResolveSourceFile();
        return LoadFile(sourceFile);
    }

    public static void Invalidate()
    {
        lock (CacheLock)
        {
            _cache = null;
        }
    }

    public static McpRepositoryIndex LoadFile(string sourceFile)
    {
        sourceFile = Path.GetFullPath(sourceFile);
        var info = new FileInfo(sourceFile);
        lock (CacheLock)
        {
            if (_cache is not null
                && _cache.Path.Equals(sourceFile, StringComparison.OrdinalIgnoreCase)
                && _cache.Length == info.Length
                && _cache.LastWriteUtc == info.LastWriteTimeUtc)
            {
                return _cache.Index;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(sourceFile), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = document.RootElement;
            var nodes = new List<McpRepositoryNode>(8192);
            if (root.TryGetProperty("indexes", out var indexes) && indexes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in indexes.EnumerateArray())
                {
                    Flatten(node, null, 0, nodes);
                }
            }
            var index = new McpRepositoryIndex(
                sourceFile,
                GetString(root, "time"),
                GetString(root, "url"),
                nodes);
            _cache = new CacheEntry(sourceFile, info.Length, info.LastWriteTimeUtc, index);
            return index;
        }
    }

    public IReadOnlyList<McpRepositoryNode> ChildrenOf(string? path)
    {
        var normalized = NormalizePath(path);
        return Nodes.Where(x => string.Equals(x.ParentPath, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.NodeType == "directory")
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim().Trim('/');

    private static string ResolveSourceFile()
    {
        if (File.Exists(ScriptRepoUpdater.RepoUpdatedJsonPath)) return ScriptRepoUpdater.RepoUpdatedJsonPath;
        var root = ScriptRepoUpdater.CenterRepoPath;
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("本地脚本仓库不存在；请先调用 bgi_update_script_repository。");
        return Directory.GetFiles(root, "repo.json", SearchOption.AllDirectories).FirstOrDefault()
               ?? throw new FileNotFoundException("本地脚本仓库中没有 repo.json；请先更新仓库。");
    }

    private static void Flatten(JsonElement element, string? parentPath, int depth, List<McpRepositoryNode> nodes)
    {
        var name = GetString(element, "name") ?? throw new JsonException("仓库节点缺少 name。");
        var path = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}/{name}";
        var rootType = path.Split('/', 2)[0];
        var tags = GetStringArray(element, "tags");
        var authors = GetAuthors(element);
        var childCount = element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array
            ? children.GetArrayLength()
            : 0;
        var node = new McpRepositoryNode(
            path,
            parentPath ?? string.Empty,
            name,
            rootType,
            GetString(element, "type") ?? (childCount > 0 ? "directory" : "file"),
            depth,
            childCount,
            GetString(element, "version"),
            GetString(element, "author"),
            authors,
            GetString(element, "description"),
            tags,
            GetString(element, "lastUpdated"),
            element.TryGetProperty("hasUpdate", out var hasUpdate) && hasUpdate.ValueKind == JsonValueKind.True);
        nodes.Add(node);
        if (childCount > 0)
        {
            foreach (var child in children.EnumerateArray()) Flatten(child, path, depth + 1, nodes);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : [];

    private static IReadOnlyList<McpRepositoryAuthor> GetAuthors(JsonElement element)
    {
        if (!element.TryGetProperty("authors", out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Select(x => new McpRepositoryAuthor(GetString(x, "name") ?? string.Empty, GetString(x, "link")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToArray();
    }

    private sealed record CacheEntry(string Path, long Length, DateTime LastWriteUtc, McpRepositoryIndex Index);
}

public sealed record McpRepositoryNode(
    string Path,
    string ParentPath,
    string Name,
    string RootType,
    string NodeType,
    int Depth,
    int ChildCount,
    string? Version,
    string? Author,
    IReadOnlyList<McpRepositoryAuthor> Authors,
    string? Description,
    IReadOnlyList<string> Tags,
    string? LastUpdated,
    bool HasUpdate);

public sealed record McpRepositoryAuthor(string Name, string? Link);
