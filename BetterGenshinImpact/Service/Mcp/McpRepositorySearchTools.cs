using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace BetterGenshinImpact.Service.Mcp;

[McpServerToolType]
public sealed class McpRepositorySearchTools(
    McpApplicationServices application,
    McpDetachedTaskRegistry detachedTaskRegistry)
{
    [McpServerTool(Name = "bgi_refresh_repository_index", ReadOnly = true, Idempotent = true),
     Description("丢弃 MCP 的仓库内存索引并立即重新解析当前 repo.json/repo_updated.json。仓库更新工具会自动刷新；仅在文件被外部程序修改或需要强制确认最新内容时调用。")]
    public static object RefreshRepositoryIndex()
    {
        McpRepositoryIndex.Invalidate();
        var index = McpRepositoryIndex.LoadCurrent();
        return new
        {
            refreshed = true, sourceFile = index.SourceFile, index.RepositoryTime, totalNodes = index.Nodes.Count
        };
    }

    [McpServerTool(Name = "bgi_get_repository_index_summary", ReadOnly = true, Idempotent = true),
     Description("读取本地大型脚本仓库索引的摘要、顶层路线、节点/文件数量和更新时间。AI 查找路线或脚本时应先调用本工具，而不是读取完整 repo.json。")]
    public static object GetRepositoryIndexSummary()
    {
        var index = McpRepositoryIndex.LoadCurrent();
        var roots = index.Nodes.Where(x => x.Depth == 0)
            .Select(root => new
            {
                root = root.Path,
                purpose = RootPurpose(root.RootType),
                directChildren = root.ChildCount,
                totalNodes = index.Nodes.Count(x => x.Path.Equals(root.Path, StringComparison.OrdinalIgnoreCase)
                                                    || x.Path.StartsWith(root.Path + '/',
                                                        StringComparison.OrdinalIgnoreCase)),
                files = index.Nodes.Count(x =>
                    x.RootType.Equals(root.RootType, StringComparison.OrdinalIgnoreCase) && x.NodeType == "file"),
                directories = index.Nodes.Count(x =>
                    x.RootType.Equals(root.RootType, StringComparison.OrdinalIgnoreCase) && x.NodeType == "directory"),
            })
            .ToArray();
        return new
        {
            sourceFile = index.SourceFile,
            index.RepositoryTime,
            index.DownloadUrl,
            totalNodes = index.Nodes.Count,
            files = index.Nodes.Count(x => x.NodeType == "file"),
            directories = index.Nodes.Count(x => x.NodeType == "directory"),
            roots,
            navigation = new[]
            {
                "用 bgi_browse_repository(path='pathing') 逐层浏览路线分类。",
                "用 bgi_search_repository 的 terms/tags/author/rootType/pathPrefix 组合过滤，不要只传一个宽泛关键词。",
                "对候选项调用 bgi_get_repository_item 获取精确订阅路径和本地安装位置。",
                "确认后调用 bgi_subscribe_repository_items 安装精确节点。",
            },
        };
    }

    [McpServerTool(Name = "bgi_browse_repository", ReadOnly = true, Idempotent = true),
     Description("像目录树一样逐层浏览 repo.json。path 为空返回 pathing/js/combat/tcg；传精确目录路径返回直接子节点，支持类型过滤和分页，避免一次输出整棵树。")]
    public static object BrowseRepository(
        [Description("精确目录路径，例如 pathing/地方特产；空字符串表示仓库根。")]
        string path = "",
        [Description("可选 directory 或 file。")] string? nodeType = null,
        [Description("1 开始页码。")] int page = 1,
        [Description("每页 1-200 个直接子节点。")] int pageSize = 100)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        pageSize = Math.Clamp(pageSize, 1, 200);
        var index = McpRepositoryIndex.LoadCurrent();
        var normalized = McpRepositoryIndex.NormalizePath(path);
        if (!string.IsNullOrEmpty(normalized))
        {
            if (!index.ByPath.TryGetValue(normalized, out var parent))
                throw new ArgumentException($"仓库路径不存在：{normalized}", nameof(path));
            if (parent.NodeType != "directory")
                throw new ArgumentException($"路径不是目录：{normalized}；请改用 bgi_get_repository_item。", nameof(path));
        }

        ValidateNodeType(nodeType);
        var query = index.ChildrenOf(normalized)
            .Where(x => string.IsNullOrWhiteSpace(nodeType) ||
                        x.NodeType.Equals(nodeType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new
        {
            path = normalized,
            breadcrumb = BuildBreadcrumb(normalized),
            purpose = string.IsNullOrEmpty(normalized) ? "仓库顶层分类" : RootPurpose(normalized.Split('/', 2)[0]),
            total = query.Length,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(query.Length / (double)pageSize),
            children = query.Skip((page - 1) * pageSize).Take(pageSize).Select(ToSummary).ToArray(),
        };
    }

    [McpServerTool(Name = "bgi_search_repository", ReadOnly = true, Idempotent = true),
     Description("对大型 repo.json 做结构化多条件搜索：多词 AND/OR、根类型、路径范围、节点类型、作者、标签 all/any、更新时间和更新标记。返回分页候选及相关度，不应只用单个宽泛关键词。")]
    public static object SearchRepository(
        [Description("零到多个搜索词，匹配名称、完整路径、描述、标签和作者。")]
        string[]? terms = null,
        [Description("all=所有词命中；any=至少一个词命中。")]
        string matchMode = "all",
        [Description("可选顶层分类名，来自 bgi_get_repository_index_summary；常见为 pathing、js、combat、tcg，未来新增分类也支持。")]
        string? rootType = null,
        [Description("可选目录路径前缀，用于限定某个分类子树。")] string? pathPrefix = null,
        [Description("可选 file 或 directory。通常找可安装单项时用 file，找 JS 项目时也可能需要 directory。")]
        string? nodeType = null,
        [Description("作者名称子串过滤。")] string? author = null,
        [Description("至少命中一个的标签集合。")] string[]? tagsAny = null,
        [Description("必须全部具备的标签集合。")] string[]? tagsAll = null,
        [Description("只返回 repo_updated.json 中 hasUpdate=true 的节点。")]
        bool updatedOnly = false,
        [Description("只返回 lastUpdated 不早于该日期的节点，格式 yyyy-MM-dd。")]
        string? updatedAfter = null,
        [Description("relevance、name、updated 或 path。")]
        string sort = "relevance",
        [Description("1 开始页码。")] int page = 1,
        [Description("每页 1-100 项，默认 30。")] int pageSize = 30)
    {
        if (matchMode is not ("all" or "any"))
            throw new ArgumentException("matchMode 必须是 all 或 any。", nameof(matchMode));
        if (sort is not ("relevance" or "name" or "updated" or "path"))
            throw new ArgumentException("sort 必须是 relevance、name、updated 或 path。", nameof(sort));
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        pageSize = Math.Clamp(pageSize, 1, 100);
        ValidateRootType(rootType);
        ValidateNodeType(nodeType);
        DateTime? updatedAfterDate = null;
        if (!string.IsNullOrWhiteSpace(updatedAfter))
        {
            if (!DateTime.TryParseExact(updatedAfter, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None,
                    out var parsedDate))
                throw new ArgumentException("updatedAfter 必须是 yyyy-MM-dd。", nameof(updatedAfter));
            updatedAfterDate = parsedDate;
        }

        var normalizedTerms = NormalizeTerms(terms);
        var anyTags = NormalizeTerms(tagsAny);
        var allTags = NormalizeTerms(tagsAll);
        var normalizedPrefix = McpRepositoryIndex.NormalizePath(pathPrefix);
        var index = McpRepositoryIndex.LoadCurrent();
        var candidates = index.Nodes
            .Where(x => string.IsNullOrWhiteSpace(rootType) ||
                        x.RootType.Equals(rootType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrEmpty(normalizedPrefix) ||
                        x.Path.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase) ||
                        x.Path.StartsWith(normalizedPrefix + '/', StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(nodeType) ||
                        x.NodeType.Equals(nodeType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(author) ||
                        AuthorText(x).Contains(author, StringComparison.OrdinalIgnoreCase))
            .Where(x => anyTags.Length == 0 ||
                        anyTags.Any(tag => x.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(x => allTags.Length == 0 ||
                        allTags.All(tag => x.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .Where(x => !updatedOnly || x.HasUpdate)
            .Where(x => updatedAfterDate is null || ParseDate(x.LastUpdated) >= updatedAfterDate)
            .Select(x => new { Node = x, Score = RepositorySearchScore(x, normalizedTerms, matchMode) })
            .Where(x => normalizedTerms.Length == 0 || x.Score >= 0);

        candidates = sort switch
        {
            "name" => candidates.OrderBy(x => x.Node.Name, StringComparer.OrdinalIgnoreCase),
            "updated" => candidates.OrderByDescending(x => ParseDate(x.Node.LastUpdated)),
            "path" => candidates.OrderBy(x => x.Node.Path, StringComparer.OrdinalIgnoreCase),
            _ => candidates.OrderByDescending(x => x.Score).ThenBy(x => x.Node.Path, StringComparer.OrdinalIgnoreCase),
        };
        var materialized = candidates.ToArray();
        var items = materialized.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { relevance = x.Score, item = ToSummary(x.Node) })
            .ToArray();
        return new
        {
            total = materialized.Length,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(materialized.Length / (double)pageSize),
            filters = new
            {
                terms = normalizedTerms, matchMode, rootType, pathPrefix = normalizedPrefix, nodeType, author,
                tagsAny = anyTags, tagsAll = allTags, updatedOnly, updatedAfter, sort
            },
            items,
            nextStep = "对少量候选的精确 path 调用 bgi_get_repository_item，不要直接订阅模糊搜索结果。",
        };
    }

    [McpServerTool(Name = "bgi_get_repository_item", ReadOnly = true, Idempotent = true),
     Description("按精确仓库路径读取节点详情、面包屑、子节点预览、订阅覆盖状态、本地安装目标和是否已安装。搜索后必须用本工具确认路径再订阅。")]
    public static object GetRepositoryItem(
        [Description("bgi_browse_repository 或 bgi_search_repository 返回的精确 path。")]
        string path,
        [Description("目录节点最多预览多少个直接子项，范围 0-100。")]
        int childPreviewLimit = 20)
    {
        childPreviewLimit = Math.Clamp(childPreviewLimit, 0, 100);
        var index = McpRepositoryIndex.LoadCurrent();
        var normalized = McpRepositoryIndex.NormalizePath(path);
        if (!index.ByPath.TryGetValue(normalized, out var node))
            throw new ArgumentException($"仓库路径不存在：{normalized}", nameof(path));
        var subscriptions = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo();
        var direct = subscriptions.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        var covering = subscriptions.FirstOrDefault(x =>
            normalized.StartsWith(McpRepositoryIndex.NormalizePath(x) + '/', StringComparison.OrdinalIgnoreCase));
        var destination = ResolveInstallDestination(node.Path);
        var descendants = node.NodeType == "directory"
            ? index.Nodes.Count(x => x.Path.StartsWith(node.Path + '/', StringComparison.OrdinalIgnoreCase))
            : 0;
        return new
        {
            item = ToDetail(node),
            breadcrumb = BuildBreadcrumb(node.Path),
            purpose = RootPurpose(node.RootType),
            descendantCount = descendants,
            children = index.ChildrenOf(node.Path).Take(childPreviewLimit).Select(ToSummary).ToArray(),
            subscription = new
            {
                subscriptionPath = node.Path,
                directlySubscribed = direct,
                coveredByAncestorSubscription = covering,
                recommendedGranularity = RecommendGranularity(node, descendants),
            },
            installation = new
            {
                destination,
                exists = destination is not null && (File.Exists(destination) || Directory.Exists(destination)),
                rootMapping = ScriptRepoUpdater.PathMapper.GetValueOrDefault(node.RootType),
            },
        };
    }

    [McpServerTool(Name = "bgi_get_repository_facets", ReadOnly = true, Idempotent = true),
     Description("统计指定仓库子树中的常见标签和作者，帮助 AI 在不知道准确关键词时先缩小搜索范围。支持 rootType/pathPrefix/nodeType，返回计数最高项。")]
    public static object GetRepositoryFacets(
        [Description("可选顶层分类名，来自当前索引摘要。")] string? rootType = null,
        [Description("可选目录前缀。")] string? pathPrefix = null,
        [Description("可选 file 或 directory。")] string? nodeType = "file",
        [Description("标签、作者各返回 1-200 项。")] int limit = 50)
    {
        ValidateRootType(rootType);
        ValidateNodeType(nodeType);
        limit = Math.Clamp(limit, 1, 200);
        var prefix = McpRepositoryIndex.NormalizePath(pathPrefix);
        var nodes = McpRepositoryIndex.LoadCurrent().Nodes
            .Where(x => string.IsNullOrWhiteSpace(rootType) ||
                        x.RootType.Equals(rootType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrEmpty(prefix) || x.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                        x.Path.StartsWith(prefix + '/', StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(nodeType) ||
                        x.NodeType.Equals(nodeType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var tags = nodes.SelectMany(x => x.Tags).GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new { value = x.Key, count = x.Count() }).OrderByDescending(x => x.count).ThenBy(x => x.value)
            .Take(limit).ToArray();
        var authors = nodes.SelectMany(x => x.Authors.Select(a => a.Name).Append(x.Author ?? string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new { value = x.Key, count = x.Count() }).OrderByDescending(x => x.count).ThenBy(x => x.value)
            .Take(limit).ToArray();
        return new { matchedNodes = nodes.Length, rootType, pathPrefix = prefix, nodeType, tags, authors };
    }

    [McpServerTool(Name = "bgi_resolve_repository_paths", ReadOnly = true, Idempotent = true),
     Description("批量验证最多 100 个精确仓库路径，并返回每项类型、描述、订阅路径、本地安装位置和存在状态。用于订阅前最后检查，不做模糊匹配。")]
    public static IReadOnlyList<object> ResolveRepositoryPaths(
        [Description("精确路径数组，最多 100 个。")] string[] paths)
    {
        if (paths.Length is < 1 or > 100) throw new ArgumentException("paths 数量必须在 1-100。", nameof(paths));
        var index = McpRepositoryIndex.LoadCurrent();
        return paths.Select(path =>
        {
            var normalized = McpRepositoryIndex.NormalizePath(path);
            if (!index.ByPath.TryGetValue(normalized, out var node))
                return (object)new { path = normalized, existsInRepository = false };
            var destination = ResolveInstallDestination(node.Path);
            return new
            {
                path = node.Path,
                existsInRepository = true,
                node.NodeType,
                node.RootType,
                node.Description,
                node.Tags,
                installDestination = destination,
                installed = destination is not null && (File.Exists(destination) || Directory.Exists(destination)),
            };
        }).ToArray();
    }

    [McpServerTool(Name = "bgi_subscribe_repository_items", Destructive = true, OpenWorld = true),
     Description("订阅并可安装一组已经精确解析的仓库节点。每个 path 必须存在于当前 repo.json；拒绝模糊词。目录会订阅整个子树，调用前应先用详情检查 descendantCount。")]
    public static async Task<object> SubscribeRepositoryItems(
        [Description("一到五十个精确仓库 path。")] string[] paths,
        [Description("true 立即检出并覆盖对应 User 脚本；false 只写订阅清单。")]
        bool install = true,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("订阅仓库节点需要将 confirm 设为 true。");
        if (paths.Length is < 1 or > 50) throw new ArgumentException("paths 数量必须在 1-50。", nameof(paths));
        var index = McpRepositoryIndex.LoadCurrent();
        var normalized = paths.Select(McpRepositoryIndex.NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var path in normalized)
        {
            if (!index.ByPath.ContainsKey(path)) throw new ArgumentException($"仓库路径不存在：{path}", nameof(paths));
        }

        if (install)
        {
            var pathJson = JsonSerializer.Serialize(normalized, ConfigService.JsonOptions);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                    ScriptRepoUpdater.Instance.ImportScriptFromPathJson(pathJson)).Task.Unwrap()
                .WaitAsync(cancellationToken);
        }
        else
        {
            var merged = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo().Concat(normalized);
            await ScriptRepoUpdater.Instance.SetSubscribedPathsForCurrentRepoAsync(merged).WaitAsync(cancellationToken);
        }

        return new
        {
            subscribed = normalized, installedFilesChanged = install,
            allSubscriptions = ScriptRepoUpdater.GetSubscribedPathsForCurrentRepo()
        };
    }

    [McpServerTool(Name = "bgi_run_repository_pathing", Destructive = true, OpenWorld = true),
     Description(
         "把一个精确 pathing 仓库文件或目录作为内存 ScriptGroup 顺序运行：必要时先安装，按仓库路径排序构造 Pathing 项目，再通过 ScriptService.RunMulti 执行；不会写入 User/ScriptGroup。")]
    public async Task<object> RunRepositoryPathing(
        [Description("bgi_get_repository_item 已确认的精确 pathing 文件或目录路径。")]
        string path,
        [Description("本地不存在时是否从当前仓库安装该节点。")] bool installIfMissing = true,
        [Description("一次最多执行的路线文件数，范围 1-100；目录超过该值时拒绝，避免误跑大分类。")]
        int maxRoutes = 30,
        [Description("false（默认）在确认任务取得 BetterGI 独立任务锁后立即结束 Agent 调用；true 等待全部路线完成。")]
        bool waitForCompletion = false,
        [Description("等待任务真正启动的秒数，范围 5-600。仅影响 Agent 等待，不会取消后台任务。")]
        int startupTimeoutSeconds = 180,
        [Description("必须明确设为 true，因为会安装文件、启动游戏/截图器并操作游戏。")]
        bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (!confirm) throw new InvalidOperationException("运行仓库路线需要将 confirm 设为 true。");
        if (maxRoutes is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxRoutes));
        var index = McpRepositoryIndex.LoadCurrent();
        var normalized = McpRepositoryIndex.NormalizePath(path);
        if (!index.ByPath.TryGetValue(normalized, out var selected))
            throw new ArgumentException($"仓库路径不存在：{normalized}", nameof(path));
        if (!selected.RootType.Equals("pathing", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("该工具只运行 pathing 路线；请选择 pathing 根下的精确节点。", nameof(path));

        var routeNodes = (selected.NodeType == "file"
                ? [selected]
                : index.Nodes.Where(x => x.NodeType == "file"
                                         && x.Path.StartsWith(selected.Path + '/', StringComparison.OrdinalIgnoreCase)))
            .Where(x => x.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (routeNodes.Length == 0) throw new InvalidOperationException("该仓库节点下没有可运行的 Pathing JSON。");
        if (routeNodes.Length > maxRoutes)
            throw new InvalidOperationException(
                $"该节点包含 {routeNodes.Length} 条路线，超过 maxRoutes={maxRoutes}。请选择更窄目录或明确提高上限。");

        var destination = ResolveInstallDestination(selected.Path)
                          ?? throw new InvalidOperationException("无法解析本地安装位置。");
        var installed = selected.NodeType == "file" ? File.Exists(destination) : Directory.Exists(destination);
        if (!installed)
        {
            if (!installIfMissing)
                throw new FileNotFoundException("路线尚未安装；将 installIfMissing 设为 true，或先调用订阅安装工具。", destination);
            var pathJson = JsonSerializer.Serialize(new[] { selected.Path }, ConfigService.JsonOptions);
            await Application.Current.Dispatcher.InvokeAsync(() =>
                    ScriptRepoUpdater.Instance.ImportScriptFromPathJson(pathJson)).Task.Unwrap()
                .WaitAsync(cancellationToken);
        }

        var routeProjects = new List<ScriptGroupProject>();
        foreach (var route in routeNodes)
        {
            var relative = route.Path["pathing/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var folder = Path.GetDirectoryName(relative) ?? string.Empty;
            var localFile = Path.Combine(MapPathingViewModel.PathJsonPath, folder, route.Name);
            if (!File.Exists(localFile))
                throw new FileNotFoundException($"仓库安装后仍未找到路线文件：{route.Path}", localFile);
            routeProjects.Add(ScriptGroupProject.BuildPathingProject(route.Name, folder));
        }

        var group = new ScriptGroup { Name = $"临时路线：{selected.Name}" };
        foreach (var routeProject in routeProjects) group.AddProject(routeProject);
        var scriptService = application.Services.GetRequiredService<IScriptService>();
        var launch = await detachedTaskRegistry.LaunchAsync(
            group.Name,
            () => Application.Current.Dispatcher.InvokeAsync(() =>
                scriptService.RunMulti(group.Projects, group.Name)).Task.Unwrap(),
            waitForCompletion,
            startupTimeoutSeconds,
            cancellationToken);
        return new
        {
            launch.Accepted,
            launch.Running,
            launch.Completed,
            detachedTaskId = launch.Id,
            launch.Message,
            repositoryPath = selected.Path,
            installedByThisCall = !installed,
            routeCount = routeNodes.Length,
            routes = routeNodes.Select(x => x.Name).ToArray(),
            scriptGroup = group.Name,
            persistentScriptGroupCreated = false,
            schedulerProjectCount = group.Projects.Count,
        };
    }

    private static object ToSummary(McpRepositoryNode node) => new
    {
        node.Path,
        node.Name,
        node.NodeType,
        node.RootType,
        node.ChildCount,
        node.Version,
        node.Author,
        node.Description,
        node.Tags,
        node.LastUpdated,
        node.HasUpdate,
    };

    private static object ToDetail(McpRepositoryNode node) => new
    {
        node.Path,
        node.ParentPath,
        node.Name,
        node.NodeType,
        node.RootType,
        node.Depth,
        node.ChildCount,
        node.Version,
        node.Author,
        node.Authors,
        node.Description,
        node.Tags,
        node.LastUpdated,
        node.HasUpdate,
    };

    private static int RepositorySearchScore(McpRepositoryNode node, IReadOnlyList<string> terms, string matchMode)
    {
        if (terms.Count == 0) return 0;
        var authorText = AuthorText(node);
        var tagText = string.Join(' ', node.Tags);
        var description = node.Description ?? string.Empty;
        var matches = terms.Select(term =>
            node.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || node.Path.Contains(term, StringComparison.OrdinalIgnoreCase)
            || description.Contains(term, StringComparison.OrdinalIgnoreCase)
            || tagText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || authorText.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchMode == "all" ? matches.Any(x => !x) : matches.All(x => !x)) return -1;
        var score = 0;
        foreach (var term in terms)
        {
            if (node.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 100;
            else if (node.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 60;
            else if (node.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 45;
            if (node.Tags.Any(x => x.Equals(term, StringComparison.OrdinalIgnoreCase))) score += 35;
            else if (tagText.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 15;
            if (node.Path.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 20;
            if (description.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 12;
            if (authorText.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
        }

        return score;
    }

    private static string[] NormalizeTerms(IEnumerable<string>? values) => (values ?? [])
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string AuthorText(McpRepositoryNode node) =>
        string.Join(' ', node.Authors.Select(x => x.Name).Append(node.Author ?? string.Empty));

    private static DateTime ParseDate(string? value) =>
        DateTime.TryParse(value, out var date) ? date : DateTime.MinValue;

    private static IReadOnlyList<string> BuildBreadcrumb(string path)
    {
        var parts = McpRepositoryIndex.NormalizePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Select((_, index) => string.Join('/', parts.Take(index + 1))).ToArray();
    }

    private static string? ResolveInstallDestination(string repositoryPath)
    {
        var parts = McpRepositoryIndex.NormalizePath(repositoryPath).Split('/', 2);
        if (parts.Length == 0 || !ScriptRepoUpdater.PathMapper.TryGetValue(parts[0], out var root)) return null;
        return parts.Length == 1 ? root : Path.Combine(root, parts[1].Replace('/', Path.DirectorySeparatorChar));
    }

    private static string RecommendGranularity(McpRepositoryNode node, int descendants) =>
        node.NodeType == "file"
            ? "这是单文件，可精确订阅。"
            : descendants switch
            {
                0 => "空目录，不建议订阅。",
                > 500 => "该目录包含大量后代；应继续浏览并选择更窄子目录，除非明确需要整个分类。",
                > 50 => "该目录较大；建议检查子目录后再决定是否整体订阅。",
                _ => "可整体订阅此目录，也可继续浏览选择更细节点。",
            };

    private static string RootPurpose(string rootType) => rootType.ToLowerInvariant() switch
    {
        "pathing" => "地图追踪路线 JSON；订阅后安装到 User/AutoPathing，可加入调度配置组作为 Pathing 项目。",
        "js" => "BetterGI JavaScript 项目；通常应订阅脚本目录，安装到 User/JsScript，再读取 manifest/settings_ui。",
        "combat" => "自动战斗策略文本；安装到 User/AutoFight，供自动战斗、秘境和 Boss 使用。",
        "tcg" => "七圣召唤策略文本/目录；安装到 User/AutoGeniusInvokation。",
        _ => "未知仓库根类型。",
    };

    private static void ValidateRootType(string? rootType)
    {
        if (string.IsNullOrWhiteSpace(rootType)) return;
        var roots = McpRepositoryIndex.LoadCurrent().Nodes.Where(x => x.Depth == 0).Select(x => x.Path).ToArray();
        if (!roots.Contains(rootType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"rootType 不存在。当前可用顶层分类：{string.Join("、", roots)}", nameof(rootType));
    }

    private static void ValidateNodeType(string? nodeType)
    {
        if (!string.IsNullOrWhiteSpace(nodeType) && nodeType is not ("file" or "directory"))
            throw new ArgumentException("nodeType 必须是 file 或 directory。", nameof(nodeType));
    }
}