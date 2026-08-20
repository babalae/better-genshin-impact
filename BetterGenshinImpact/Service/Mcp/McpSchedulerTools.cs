using System.ComponentModel;
using System.Dynamic;
using System.Text.Json;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 面向 AI 的显式调度器和 JS 脚本工具。这里不依赖 UI 对话框，参数可以完整地由 JSON Schema 表达。
/// </summary>
[McpServerToolType]
public sealed class McpSchedulerTools(
    McpApplicationServices application,
    McpDetachedTaskRegistry detachedTaskRegistry)
{
    private static readonly SemaphoreSlim GroupWriteLock = new(1, 1);
    private static string GroupFolder => Global.Absolute(@"User\ScriptGroup");

    [McpServerTool(Name = "bgi_get_capabilities", ReadOnly = true, Idempotent = true),
     Description("返回 BetterGI MCP 的业务能力地图、推荐调用顺序和关键概念。AI 在首次控制 BetterGI 或不确定工具选择时应先调用本工具。")]
    public static object GetCapabilities() => new
    {
        concepts = new
        {
            javascriptScript =
                "User/JsScript 下包含 manifest.json 的脚本项目；folderName 是稳定唯一标识。脚本可声明 settings_ui，运行时通过全局 settings 对象读取值。",
            scriptGroup =
                "调度器持久化配置，位于 User/ScriptGroup/*.json；组内项目按 1 开始的 index 排序，可包含 Javascript、Pathing、KeyMouse、Shell。",
            scriptProject = "配置组中的一次脚本引用；同一个 JS folderName 可在不同组或同组多次出现，并拥有各自 JsScriptSettingsObject。",
            taskRuntime = "所有执行工具复用 BetterGI 的截图器、TaskRunner、CancellationContext 和单任务互斥约束。",
            settingCatalog =
                "AllConfig 下约 635 个设置叶子项被划分为 36 个业务分区。设置搜索支持多词、分区、路径、类型和分页；descriptionSource 表明说明来自源码 XML 还是结构推断。",
            repositoryIndex =
                "脚本仓库 repo.json 是大型树形索引。必须先看摘要/分面/目录树，再组合 terms、tags、author、rootType、pathPrefix 搜索，最后精确解析 path 后订阅。",
        },
        recommendedFlows = new
        {
            runConfiguredJs = new[]
            {
                "bgi_list_script_groups", "bgi_get_script_group", "bgi_get_js_script_settings", "bgi_run_script_project"
            },
            configureJs = new[]
                { "bgi_list_javascript_scripts", "bgi_get_javascript_script", "bgi_set_js_script_settings" },
            runAdHocJs = new[] { "bgi_get_javascript_script", "bgi_run_javascript_script" },
            editScheduler = new[]
            {
                "bgi_get_script_group",
                "bgi_add_script_group_project / bgi_update_script_group_project / bgi_reorder_script_group_project",
                "bgi_run_script_groups"
            },
            installScripts = new[]
            {
                "bgi_get_script_repository", "bgi_update_script_repository", "bgi_subscribe_scripts",
                "bgi_list_javascript_scripts"
            },
            findRepositoryRoute = new[]
            {
                "bgi_get_repository_index_summary", "bgi_get_repository_facets 或 bgi_browse_repository",
                "bgi_search_repository", "bgi_get_repository_item", "bgi_subscribe_repository_items"
            },
            understandSettings = new[]
            {
                "bgi_list_setting_sections", "bgi_search_settings", "bgi_get_setting_details",
                "bgi_update_settings(dryRun=true)", "bgi_update_settings(dryRun=false, confirm=true)"
            },
            stopExecution = new[]
            {
                "bgi_get_execution_status", "bgi_stop_current_task_and_wait 或 bgi_interrupt_current_script",
                "bgi_release_all_simulated_keys（仅紧急情况）"
            },
        },
        cautions = new[]
        {
            "先用读取工具获得 folderName、groupName、projectIndex 和 settingsSchema，不要猜名称。",
            "JS 设置属于配置组项目，不是脚本目录的全局设置；同一脚本的不同项目可以有不同设置。",
            "允许 JS HTTP 会授予 manifest.json 中 http_allowed_urls 的网络访问权，必须显式确认。",
            "Shell 项目会执行本机命令，新增或直接执行都必须显式确认。",
            "长任务取消请调用 bgi_cancel_current_task(confirm: true)。",
            "取消 MCP HTTP 请求不会自动中断 BetterGI；需要停止时使用 bgi_stop_current_task_and_wait。",
            "不要把完整 repo.json 或完整 config.json 塞进上下文；使用分页搜索和精确详情工具。",
        },
    };

    [McpServerTool(Name = "bgi_list_javascript_scripts", ReadOnly = true, Idempotent = true),
     Description(
         "列出 User/JsScript 中已安装且 manifest 有效的全部 JS 脚本。返回 folderName、名称、版本、说明、入口文件、设置文件和允许联网域；folderName 用于后续工具。")]
    public static IReadOnlyList<object> ListJavaScriptScripts(
        [Description("可选过滤词，匹配 folderName、脚本名称或说明。")]
        string? filter = null)
    {
        Directory.CreateDirectory(Global.ScriptPath());
        var result = new List<object>();
        foreach (var directory in Directory.GetDirectories(Global.ScriptPath()))
        {
            var folderName = Path.GetFileName(directory);
            try
            {
                var project = new ScriptProject(folderName);
                if (!string.IsNullOrWhiteSpace(filter)
                    && !folderName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !project.Manifest.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !project.Manifest.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(ToJavaScriptSummary(project));
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(filter) ||
                    folderName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new { folderName, valid = false, error = ex.Message });
                }
            }
        }

        return result;
    }

    [McpServerTool(Name = "bgi_get_javascript_script", ReadOnly = true, Idempotent = true),
     Description("读取一个已安装 JS 脚本的完整 manifest、可设置字段定义，以及它在所有调度配置组中的引用和当前设置。调用运行或改设置工具前应先调用本工具。")]
    public static object GetJavaScriptScript(
        [Description("bgi_list_javascript_scripts 返回的脚本目录名，不是 manifest 中的显示名称。")]
        string folderName)
    {
        var project = LoadJavaScriptProject(folderName);
        var usages = LoadAllGroups()
            .SelectMany(x => x.Group.Projects
                .Where(p => p.Type == "Javascript" &&
                            p.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                .Select(p => new
                {
                    groupName = x.Group.Name,
                    projectIndex = p.Index,
                    p.Status,
                    p.Schedule,
                    p.RunNum,
                    p.AllowJsNotification,
                    allowHttp = p.AllowJsHTTP,
                    settings = p.JsScriptSettingsObject,
                }))
            .ToArray();
        return new
        {
            folderName = project.FolderName,
            project.Manifest,
            settingsSchema = project.Manifest.LoadSettingItems(project.ProjectPath),
            groupUsages = usages,
        };
    }

    [McpServerTool(Name = "bgi_list_available_scripts", ReadOnly = true, Idempotent = true),
     Description("列出可加入调度器的脚本资源。支持 Javascript、Pathing、KeyMouse；结果包含创建项目所需的 name 和 folderName。")]
    public static IReadOnlyList<object> ListAvailableScripts(
        [Description("Javascript、Pathing 或 KeyMouse；省略时返回三类。")]
        string? type = null,
        [Description("可选名称/路径过滤词。")] string? filter = null,
        [Description("最大返回条数，范围 1-2000，默认 500。")]
        int limit = 500)
    {
        limit = Math.Clamp(limit, 1, 2000);
        var allowed = new[] { "Javascript", "Pathing", "KeyMouse" };
        if (!string.IsNullOrWhiteSpace(type) && !allowed.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("type 必须是 Javascript、Pathing 或 KeyMouse。", nameof(type));
        }

        bool Wants(string value) =>
            string.IsNullOrWhiteSpace(type) || value.Equals(type, StringComparison.OrdinalIgnoreCase);

        bool Matches(string value) => string.IsNullOrWhiteSpace(filter) ||
                                      value.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var rows = new List<object>();
        if (Wants("Javascript"))
        {
            foreach (var directory in Directory.Exists(Global.ScriptPath())
                         ? Directory.GetDirectories(Global.ScriptPath())
                         : [])
            {
                try
                {
                    var project = new ScriptProject(Path.GetFileName(directory));
                    if (Matches(project.FolderName) || Matches(project.Manifest.Name))
                    {
                        rows.Add(new
                            { type = "Javascript", name = project.Manifest.Name, folderName = project.FolderName });
                    }
                }
                catch
                {
                    // 无效 JS 目录由 bgi_list_javascript_scripts 报告，这里只返回可加入项目。
                }
            }
        }

        if (Wants("Pathing"))
        {
            var root = MapPathingViewModel.PathJsonPath;
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    var relativeFolder = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                    var relative = Path.GetRelativePath(root, file);
                    if (Matches(relative))
                    {
                        rows.Add(new
                        {
                            type = "Pathing", name = Path.GetFileName(file),
                            folderName = relativeFolder == "." ? string.Empty : relativeFolder
                        });
                    }
                }
            }
        }

        if (Wants("KeyMouse"))
        {
            var root = Global.Absolute(@"User\KeyMouseScript");
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(root, file);
                    if (Matches(relative))
                    {
                        rows.Add(new { type = "KeyMouse", name = relative, folderName = relative });
                    }
                }
            }
        }

        return rows.Take(limit).ToArray();
    }

    [McpServerTool(Name = "bgi_get_script_group", ReadOnly = true, Idempotent = true),
     Description("读取一个调度配置组的完整定义，包括组级 Pathing/Shell 配置以及每个项目的 index、类型、启用状态、周期、执行次数、JS 设置和权限。")]
    public static object GetScriptGroup(
        [Description("配置组显示名称，来自 bgi_list_script_groups。")]
        string groupName)
    {
        var entry = LoadGroup(groupName);
        return new { file = Path.GetFileName(entry.File), entry.Group };
    }

    [McpServerTool(Name = "bgi_create_script_group", Destructive = true),
     Description(
         "创建一个新的空调度配置组。可选 groupConfig 是 ScriptGroupConfig JSON，包含 pathingConfig、shellConfig、enableShellConfig；省略则使用默认值。")]
    public async Task<object> CreateScriptGroup(
        [Description("新配置组名称，也会用作 JSON 文件名；不能包含文件名非法字符。")]
        string groupName,
        [Description("可选的完整 ScriptGroupConfig JSON。")]
        JsonElement? groupConfig = null,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "创建配置组");
        ValidateGroupName(groupName);
        return await MutateAsync(() =>
        {
            if (TryLoadGroup(groupName, out _))
            {
                throw new InvalidOperationException($"配置组已存在：{groupName}");
            }

            var group = new ScriptGroup { Name = groupName, Index = NextGroupIndex() };
            if (groupConfig is not null)
            {
                group.Config =
                    System.Text.Json.JsonSerializer.Deserialize<ScriptGroupConfig>(groupConfig.Value.GetRawText(),
                        ConfigService.JsonOptions)
                    ?? throw new ArgumentException("groupConfig 无法解析为 ScriptGroupConfig。", nameof(groupConfig));
            }

            var file = Path.Combine(GroupFolder, $"{groupName}.json");
            if (File.Exists(file))
            {
                throw new InvalidOperationException($"目标配置文件已经存在但无法作为同名配置组读取：{Path.GetFileName(file)}");
            }

            SaveGroup(file, group);
            return (object)new { created = true, groupName, file = Path.GetFileName(file) };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_rename_script_group", Destructive = true), Description("重命名调度配置组及其 JSON 文件，不修改组内项目。")]
    public async Task<object> RenameScriptGroup(
        [Description("现有配置组名称。")] string groupName,
        [Description("新配置组名称。")] string newName,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "重命名配置组");
        ValidateGroupName(newName);
        return await MutateAsync(() =>
        {
            var entry = LoadGroup(groupName);
            if (TryLoadGroup(newName, out _))
            {
                throw new InvalidOperationException($"目标配置组已存在：{newName}");
            }

            var target = Path.Combine(GroupFolder, $"{newName}.json");
            entry.Group.Name = newName;
            SaveGroup(target, entry.Group);
            if (!Path.GetFullPath(entry.File).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(entry.File);
            }

            return (object)new
                { renamed = true, previousName = groupName, groupName = newName, file = Path.GetFileName(target) };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_delete_script_group", Destructive = true),
     Description("永久删除一个调度配置组 JSON；不会删除该组引用的 JS、地图追踪或键鼠脚本文件。")]
    public async Task<object> DeleteScriptGroup(
        [Description("要删除的配置组名称。")] string groupName,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "删除配置组");
        return await MutateAsync(() =>
        {
            var entry = LoadGroup(groupName);
            File.Delete(entry.File);
            return (object)new { deleted = true, groupName, retainedScriptFiles = true };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_set_script_group_config", Destructive = true),
     Description(
         "完整替换一个调度配置组的组级配置。JSON 类型为 ScriptGroupConfig，包含 pathingConfig、shellConfig 和 enableShellConfig；不修改 projects。")]
    public async Task<object> SetScriptGroupConfig(
        [Description("配置组名称。")] string groupName,
        [Description("完整 ScriptGroupConfig JSON。先调用 bgi_get_script_group 读取现值后再修改。")]
        JsonElement groupConfig,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "修改配置组设置");
        var parsed =
            System.Text.Json.JsonSerializer.Deserialize<ScriptGroupConfig>(groupConfig.GetRawText(),
                ConfigService.JsonOptions)
            ?? throw new ArgumentException("groupConfig 无法解析为 ScriptGroupConfig。", nameof(groupConfig));
        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            group.Config = parsed;
            SaveGroup(entry.File, group);
            return new { saved = true, groupName, group.Config };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_add_script_group_project", Destructive = true),
     Description(
         "向调度配置组末尾添加项目。Javascript 的 folderName 来自 bgi_list_javascript_scripts；Pathing/KeyMouse 来自 bgi_list_available_scripts；Shell 的 name 是本机命令。")]
    public async Task<object> AddScriptGroupProject(
        [Description("目标配置组名称。")] string groupName,
        [Description("Javascript、Pathing、KeyMouse 或 Shell。")]
        string type,
        [Description("项目显示名或文件名。Javascript 会以 manifest.name 为准；Shell 中此值是要执行的命令。")]
        string name,
        [Description("Javascript 为脚本目录名；Pathing 为相对目录；KeyMouse 可与 name 相同；Shell 可留空。")]
        string folderName = "",
        [Description("Enabled 或 Disabled。")] string status = "Enabled",
        [Description("Daily/EveryTwoDays/星期英文名或自定义 Cron 表达式。")]
        string schedule = "Daily",
        [Description("每次调度执行次数，1-999。")] int runNum = 1,
        [Description("Javascript 自定义设置对象；字段定义来自 bgi_get_javascript_script.settingsSchema。")]
        JsonElement? settings = null,
        [Description("是否允许 JS 使用 BetterGI 通知接口。")]
        bool allowNotification = true,
        [Description("是否授权 JS 请求 manifest.httpAllowedUrls。")]
        bool allowHttp = false,
        [Description("allowHttp=true 时必须明确设为 true。")]
        bool confirmHttpAccess = false,
        [Description("type=Shell 时必须明确设为 true。")]
        bool confirmShellCommand = false,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "添加调度项目");
        ValidateProjectCommon(type, status, runNum);
        if (type.Equals("Shell", StringComparison.OrdinalIgnoreCase) && !confirmShellCommand)
        {
            throw new InvalidOperationException("添加 Shell 项目需要将 confirmShellCommand 设为 true。");
        }

        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            var project = CreateProject(type, name, folderName);
            project.Status = NormalizeType(status, ["Enabled", "Disabled"], nameof(status));
            project.Schedule = schedule;
            project.RunNum = runNum;
            project.AllowJsNotification = allowNotification;
            if (project.Type == "Javascript")
            {
                ApplySettings(project, settings, replace: true);
                ApplyHttpPermission(project, allowHttp, confirmHttpAccess);
            }

            group.AddProject(project);
            NormalizeProjectIndexes(group);
            SaveGroup(entry.File, group);
            return new { added = true, groupName, projectIndex = project.Index, project };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_update_script_group_project", Destructive = true),
     Description("修改一个调度项目的通用属性和 JS 权限/设置。projectIndex 是 bgi_get_script_group 返回的 1 开始索引；省略的字段保持不变。")]
    public async Task<object> UpdateScriptGroupProject(
        [Description("配置组名称。")] string groupName,
        [Description("项目的 1 开始 index。")] int projectIndex,
        [Description("可选 Enabled 或 Disabled。")]
        string? status = null,
        [Description("可选执行周期或 Cron。")] string? schedule = null,
        [Description("可选执行次数 1-999。")] int? runNum = null,
        [Description("可选 JS 设置对象。默认与旧设置合并。")] JsonElement? settings = null,
        [Description("true 时用 settings 完整替换旧 JS 设置；false 时合并。")]
        bool replaceSettings = false,
        [Description("可选 JS 通知权限。")] bool? allowNotification = null,
        [Description("可选 JS HTTP 权限。")] bool? allowHttp = null,
        [Description("把 allowHttp 设为 true 时必须明确设为 true。")]
        bool confirmHttpAccess = false,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "修改调度项目");
        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            var project = FindProject(group, projectIndex);
            if (status is not null) project.Status = NormalizeType(status, ["Enabled", "Disabled"], nameof(status));
            if (schedule is not null) project.Schedule = schedule;
            if (runNum is not null)
            {
                if (runNum is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(runNum), "runNum 必须在 1-999。 ");
                project.RunNum = runNum.Value;
            }

            if (allowNotification is not null) project.AllowJsNotification = allowNotification;
            if (settings is not null)
            {
                EnsureJavaScript(project);
                ApplySettings(project, settings, replaceSettings);
            }

            if (allowHttp is not null)
            {
                EnsureJavaScript(project);
                ApplyHttpPermission(project, allowHttp.Value, confirmHttpAccess);
            }

            SaveGroup(entry.File, group);
            return new { saved = true, groupName, projectIndex, project };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_remove_script_group_project", Destructive = true),
     Description("从配置组移除一个项目并重新编号；不会删除实际脚本文件。")]
    public async Task<object> RemoveScriptGroupProject(
        [Description("配置组名称。")] string groupName,
        [Description("项目的 1 开始 index。")] int projectIndex,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "移除调度项目");
        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            var project = FindProject(group, projectIndex);
            group.Projects.Remove(project);
            NormalizeProjectIndexes(group);
            SaveGroup(entry.File, group);
            return new { removed = true, groupName, projectIndex, retainedScriptFiles = true };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_reorder_script_group_project", Destructive = true),
     Description("把配置组中的项目从一个 1 开始索引移动到另一个位置，并持久化新的执行顺序。")]
    public async Task<object> ReorderScriptGroupProject(
        [Description("配置组名称。")] string groupName,
        [Description("当前 1 开始索引。")] int fromIndex,
        [Description("目标 1 开始索引。")] int toIndex,
        [Description("必须明确设为 true。")] bool confirm,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "调整调度顺序");
        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            if (fromIndex < 1 || fromIndex > group.Projects.Count || toIndex < 1 || toIndex > group.Projects.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex), "fromIndex 和 toIndex 必须位于现有项目范围内。");
            }

            group.Projects.Move(fromIndex - 1, toIndex - 1);
            NormalizeProjectIndexes(group);
            SaveGroup(entry.File, group);
            return new { moved = true, groupName, fromIndex, toIndex };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_get_js_script_settings", ReadOnly = true, Idempotent = true),
     Description("读取某个配置组项目的 JS 设置值和脚本声明的设置 Schema。设置属于这个项目实例，而不是 JS 脚本的全局值。")]
    public static object GetJsScriptSettings(
        [Description("配置组名称。")] string groupName,
        [Description("Javascript 项目的 1 开始 index。")]
        int projectIndex)
    {
        var group = LoadGroup(groupName).Group;
        var item = FindProject(group, projectIndex);
        EnsureJavaScript(item);
        item.BuildScriptProjectRelation();
        return new
        {
            groupName,
            projectIndex,
            item.Name,
            item.FolderName,
            settingsSchema = item.Project!.Manifest.LoadSettingItems(item.Project.ProjectPath),
            settings = item.JsScriptSettingsObject ?? new ExpandoObject(),
        };
    }

    [McpServerTool(Name = "bgi_set_js_script_settings", Destructive = true),
     Description("修改配置组中一个 JS 项目的 settings 对象。字段名、类型和选项会按脚本 settings_ui 定义校验，并保存回配置组 JSON。")]
    public async Task<object> SetJsScriptSettings(
        [Description("配置组名称。")] string groupName,
        [Description("Javascript 项目的 1 开始 index。")]
        int projectIndex,
        [Description("设置 JSON 对象；字段定义来自 bgi_get_js_script_settings.settingsSchema。")]
        JsonElement settings,
        [Description("true 为完整替换；false 为与现值合并。")]
        bool replace = false,
        [Description("必须明确设为 true。")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        RequireConfirmation(confirm, "修改 JS 设置");
        return await MutateGroupAsync(groupName, (entry, group) =>
        {
            var project = FindProject(group, projectIndex);
            EnsureJavaScript(project);
            ApplySettings(project, settings, replace);
            SaveGroup(entry.File, group);
            return new { saved = true, groupName, projectIndex, settings = project.JsScriptSettingsObject };
        }, cancellationToken);
    }

    [McpServerTool(Name = "bgi_run_javascript_script", OpenWorld = true),
     Description("不创建配置组，直接运行一个已安装 JS 脚本。可传临时 settings；默认禁止脚本通知和 HTTP。执行仍使用 BetterGI 调度器运行环境、截图器和单任务锁。")]
    public async Task<object> RunJavaScriptScript(
        [Description("JS 脚本 folderName，来自 bgi_list_javascript_scripts。")]
        string folderName,
        [Description("本次运行的临时设置对象；字段按 settingsSchema 校验。")]
        JsonElement? settings = null,
        [Description("是否允许脚本调用 BetterGI 通知接口。")]
        bool allowNotification = false,
        [Description("是否允许脚本访问 manifest.httpAllowedUrls。")]
        bool allowHttp = false,
        [Description("allowHttp=true 时必须明确设为 true。")]
        bool confirmHttpAccess = false,
        CancellationToken cancellationToken = default)
    {
        var script = LoadJavaScriptProject(folderName);
        var group = new ScriptGroup { Name = $"MCP 临时 JS：{script.Manifest.Name}" };
        var project = new ScriptGroupProject(script)
        {
            AllowJsNotification = allowNotification,
        };
        ApplySettings(project, settings, replace: true);
        ApplyHttpPermission(project, allowHttp, confirmHttpAccess);
        group.AddProject(project);
        var scriptService = application.Services.GetRequiredService<IScriptService>();
        return await detachedTaskRegistry.LaunchAsync(
            $"JS：{script.Manifest.Name}",
            () => Application.Current.Dispatcher.InvokeAsync(() =>
                scriptService.RunMulti([project], group.Name)).Task.Unwrap(),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);
    }

    [McpServerTool(Name = "bgi_run_script_project", OpenWorld = true),
     Description("直接执行配置组中的一个指定项目，使用该项目保存的 JS settings、HTTP/通知权限和组级 Pathing/Shell 配置。默认拒绝 Disabled 项目和 Shell 项目。")]
    public async Task<object> RunScriptProject(
        [Description("配置组名称。")] string groupName,
        [Description("项目的 1 开始 index。")] int projectIndex,
        [Description("项目为 Disabled 时是否仍强制执行。")]
        bool forceDisabled = false,
        [Description("项目为 Shell 时必须明确设为 true。")]
        bool confirmShellCommand = false,
        CancellationToken cancellationToken = default)
    {
        var group = LoadGroup(groupName).Group;
        var project = FindProject(group, projectIndex);
        if (project.Status == "Disabled" && !forceDisabled)
        {
            throw new InvalidOperationException("项目当前为 Disabled；如确需运行请将 forceDisabled 设为 true。");
        }

        if (project.Type == "Shell" && !confirmShellCommand)
        {
            throw new InvalidOperationException("执行 Shell 项目需要将 confirmShellCommand 设为 true。");
        }

        var scriptService = application.Services.GetRequiredService<IScriptService>();
        return await detachedTaskRegistry.LaunchAsync(
            $"{groupName}/{projectIndex}:{project.Name}",
            () => Application.Current.Dispatcher.InvokeAsync(() =>
                scriptService.RunMulti([project], group.Name)).Task.Unwrap(),
            waitForCompletion: false,
            startupTimeoutSeconds: 180,
            cancellationToken);
    }

    private async Task<object> MutateGroupAsync(
        string groupName,
        Func<GroupEntry, ScriptGroup, object> mutation,
        CancellationToken cancellationToken) =>
        await MutateAsync(() =>
        {
            var entry = LoadGroup(groupName);
            return mutation(entry, entry.Group);
        }, cancellationToken);

    private async Task<object> MutateAsync(Func<object> mutation, CancellationToken cancellationToken)
    {
        await GroupWriteLock.WaitAsync(cancellationToken);
        try
        {
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return mutation();
            }).Task;
            var viewModel = application.Services.GetService<ScriptControlViewModel>();
            if (viewModel is not null)
            {
                await Application.Current.Dispatcher.InvokeAsync(viewModel.OnNavigatedTo).Task;
            }

            return result;
        }
        finally
        {
            GroupWriteLock.Release();
        }
    }

    private static IReadOnlyList<GroupEntry> LoadAllGroups()
    {
        if (!Directory.Exists(GroupFolder)) return [];
        var rows = new List<GroupEntry>();
        foreach (var file in Directory.GetFiles(GroupFolder, "*.json"))
        {
            try
            {
                rows.Add(new GroupEntry(file, ScriptGroup.FromJson(File.ReadAllText(file))));
            }
            catch
            {
                // 列举脚本引用时忽略损坏组；bgi_list_script_groups 会单独报告损坏文件。
            }
        }

        return rows;
    }

    private static GroupEntry LoadGroup(string groupName)
    {
        if (TryLoadGroup(groupName, out var entry)) return entry!;
        throw new ArgumentException($"未找到配置组：{groupName}", nameof(groupName));
    }

    private static bool TryLoadGroup(string groupName, out GroupEntry? entry)
    {
        entry = LoadAllGroups().FirstOrDefault(x => x.Group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        return entry is not null;
    }

    private static void SaveGroup(string file, ScriptGroup group)
    {
        Directory.CreateDirectory(GroupFolder);
        NormalizeProjectIndexes(group);
        ScriptGroup.ResetGroupInfo(group);
        var tempFile = file + ".mcp.tmp";
        try
        {
            File.WriteAllText(tempFile, group.ToJson());
            File.Move(tempFile, file, true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static ScriptGroupProject CreateProject(string type, string name, string folderName)
    {
        var normalized = NormalizeType(type, ["Javascript", "Pathing", "KeyMouse", "Shell"], nameof(type));
        return normalized switch
        {
            "Javascript" => new ScriptGroupProject(LoadJavaScriptProject(folderName)),
            "Pathing" when !string.IsNullOrWhiteSpace(name) => CreateRelativeFileProject(name, folderName, "Pathing"),
            "KeyMouse" when !string.IsNullOrWhiteSpace(name) => CreateRelativeFileProject(name, folderName, "KeyMouse"),
            "Shell" when !string.IsNullOrWhiteSpace(name) => ScriptGroupProject.BuildShellProject(name),
            _ => throw new ArgumentException($"{normalized} 项目的 name 不能为空。", nameof(name)),
        };
    }

    private static ScriptGroupProject CreateRelativeFileProject(string name, string folderName, string type)
    {
        static bool IsUnsafe(string value) =>
            Path.IsPathRooted(value)
            || value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or "..");

        if (IsUnsafe(name) || IsUnsafe(folderName))
        {
            throw new ArgumentException($"{type} 的 name/folderName 必须是 User 目录内的安全相对路径。");
        }

        return type == "Pathing"
            ? ScriptGroupProject.BuildPathingProject(name, folderName)
            : ScriptGroupProject.BuildKeyMouseProject(name);
    }

    private static ScriptProject LoadJavaScriptProject(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)
            || Path.IsPathRooted(folderName)
            || folderName.Contains('/')
            || folderName.Contains('\\')
            || folderName is "." or "..")
        {
            throw new ArgumentException("folderName 必须是 User/JsScript 下的单层目录名。", nameof(folderName));
        }

        return new ScriptProject(folderName);
    }

    private static ScriptGroupProject FindProject(ScriptGroup group, int projectIndex)
    {
        var project = group.Projects.FirstOrDefault(x => x.Index == projectIndex);
        if (project is null)
        {
            throw new ArgumentException($"配置组“{group.Name}”中不存在 index={projectIndex} 的项目。", nameof(projectIndex));
        }

        return project;
    }

    private static void ApplySettings(ScriptGroupProject project, JsonElement? settings, bool replace)
    {
        EnsureJavaScript(project);
        project.BuildScriptProjectRelation();
        var schema = project.Project!.Manifest.LoadSettingItems(project.Project.ProjectPath)
            .Where(x => x.Type != "separator" && !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        JObject incoming = settings is null || settings.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new JObject()
            : JObject.Parse(settings.Value.GetRawText());
        foreach (var property in incoming.Properties())
        {
            if (!schema.TryGetValue(property.Name, out var item))
            {
                throw new ArgumentException($"脚本未声明设置字段：{property.Name}", nameof(settings));
            }

            ValidateSettingValue(item, property.Value);
        }

        JObject merged;
        if (replace || project.JsScriptSettingsObject is null)
        {
            merged = incoming;
        }
        else
        {
            merged = JObject.FromObject(project.JsScriptSettingsObject);
            merged.Merge(incoming, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
        }

        project.JsScriptSettingsObject = merged.ToObject<ExpandoObject>() ?? new ExpandoObject();
    }

    private static void ValidateSettingValue(SettingItem item, JToken value)
    {
        if (value.Type == JTokenType.Null) return;
        switch (item.Type)
        {
            case "checkbox" when value.Type != JTokenType.Boolean:
                throw new ArgumentException($"设置 {item.Name} 必须是 boolean。");
            case "input-text" or "select" or "cascade-select" when value.Type != JTokenType.String:
                throw new ArgumentException($"设置 {item.Name} 必须是 string。");
            case "multi-checkbox":
                if (value is not JArray array || array.Any(x => x.Type != JTokenType.String))
                    throw new ArgumentException($"设置 {item.Name} 必须是 string 数组。");
                if (item.Options is not null &&
                    array.Values<string>().Any(x => x is not null && !item.Options.Contains(x)))
                    throw new ArgumentException($"设置 {item.Name} 包含未声明选项。");
                break;
        }

        if (item.Type == "select" && item.Options is not null && !item.Options.Contains(value.Value<string>()!))
        {
            throw new ArgumentException($"设置 {item.Name} 必须是以下选项之一：{string.Join("、", item.Options)}");
        }

        if (item.Type == "cascade-select" && item.CascadeOptions is not null)
        {
            var allowed = item.CascadeOptions.Keys.Concat(item.CascadeOptions.Values.SelectMany(x => x)).ToHashSet();
            if (!allowed.Contains(value.Value<string>()!))
                throw new ArgumentException($"设置 {item.Name} 不是 cascadeOptions 中的有效值。");
        }
    }

    private static void ApplyHttpPermission(ScriptGroupProject project, bool allowHttp, bool confirmHttpAccess)
    {
        if (allowHttp && !confirmHttpAccess)
        {
            throw new InvalidOperationException("允许 JS HTTP 访问需要将 confirmHttpAccess 设为 true。");
        }

        project.BuildScriptProjectRelation();
        project.AllowJsHTTPHash = allowHttp ? project.GetHttpAllowedUrlsHash() : string.Empty;
    }

    private static void EnsureJavaScript(ScriptGroupProject project)
    {
        if (project.Type != "Javascript")
        {
            throw new InvalidOperationException($"项目 {project.Index} 的类型是 {project.Type}，不是 Javascript。");
        }
    }

    private static void NormalizeProjectIndexes(ScriptGroup group)
    {
        for (var index = 0; index < group.Projects.Count; index++)
        {
            group.Projects[index].Index = index + 1;
        }
    }

    private static int NextGroupIndex()
    {
        var groups = LoadAllGroups();
        return groups.Count == 0 ? 1 : groups.Max(x => x.Group.Index) + 1;
    }

    private static void ValidateProjectCommon(string type, string status, int runNum)
    {
        _ = NormalizeType(type, ["Javascript", "Pathing", "KeyMouse", "Shell"], nameof(type));
        _ = NormalizeType(status, ["Enabled", "Disabled"], nameof(status));
        if (runNum is < 1 or > 999) throw new ArgumentOutOfRangeException(nameof(runNum), "runNum 必须在 1-999。");
    }

    private static string NormalizeType(string value, IEnumerable<string> allowed, string parameterName) =>
        allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{parameterName} 的值无效：{value}。", parameterName);

    private static void ValidateGroupName(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)
            || groupName is "." or ".."
            || groupName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("配置组名称为空或包含文件名非法字符。", nameof(groupName));
        }
    }

    private static void RequireConfirmation(bool confirm, string operation)
    {
        if (!confirm) throw new InvalidOperationException($"{operation}需要将 confirm 设为 true。");
    }

    private static object ToJavaScriptSummary(ScriptProject project) => new
    {
        folderName = project.FolderName,
        valid = true,
        project.Manifest.Name,
        project.Manifest.Version,
        project.Manifest.BgiVersion,
        project.Manifest.Description,
        project.Manifest.Main,
        project.Manifest.SettingsUi,
        hasSettings = project.Manifest.LoadSettingItems(project.ProjectPath).Count > 0,
        project.Manifest.HttpAllowedUrls,
        project.Manifest.Authors,
    };

    private sealed record GroupEntry(string File, ScriptGroup Group);
}