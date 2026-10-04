using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>将老一条龙的四类战斗任务接入统一服务，配置来自快照而非页面。</summary>
public sealed class PuloniaCombatTaskExecutor : IPuloniaTaskExecutor
{
    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } =
    [
        PuloniaCombatTaskSettings.Definition("builtin.auto_domain", "自动秘境", new AutoDomainConfig(),
            new JObject { ["strategy_name"] = "根据队伍自动选择", ["round_count"] = 0, ["max_artifact_star"] = "4", ["weekday_overrides"] = new JObject() }),
        PuloniaCombatTaskSettings.Definition("builtin.auto_boss", "自动首领讨伐", new AutoBossConfig(),
            new JObject { ["fight_config"] = PuloniaCombatTaskSettings.ToParameters(new AutoFightConfig()) }),
        PuloniaCombatTaskSettings.Definition("builtin.auto_stygian", "自动幽境危战", new AutoStygianOnslaughtConfig { StrategyName = "根据队伍自动选择" },
            new JObject { ["max_artifact_star"] = "4" }),
        PuloniaCombatTaskSettings.Definition("builtin.auto_ley_line", "自动地脉花", new AutoLeyLineOutcropConfig { FightConfig = new AutoLeyLineOutcropFightConfig { StrategyName = "根据队伍自动选择" } },
            new JObject { ["one_dragon_mode"] = true, ["weekday_overrides"] = new JObject() })
    ];

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        // 周配置按服务器四点日切选择，只合并本任务的字段，不修改计划或全局设置。
        var values = (JObject)task.Parameters.DeepClone();
        var serverTime = ServerTimeHelper.GetServerTimeNow().AddHours(-4);
        if (values["weekday_overrides"] is JObject weekly && weekly[((int)serverTime.DayOfWeek).ToString()] is JObject day)
        {
            if (day.Value<bool?>("enabled") == false)
                return new PuloniaTaskOutcome(PuloniaTaskOutcomeKind.Skipped, "今天不在此任务的运行日期内。");
            foreach (var field in day.Properties())
                if (field.Name != "enabled") values[field.Name] = field.Value.DeepClone();
            var definition = Definitions.First(item => item.TaskType == task.TaskType);
            PuloniaTaskValidator.ValidateParameters(values, definition.ParameterSchema, task.TaskType);
        }
        switch (task.TaskType)
        {
            case "builtin.auto_domain":
                var domain = PuloniaCombatTaskSettings.FromParameters<AutoDomainConfig>(values);
                if (string.IsNullOrWhiteSpace(domain.DomainName)) return PuloniaTaskOutcome.Failure("请选择秘境。");
                var rounds = values.Value<int>("round_count");
                if (rounds < 0 || rounds > 10000) return PuloniaTaskOutcome.Failure("秘境次数必须在 0—10000 之间。");
                var param = new AutoDomainParam(StrategyPath(values.Value<string>("strategy_name")))
                {
                    DomainRoundNum = rounds == 0 ? 9999 : rounds, PartyName = domain.PartyName, DomainName = domain.DomainName,
                    SundaySelectedValue = domain.SundaySelectedValue, AutoArtifactSalvage = domain.AutoArtifactSalvage,
                    MaxArtifactStar = values.Value<string>("max_artifact_star")!, SpecifyResinUse = domain.SpecifyResinUse,
                    ResinPriorityList = new List<string>(domain.ResinPriorityList), OriginalResinUseCount = domain.OriginalResinUseCount,
                    OriginalResin20UseCount = domain.OriginalResin20UseCount, OriginalResin40UseCount = domain.OriginalResin40UseCount,
                    CondensedResinUseCount = domain.CondensedResinUseCount, TransientResinUseCount = domain.TransientResinUseCount,
                    FragileResinUseCount = domain.FragileResinUseCount, RewardRecognitionEnabled = domain.RewardRecognitionEnabled
                };
                await new AutoDomainTask(param, domain).Start(ct).ConfigureAwait(false);
                break;
            case "builtin.auto_boss":
                var boss = PuloniaCombatTaskSettings.FromParameters<AutoBossConfig>(values);
                if (string.IsNullOrWhiteSpace(boss.BossName)) return PuloniaTaskOutcome.Failure("请选择首领。");
                if (boss.RunCount < 1 || boss.ReviveRetryCount < 0 || boss.Timeout < 1) return PuloniaTaskOutcome.Failure("首领次数、重试次数或超时配置无效。");
                var bossParam = AutoBossParam.CreateWithoutDefaultConfig(StrategyPath(boss.StrategyName));
                bossParam.SetAutoBossConfig(boss);
                var bossFight = PuloniaCombatTaskSettings.FromParameters<AutoFightConfig>((JObject)values["fight_config"]!);
                await new AutoBossTask(bossParam, bossFight).Start(ct).ConfigureAwait(false);
                break;
            case "builtin.auto_stygian":
                var stygian = PuloniaCombatTaskSettings.FromParameters<AutoStygianOnslaughtConfig>(values);
                if (stygian.BossNum is < 1 or > 3) return PuloniaTaskOutcome.Failure("幽境首领序号必须为 1—3。");
                var strategy = StrategyPath(stygian.StrategyName);
                var stygianParam = new AutoStygianOnslaughtParam(stygian) { CombatScriptBagPath = strategy, MaxArtifactStar = values.Value<string>("max_artifact_star") };
                await new AutoStygianOnslaughtTask(stygianParam, strategy).Start(ct).ConfigureAwait(false);
                break;
            case "builtin.auto_ley_line":
                var ley = PuloniaCombatTaskSettings.FromParameters<AutoLeyLineOutcropConfig>(values);
                if (ley.Count < 1 || ley.Timeout < 1) return PuloniaTaskOutcome.Failure("地脉花次数和超时必须为正数。");
                await new AutoLeyLineOutcropTask(new AutoLeyLineOutcropParam(ley), values.Value<bool>("one_dragon_mode")).Start(ct).ConfigureAwait(false);
                break;
            default: return PuloniaTaskOutcome.Failure("未知战斗任务：" + task.TaskType);
        }
        ct.ThrowIfCancellationRequested();
        // 旧任务没有统一的结构化领取证据，不能直接消费 Pulonia CD 或记为确认成功。
        return PuloniaTaskOutcome.ExecutedUnverified("战斗流程已结束，领取结果需核验。");
    }

    /// <summary>按显式策略名称解析脚本；缺失策略必须报错，不能隐式使用另一个页面的选择。</summary>
    private static string StrategyPath(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "根据队伍自动选择") return Global.Absolute(@"User\AutoFight\");
        if (name == AutoFightParam.ComboStrategyName) return name;
        var path = Global.Absolute(Path.Combine("User", "AutoFight", name + ".txt"));
        if (!File.Exists(path)) throw new FileNotFoundException("战斗策略不存在。", path);
        return path;
    }
}
