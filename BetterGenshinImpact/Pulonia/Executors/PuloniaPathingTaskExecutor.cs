using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 使用现有地图追踪能力执行 Pulonia 路线资源，并返回路线真实完成状态。
/// </summary>
public sealed class PuloniaPathingTaskExecutor : IPuloniaTaskExecutor
{
    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "pathing",
        DisplayName = "地图追踪",
        Description = "选择本地路线文件，并配置队伍、自动拾取与自动战斗选项。",
        RequiresGameSession = true,
        ResourceBaseDirectory = MapPathingViewModel.PathJsonPath,
        DefaultParameters = new JObject
        {
            ["party_name"] = "",
            ["skip_party_switch"] = false,
            ["auto_pick_enabled"] = true,
            ["auto_fight_enabled"] = true,
            ["auto_skip_enabled"] = true
        },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["party_name"] = new JObject { ["type"] = "string" },
                ["skip_party_switch"] = new JObject { ["type"] = "boolean" },
                ["auto_pick_enabled"] = new JObject { ["type"] = "boolean" },
                ["auto_fight_enabled"] = new JObject { ["type"] = "boolean" },
                ["auto_skip_enabled"] = new JObject { ["type"] = "boolean" }
            },
            ["additionalProperties"] = false
        },
        PublicParameters = ["party_name", "skip_party_switch", "auto_pick_enabled", "auto_fight_enabled", "auto_skip_enabled"]
    }];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Path))
            return PuloniaTaskOutcome.Failure("地图追踪节点没有固定资源路径。");
        var pathingTask = PathingTask.BuildFromFilePath(task.Path);
        if (pathingTask is null)
            return PuloniaTaskOutcome.Failure("路线版本高于当前 BetterGI，已拒绝执行。");

        var parameters = task.Parameters;
        var partyConfig = new PathingPartyConfig
        {
            Enabled = true,
            PartyName = parameters.Value<string>("party_name") ?? string.Empty,
            SkipPartySwitch = parameters.Value<bool>("skip_party_switch"),
            AutoPickEnabled = parameters.Value<bool>("auto_pick_enabled"),
            AutoFightEnabled = parameters.Value<bool>("auto_fight_enabled"),
            AutoSkipEnabled = parameters.Value<bool>("auto_skip_enabled")
        };
        if (partyConfig.AutoPickEnabled)
            TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);

        var executor = new PathExecutor(ct) { PartyConfig = partyConfig };
        await executor.Pathing(pathingTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data = new JObject
        {
            ["success_end"] = executor.SuccessEnd,
            ["success_fight"] = executor.SuccessFight,
            ["resource_version"] = task.ResourceVersion
        };
        return executor.SuccessEnd
            ? PuloniaTaskOutcome.ExecutedUnverified("地图追踪路线已完整执行，但没有逐点采集证据。", data)
            : PuloniaTaskOutcome.Failure("地图追踪路线未完整执行。", data);
    }
}
