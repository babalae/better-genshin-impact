using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 单元测试使用的独立任务能力定义。真实的 <see cref="PuloniaSoloTaskExecutor"/> 要创建参数默认实例，
/// 战斗类参数的基类会读取运行中应用的全局配置与本地化服务，测试进程里无法创建；
/// 这里按执行器的同一规则建立定义：任务类型为 solo.{TaskId}，Schema 由参数类型生成并附带每周安排。
/// </summary>
internal static class PuloniaSoloTestDefinitions
{
    /// <summary>
    /// 按执行器规则建立一个独立任务能力定义。
    /// </summary>
    /// <param name="taskId">独立任务标识。</param>
    /// <param name="displayName">显示名。</param>
    /// <param name="paramType">参数类型，用于生成 Schema。</param>
    /// <param name="defaults">默认参数；战斗类参数无法在测试中实例化，由调用方给出常用字段。</param>
    /// <param name="group">菜单分组。</param>
    /// <param name="order">菜单排序。</param>
    /// <param name="oneDragonOrder">一条龙默认顺序，0 表示不默认加入。</param>
    /// <returns>能力定义。</returns>
    internal static PuloniaTaskDefinition Create(string taskId, string displayName, Type paramType, JObject defaults,
        string group = "", int order = 0, int oneDragonOrder = 0)
    {
        var schema = PuloniaSoloTaskSchema.Create(paramType);
        var properties = schema["properties"] as JObject ?? new JObject();
        properties[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] = new JObject { ["type"] = "object" };
        if (taskId == AutoDomainTask.TaskId)
            properties[PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter] = new JObject { ["type"] = "boolean" };
        if (taskId == AutoBossTask.TaskId)
        {
            properties[PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter] = new JObject { ["type"] = "integer", ["minimum"] = 0 };
            properties[PuloniaSoloTaskExecutor.BossRewardCountResetParameter] = new JObject { ["type"] = "string" };
        }
        schema["properties"] = properties;
        schema["additionalProperties"] = false;
        return new PuloniaTaskDefinition
        {
            TaskType = PuloniaSoloTaskExecutor.ToTaskType(taskId),
            DisplayName = displayName,
            Description = $"执行独立任务“{displayName}”。",
            RequiresGameSession = true,
            DefaultParameters = defaults,
            ParameterSchema = schema,
            PublicParameters = properties.Properties().Select(p => p.Name).ToArray(),
            IsSoloTask = true,
            Group = group,
            Order = order,
            OneDragonOrder = oneDragonOrder
        };
    }

    /// <summary>
    /// 按 SoloTaskJson 合同序列化一个可以在测试中直接创建的参数实例，作为默认参数。
    /// </summary>
    /// <param name="param">参数实例。</param>
    /// <returns>参数 JSON。</returns>
    internal static JObject Defaults<T>(T param) => JObject.Parse(SoloTaskJson.Serialize(param));

    /// <summary>
    /// 自动首领讨伐的能力定义，默认参数取参数类的常用默认值。
    /// </summary>
    /// <returns>能力定义。</returns>
    internal static PuloniaTaskDefinition AutoBoss() => Create(AutoBossTask.TaskId, "自动首领讨伐", typeof(AutoBossParam),
        new JObject
        {
            ["boss_name"] = "", ["strategy_name"] = "根据队伍自动选择", ["combat_strategy_path"] = "根据队伍自动选择",
            ["team_name"] = "", ["specify_run_count"] = false, ["run_count"] = 1, ["revive_retry_count"] = 3, ["timeout"] = 240
        }, "树脂与战斗", 220, 4);

    /// <summary>
    /// 与注册表声明一致的一条龙候选任务：八个带 OneDragonOrder 的任务（故意打乱顺序），外加一个不进入默认清单的任务。
    /// </summary>
    /// <returns>能力定义数组。</returns>
    internal static PuloniaTaskDefinition[] OneDragonCandidates() =>
    [
        Create(GoToSereniteaPotTask.TaskId, "领取尘歌壶奖励", typeof(SereniteaPotParam), Defaults(new SereniteaPotParam()), "日常奖励", 140, 8),
        Create(ReturnMainUiTask.TaskId, "返回主界面", typeof(NoParam), new JObject(), "工具", 430),
        Create(ClaimMailRewardsTask.TaskId, "领取邮件", typeof(NoParam), new JObject(), "日常奖励", 110, 1),
        Create(AutoDomainTask.TaskId, "自动秘境", typeof(AutoDomainParam),
            new JObject { ["domain_round_num"] = 9999, ["party_name"] = "", ["domain_name"] = "" }, "树脂与战斗", 210, 3),
        Create(GoToCraftingBenchTask.TaskId, "合成浓缩树脂", typeof(CraftCondensedResinParam), Defaults(new CraftCondensedResinParam()), "日常奖励", 120, 2),
        AutoBoss(),
        Create(AutoStygianOnslaughtTask.TaskId, "自动幽境危战", typeof(AutoStygianOnslaughtParam),
            new JObject { ["boss_num"] = 1, ["strategy_name"] = "根据队伍自动选择" }, "树脂与战斗", 230, 5),
        Create(AutoLeyLineOutcropTask.TaskId, "自动地脉花", typeof(AutoLeyLineOutcropParam),
            new JObject { ["count"] = 6, ["country"] = "蒙德", ["ley_line_outcrop_type"] = "启示之花" }, "树脂与战斗", 240, 6),
        Create(ClaimDailyRewardsTask.TaskId, "领取每日奖励", typeof(DailyRewardsParam), Defaults(new DailyRewardsParam()), "日常奖励", 130, 7)
    ];
}
