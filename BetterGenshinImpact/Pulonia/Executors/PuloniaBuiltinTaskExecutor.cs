using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 将无需页面状态的常用内置任务暴露为具有明确参数和真实异常结果的 Pulonia 能力。
/// </summary>
public sealed class PuloniaBuiltinTaskExecutor : IPuloniaTaskExecutor
{
    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } =
    [
        BuildEmptyDefinition("builtin.return_main_ui"),
        BuildEmptyDefinition("builtin.claim_mail"),
        BuildEmptyDefinition("builtin.claim_battle_pass"),
        BuildEmptyDefinition("builtin.claim_encounter_points"),
        BuildEmptyDefinition("builtin.serenitea_pot_rewards"),
        new PuloniaTaskDefinition
        {
            TaskType = "builtin.daily_rewards",
            RequiresGameSession = true,
            DefaultParameters = new JObject
            {
                ["country"] = "蒙德",
                ["party_name"] = null
            },
            ParameterSchema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["country"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("蒙德", "璃月", "稻妻", "须弥", "枫丹", "挪德卡莱")
                    },
                    ["party_name"] = new JObject { ["type"] = new JArray("string", "null") }
                },
                ["required"] = new JArray("country"),
                ["additionalProperties"] = false
            },
            PublicParameters = ["country", "party_name"]
        },
        new PuloniaTaskDefinition
        {
            TaskType = "builtin.craft_condensed_resin",
            RequiresGameSession = true,
            DefaultParameters = new JObject { ["country"] = "蒙德" },
            ParameterSchema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["country"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("蒙德", "璃月", "稻妻", "枫丹")
                    }
                },
                ["required"] = new JArray("country"),
                ["additionalProperties"] = false
            },
            PublicParameters = ["country"]
        }
    ];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        switch (task.TaskType)
        {
            case "builtin.return_main_ui":
                await new ReturnMainUiTask().Start(ct).ConfigureAwait(false);
                break;
            case "builtin.claim_mail":
                await new ClaimMailRewardsTask().DoOnce(ct).ConfigureAwait(false);
                break;
            case "builtin.claim_battle_pass":
                await new ClaimBattlePassRewardsTask().DoOnce(ct).ConfigureAwait(false);
                break;
            case "builtin.claim_encounter_points":
                if (!await new ClaimEncounterPointsRewardsTask().DoOnce(ct).ConfigureAwait(false))
                    return PuloniaTaskOutcome.Failure("未能进入或处理历练点领取页面。");
                break;
            case "builtin.daily_rewards":
                await ExecuteDailyRewardsAsync(task.Parameters, ct).ConfigureAwait(false);
                break;
            case "builtin.craft_condensed_resin":
                await new GoToCraftingBenchTask().GoCraftResin(
                    task.Parameters.Value<string>("country")!, ct).ConfigureAwait(false);
                break;
            case "builtin.serenitea_pot_rewards":
                if (!await new GoToSereniteaPotTask().DoOnce(ct).ConfigureAwait(false))
                    return PuloniaTaskOutcome.Failure("尘歌壶奖励流程未到达领取与收尾阶段。");
                break;
            default:
                return PuloniaTaskOutcome.Failure($"未知内置任务类型 {task.TaskType}。");
        }

        ct.ThrowIfCancellationRequested();
        return PuloniaTaskOutcome.Success($"内置任务 {task.TaskType} 已执行完成。");
    }

    /// <summary>
    /// 执行每日奖励完整链路，任何子步骤异常都返回给 Pulonia 协调器。
    /// </summary>
    private static async Task ExecuteDailyRewardsAsync(JObject parameters, CancellationToken ct)
    {
        if (!await new ClaimEncounterPointsRewardsTask().DoOnce(ct).ConfigureAwait(false))
            throw new InvalidOperationException("未能进入或处理历练点领取页面。");
        await new GoToAdventurersGuildTask().Start(parameters.Value<string>("country")!, ct,
            parameters.Value<string?>("party_name"), onlyDoOnce: true).ConfigureAwait(false);
        await new ClaimBattlePassRewardsTask().DoOnce(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 建立无参数、但仍需游戏会话和输入所有权的内置能力定义。
    /// </summary>
    private static PuloniaTaskDefinition BuildEmptyDefinition(string taskType) => new()
    {
        TaskType = taskType,
        RequiresGameSession = true,
        DefaultParameters = new JObject(),
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false
        }
    };
}
