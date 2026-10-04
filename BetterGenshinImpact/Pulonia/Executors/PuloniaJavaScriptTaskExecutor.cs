using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 在统一取消上下文中执行现有 JS 项目，并让脚本内路线、宏和内置任务继承同一令牌。
/// </summary>
public sealed class PuloniaJavaScriptTaskExecutor : IPuloniaTaskExecutor
{
    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "javascript",
        DisplayName = "JS 脚本",
        Description = "从已拉取的脚本仓库选择 JS 项目，并配置脚本设置与队伍选项。",
        RequiresGameSession = true,
        ResourceBaseDirectory = Global.ScriptPath(),
        DefaultParameters = new(PuloniaTaskCommonSettings.CreateDefaults("javascript"))
        {
            ["settings"] = new JObject()
        },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(PuloniaTaskCommonSettings.CreateSchemaProperties("javascript"))
            {
                ["settings"] = new JObject { ["type"] = "object", ["additionalProperties"] = true }
            },
            ["additionalProperties"] = false
        },
        PublicParameters = ["settings", .. PuloniaTaskCommonSettings.CreateDefaults("javascript").Properties().Select(property => property.Name)]
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("JS 节点没有固定项目路径。");

        using var workspace = task.Resource is { } repositoryResource
            ? await ScriptRepositoryStore.Shared.AcquireWorkspaceAsync(repositoryResource, ct).ConfigureAwait(false) : null;
        using var repositoryResources = task.Resource is { } source
            ? new ScriptRepositoryResourceContext(await ScriptRepositoryStore.Shared.OpenApprovedAsync(source, ct).ConfigureAwait(false)) : null;
        var scriptRoot = workspace?.Path ?? Path.GetFullPath(Global.ScriptPath());
        var projectPath = workspace?.Path ?? Path.GetFullPath(task.Path);
        var relativeProjectPath = Path.GetRelativePath(scriptRoot, projectPath);
        if (relativeProjectPath == ".." || relativeProjectPath.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || Path.IsPathRooted(relativeProjectPath))
            return PuloniaTaskOutcome.Failure("JS 项目必须位于 BetterGI 的 User\\JsScript 目录内。");

        var parameters = task.Parameters;
        var settings = parameters["settings"]?.ToObject<ExpandoObject>() ?? new ExpandoObject();
        var partyConfig = PuloniaTaskCommonSettings.FromParameters("javascript", parameters).PathingConfig;
        // 脚本调用的路线和战斗使用同一份配置，辅助触发器在节点退出时统一撤销。
        using var autoPick = partyConfig.AutoPickEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null) : null;
        using var autoEat = partyConfig.AutoEatEnabled
            ? TaskTriggerDispatcher.Instance().AddTrigger("AutoEat", null) : null;

        var project = new ScriptProject(relativeProjectPath, scriptRoot, repositoryResources);
        await project.ExecuteAsync(settings, partyConfig, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PuloniaTaskOutcome.ExecutedUnverified("JS 项目已执行完成，但旧脚本没有结构化副作用证据。",
            new JObject { ["resource_version"] = task.ResourceVersion });
    }
}
