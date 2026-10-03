using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Models;
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
        Description = "选择已经安装的 JS 项目，并配置脚本设置与队伍选项。",
        RequiresGameSession = true,
        ResourceBaseDirectory = Global.ScriptPath(),
        DefaultParameters = new JObject
        {
            ["settings"] = new JObject(),
            ["party_name"] = "",
            ["auto_pick_enabled"] = true,
            ["auto_fight_enabled"] = true
        },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["settings"] = new JObject { ["type"] = "object", ["additionalProperties"] = true },
                ["party_name"] = new JObject { ["type"] = "string" },
                ["auto_pick_enabled"] = new JObject { ["type"] = "boolean" },
                ["auto_fight_enabled"] = new JObject { ["type"] = "boolean" }
            },
            ["additionalProperties"] = false
        },
        PublicParameters = ["settings", "party_name", "auto_pick_enabled", "auto_fight_enabled"]
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("JS 节点没有固定项目路径。");

        var scriptRoot = Path.GetFullPath(Global.ScriptPath());
        var projectPath = Path.GetFullPath(task.Path);
        var relativeProjectPath = Path.GetRelativePath(scriptRoot, projectPath);
        if (relativeProjectPath == ".." || relativeProjectPath.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || Path.IsPathRooted(relativeProjectPath))
            return PuloniaTaskOutcome.Failure("JS 项目必须位于 BetterGI 的 User\\JsScript 目录内。");

        var parameters = task.Parameters;
        var settings = parameters["settings"]?.ToObject<ExpandoObject>() ?? new ExpandoObject();
        var partyConfig = new PathingPartyConfig
        {
            Enabled = true,
            PartyName = parameters.Value<string>("party_name") ?? string.Empty,
            AutoPickEnabled = parameters.Value<bool>("auto_pick_enabled"),
            AutoFightEnabled = parameters.Value<bool>("auto_fight_enabled")
        };
        if (partyConfig.AutoPickEnabled)
            TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);

        var project = new ScriptProject(relativeProjectPath);
        await project.ExecuteAsync(settings, partyConfig, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PuloniaTaskOutcome.ExecutedUnverified("JS 项目已执行完成，但旧脚本没有结构化副作用证据。",
            new JObject { ["resource_version"] = task.ResourceVersion });
    }
}
