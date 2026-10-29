using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划：在一次运行中按固定顺序调度合成浓缩树脂、首领、秘境养成与剩余树脂承接，并汇总本次全部奖励。
/// 秘境养成沿用自动秘境的“根据提升指南选择秘境”，需求以游戏内养成计划为准；账号由一条龙负责。
/// 顺序：读取树脂 → 识别活动 → 合成浓缩（可选）→ 活动（移涌领取 → 紊乱爆发期，开启优先满足活动时）
/// → 首领 → 秘境养成 → 剩余树脂（幽境危战 → 地脉花 → 备选秘境）。
/// </summary>
[SoloTaskInfo(TaskId, "体力计划", Description = "按体力用量依次调度合成浓缩、首领、提升指南养成与剩余树脂任务，并汇总本次全部奖励。",
    Group = "树脂与战斗", Order = 200, Scopes = SoloTaskScopes.Plan, OneDragonOrder = 2)]
[SoloTaskSettingsView(typeof(ResinPlanSettingsView), typeof(ResinPlanSettingsViewModel))]
public sealed class ResinPlanTask : ISoloTask<ResinPlanParam, ResinPlanData>
{
    /// <summary>独立任务稳定标识。</summary>
    public const string TaskId = "resin_plan";

    /// <summary>日志中使用的任务名。</summary>
    private const string TaskName = "体力计划";

    /// <summary>本次运行使用的运行环境。</summary>
    private readonly GameRuntime _runtime;

    /// <summary>配置根，传给各子任务。</summary>
    private readonly AllConfig _config;

    /// <summary>创建体力计划任务。</summary>
    /// <param name="runtime">本次运行使用的运行环境。</param>
    /// <param name="config">配置根。</param>
    public ResinPlanTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 按顺序执行各阶段并汇总结果。子任务失败或抛出业务异常时记录该阶段失败，回到主界面后继续后续阶段；
    /// 取消与运行环境停止直接向外抛出。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>参数无效或读不到树脂时返回失败；其余按阶段结果汇总，并附带全部奖励与阶段记录。</returns>
    public async Task<SoloTaskResult<ResinPlanData>> RunAsync(ResinPlanParam param, CancellationToken ct)
    {
        // 0. 参数校验：任何游戏操作之前确认参数可用
        if (Validate(param) is { } problem)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, problem);
        }

        var run = new ResinPlanRun(param.MaxResin);
        Logger.LogInformation("→ {Name} 开始：{Budget}", TaskName,
            param.MaxResin > 0 ? $"本次最多消耗 {param.MaxResin} 点树脂" : "使用全部原粹与浓缩树脂");

        // 1. 读取开始时的树脂；读不到时不消耗任何树脂
        var stock = await ReadStockAsync(ct);
        if (stock is null)
        {
            return ResinPlanResults.Failed(run.Data, ResinPlanReasons.ResinUnknown, "未能读取当前树脂数量，体力计划没有消耗树脂。");
        }

        run.Start(stock);

        // 1.1 识别活动：移涌活动与幽境危战紊乱爆发期；识别失败按没有活动处理，结束后回到主界面
        // 开启优先满足活动时用于调度；危战开启时用于判断紊乱爆发期是否已结束。两者都关闭时不打开活动页
        if (param.EventPriority || param.Stygian.Enabled)
        {
            run.Data.Events = await new ResinPlanEventDetector(_runtime).DetectAsync(ct);
            await SafeReturnMainUiAsync(ct);
        }

        var events = run.Data.Events is { Recognized: true } recognized ? recognized : null;
        // 爆发期优先时危战会暂停首领，合成时不再为首领预留原粹
        var burstPriority = param.EventPriority && param.Stygian.Enabled && events?.StygianBurst == StygianBurstState.Active;
        if (!param.EventPriority && events?.Overflows.Count > 0)
        {
            Logger.LogInformation("{Name}：未开启优先满足活动，移涌活动不单独领取", TaskName);
        }

        // 2. 合成浓缩树脂（可选），为首领与移涌领取预留原粹
        if (param.CraftCondensedFirst)
        {
            await RunCraftAsync(param, run, burstPriority, ct);
        }

        // 3. 活动优先：按地脉移涌 → 盛材移涌 → 祝圣移涌领取双倍，再处理紊乱爆发期
        var stygianHandled = false;
        var pauseTraining = false;
        if (param.EventPriority && events is not null)
        {
            foreach (var overflow in events.Overflows.OrderBy(item => item.Event))
            {
                await RunOverflowAsync(param, run, overflow, ct);
            }

            if (events.StygianBurst == StygianBurstState.Active)
            {
                stygianHandled = true;
                pauseTraining = await RunStygianBurstAsync(param, run, ct);
            }
        }

        // 4. 首领讨伐：固定次数，只用原粹；爆发期危战已用掉树脂时暂停
        if (param.Boss.Enabled)
        {
            if (pauseTraining)
            {
                SkipForBurst(run, ResinPlanStage.Boss, "首领讨伐", AutoBossTask.TaskId, param.Boss.Param.BossName);
            }
            else
            {
                await RunBossAsync(param, run, ct);
            }
        }

        // 5. 秘境养成：根据提升指南选择秘境，可连续刷多个候选秘境；爆发期危战已用掉树脂时暂停
        if (param.Domain.Enabled)
        {
            if (pauseTraining)
            {
                SkipForBurst(run, ResinPlanStage.TrainingDomain, "秘境养成", AutoDomainTask.TaskId, AutoDomainTask.TrainingGuideOption);
            }
            else
            {
                await RunTrainingDomainAsync(param, run, ct);
            }
        }

        // 6. 剩余树脂依次承接：幽境危战（紊乱爆发期结束时跳过；活动阶段已执行时不再执行）→ 地脉花 → 备选秘境
        if (param.Stygian.Enabled && !stygianHandled)
        {
            await RunStygianAsync(param, run, ct);
        }

        if (param.LeyLine.Enabled)
        {
            await RunLeyLineAsync(param, run, ct);
        }

        if (param.Domain.Enabled)
        {
            await RunFallbackDomainAsync(param, run, ct);
        }

        // 7. 汇总奖励与消耗
        var result = ResinPlanResults.Complete(run.Data);
        Logger.LogInformation("→ {Name} 结束：{Message}", TaskName, result.Message);
        return result;
    }

    /// <summary>校验参数取值范围。</summary>
    /// <param name="param">体力计划参数。</param>
    /// <returns>错误说明；合法时返回 null。</returns>
    internal static string? Validate(ResinPlanParam param)
    {
        if (param.MaxResin < 0)
        {
            return "体力用量不能为负数；0 表示使用全部树脂。";
        }

        if (param.CraftCondensedFirst && string.IsNullOrWhiteSpace(param.Craft.Country))
        {
            return "已开启优先合成浓缩树脂，但未选择合成台所在地区。";
        }

        return null;
    }

    /// <summary>按预算计算合成份数并合成浓缩树脂；合成是转换，不计入领奖消耗。</summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="run">运行状态。</param>
    /// <param name="burstPriority">紊乱爆发期优先：首领会被暂停，不为其预留原粹。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task RunCraftAsync(ResinPlanParam param, ResinPlanRun run, bool burstPriority, CancellationToken ct)
    {
        var step = run.AddStep(ResinPlanStage.Craft, "合成浓缩树脂", GoToCraftingBenchTask.TaskId, param.Craft.Country);
        var stock = await GetStockAsync(run, ct);
        if (stock is null)
        {
            Skip(step, ResinPlanReasons.ResinUnknown, "未能读取当前树脂数量，不合成。");
            return;
        }

        if (stock.Condensed is null)
        {
            Skip(step, ResinPlanReasons.ResinUnknown, "浓缩树脂数量未识别，不合成。");
            return;
        }

        // 首领只能使用原粹，移涌领取只有原粹享受双倍，合成前先为它们留出原粹；首领刷至耗尽时不合成
        var bossReserve = burstPriority
            ? 0
            : ResinPlanBudget.BossReserve(param.Boss.Enabled, ResinPlanBudget.BossRunLimit(param.Boss.Param.ResinUsage));
        var reserve = ResinPlanBudget.SaturatingAdd(bossReserve, PlannedOverflowReserve(param, run));
        var count = ResinPlanBudget.CraftCount(run.Remaining, stock, reserve);
        if (count == 0)
        {
            Skip(step, ResinPlanReasons.CraftNotNeeded, "可用原粹不足一份或浓缩树脂已达上限，无需合成。");
            return;
        }

        var craft = Clone(param.Craft);
        craft.CraftCount = count;
        craft.MinResinToKeep = 0;
        await ExecuteAsync(run, step, async () => await new GoToCraftingBenchTask(_runtime, _config).ExecuteAsync(craft, ct), ct);
    }

    /// <summary>
    /// 首领讨伐：次数沿用首领自己的树脂设置（指定次数或刷至耗尽），受剩余预算和原粹限制；不使用须臾、脆弱补充。
    /// </summary>
    private async Task RunBossAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var step = run.AddStep(ResinPlanStage.Boss, "首领讨伐", AutoBossTask.TaskId, param.Boss.Param.BossName);
        step.RewardsRecognized = true;
        var runLimit = ResinPlanBudget.BossRunLimit(param.Boss.Param.ResinUsage);
        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.BossClaimPoints,
            stock => ResinPlanBudget.BuildBossUsage(run.Remaining, stock, runLimit), ct);
        if (usage is null)
        {
            return;
        }

        var boss = Clone(param.Boss.Param);
        boss.ResinUsage = usage;
        boss.RewardRecognitionEnabled = true;
        boss.TargetItems = [];
        await ExecuteAsync(run, step, async () => await new AutoBossTask(_runtime, _config).ExecuteAsync(boss, ct), ct);
    }

    /// <summary>
    /// 秘境养成：沿用自动秘境的提升指南方案，按游戏内养成计划选择秘境与次数，一个秘境满足后继续下一个候选秘境。
    /// 备选秘境改到剩余树脂阶段执行，保证承接顺序固定。
    /// </summary>
    private async Task RunTrainingDomainAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var step = run.AddStep(ResinPlanStage.TrainingDomain, "秘境养成", AutoDomainTask.TaskId, AutoDomainTask.TrainingGuideOption);
        step.RewardsRecognized = true;
        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildClaimUsage(run.Remaining, stock), ct);
        if (usage is null)
        {
            return;
        }

        var domain = BuildTrainingDomainParam(param.Domain.Param, usage);
        await ExecuteAsync(run, step, async () => await new AutoDomainTask(_runtime, _config).ExecuteAsync(domain, ct), ct);
    }

    /// <summary>剩余树脂：幽境危战。紊乱爆发期已结束时任务返回跳过，剩余树脂继续交给后面的任务。</summary>
    private async Task RunStygianAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var stygianParam = param.Stygian.Param;
        var step = run.AddStep(ResinPlanStage.Stygian, "幽境危战", AutoStygianOnslaughtTask.TaskId, $"首领 {stygianParam.BossNum}");
        // 活动页已明确显示爆发期结束时不再前往危战；其余情况仍由危战任务自行判断
        if (run.Data.Events?.StygianBurst == StygianBurstState.Ended)
        {
            Skip(step, AutoStygianOnslaughtTask.EventEndedReason, "活动页显示紊乱爆发期已结束，跳过幽境危战。");
            return;
        }

        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildClaimUsage(run.Remaining, stock), ct);
        if (usage is null)
        {
            return;
        }

        var stygian = BuildStygianParam(stygianParam, usage);
        await ExecuteAsync(run, step, async () => await new AutoStygianOnslaughtTask(_runtime, _config).ExecuteAsync(stygian, ct), ct);
    }

    /// <summary>剩余树脂：地脉花。</summary>
    private async Task RunLeyLineAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var leyLineParam = param.LeyLine.Param;
        var step = run.AddStep(ResinPlanStage.LeyLine, "地脉花", AutoLeyLineOutcropTask.TaskId,
            $"{leyLineParam.Country}·{leyLineParam.LeyLineOutcropType}");
        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildClaimUsage(run.Remaining, stock), ct);
        if (usage is null)
        {
            return;
        }

        var leyLine = BuildLeyLineParam(leyLineParam, usage);
        await ExecuteAsync(run, step, async () => await new AutoLeyLineOutcropTask(_runtime, _config).ExecuteAsync(leyLine, ct), ct);
    }

    /// <summary>剩余树脂：自动秘境设置中的备选秘境；未设置时保留剩余树脂。</summary>
    private async Task RunFallbackDomainAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var domainParam = param.Domain.Param;
        var fallback = FallbackDomainName(domainParam);
        if (fallback.Length == 0)
        {
            // 只有预算未用完且最近一次读取仍能领奖时才提示保留，树脂用完后不追加多余说明
            var budgetLeft = run.Remaining is not { } remaining || remaining >= ResinPlanBudget.MinClaimPoints;
            if (budgetLeft && run.Stock?.CanClaim != false)
            {
                Skip(run.AddStep(ResinPlanStage.FallbackDomain, "备选秘境", AutoDomainTask.TaskId, string.Empty),
                    ResinPlanReasons.NoFallbackDomain, "未设置备选秘境，剩余树脂保留。");
            }

            return;
        }

        var step = run.AddStep(ResinPlanStage.FallbackDomain, "备选秘境", AutoDomainTask.TaskId, fallback);
        step.RewardsRecognized = true;
        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildClaimUsage(run.Remaining, stock), ct);
        if (usage is null)
        {
            return;
        }

        var domain = BuildManualDomainParam(domainParam, fallback, domainParam.TrainingGuideFallbackSundaySelectedValue, usage);
        await ExecuteAsync(run, step, async () => await new AutoDomainTask(_runtime, _config).ExecuteAsync(domain, ct), ct);
    }

    /// <summary>
    /// 移涌领取：在活动适用的内容里先用原粹领取，把当天的双倍次数用掉；领取完成后后续阶段照常执行。
    /// 前提不满足（对应子任务未开启、没有可用的祝圣秘境、双倍已用完）时记为跳过并写明原因。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="run">运行状态。</param>
    /// <param name="overflow">识别到的移涌活动。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task RunOverflowAsync(ResinPlanParam param, ResinPlanRun run, ResinPlanOverflowStatus overflow, CancellationToken ct)
    {
        var name = ResinPlanEventDetector.DisplayName(overflow.Event);
        switch (overflow.Event)
        {
            // 1. 地脉移涌：按地脉花子设置的国家与类型领取
            case ResinPlanOverflowEvent.LeyLineOverflow:
            {
                var leyLineParam = param.LeyLine.Param;
                var step = run.AddStep(ResinPlanStage.Event, $"{name}·地脉花", AutoLeyLineOutcropTask.TaskId,
                    $"{leyLineParam.Country}·{leyLineParam.LeyLineOutcropType}");
                if (!param.LeyLine.Enabled)
                {
                    Skip(step, ResinPlanReasons.EventTaskDisabled, $"{name}需要开启自动地脉花，本次不单独领取。");
                    return;
                }

                if (await PlanOverflowUsageAsync(param, run, step, overflow, ct) is not { } usage)
                {
                    return;
                }

                var leyLine = BuildLeyLineParam(leyLineParam, usage);
                await ExecuteAsync(run, step, async () => await new AutoLeyLineOutcropTask(_runtime, _config).ExecuteAsync(leyLine, ct), ct);
                return;
            }

            // 2. 盛材移涌：跟随培养计划时按提升指南选择秘境（参数改写与秘境养成相同，只有树脂设置不同）；也可指定精通或炼武秘境
            case ResinPlanOverflowEvent.MaterialOverflow:
            {
                var chosen = OverflowDomainName(param.Domain.Param.MaterialOverflowDomainName);
                var follow = chosen.Length == 0;
                var step = run.AddStep(ResinPlanStage.Event, follow ? $"{name}·秘境养成" : $"{name}·指定秘境", AutoDomainTask.TaskId,
                    follow ? AutoDomainTask.TrainingGuideOption : chosen);
                step.RewardsRecognized = true;
                if (!param.Domain.Enabled)
                {
                    Skip(step, ResinPlanReasons.EventTaskDisabled, $"{name}需要开启自动秘境，本次不单独领取。");
                    return;
                }

                if (!follow && !IsMaterialDomain(chosen))
                {
                    Skip(step, ResinPlanReasons.EventDomainMissing,
                        $"{name}只对精通秘境和炼武秘境生效，自动秘境设置中的盛材移涌秘境“{chosen}”不是这两类秘境。");
                    return;
                }

                if (await PlanOverflowUsageAsync(param, run, step, overflow, ct) is not { } usage)
                {
                    return;
                }

                // 指定秘境时由游戏决定周日入口，奖励序号不另设
                var domain = follow
                    ? BuildTrainingDomainParam(param.Domain.Param, usage)
                    : BuildManualDomainParam(param.Domain.Param, chosen, string.Empty, usage);
                await ExecuteAsync(run, step, async () => await new AutoDomainTask(_runtime, _config).ExecuteAsync(domain, ct), ct);
                return;
            }

            // 3. 祝圣移涌：只在祝圣秘境生效；跟随培养计划时刷提升指南中养成角色推荐圣遗物所在的秘境，也可指定祝圣秘境
            case ResinPlanOverflowEvent.BlessingOverflow:
            {
                var chosen = OverflowDomainName(param.Domain.Param.BlessingOverflowDomainName);
                var follow = chosen.Length == 0;
                var step = run.AddStep(ResinPlanStage.Event, $"{name}·祝圣秘境", AutoDomainTask.TaskId,
                    follow ? TrainingGuideDomainOptions.FollowTrainingPlanOption : chosen);
                step.RewardsRecognized = true;
                if (!param.Domain.Enabled)
                {
                    Skip(step, ResinPlanReasons.EventTaskDisabled, $"{name}需要开启自动秘境，本次不单独领取。");
                    return;
                }

                if (!follow && !IsBlessDomain(chosen))
                {
                    Skip(step, ResinPlanReasons.EventDomainMissing,
                        $"{name}只对祝圣秘境（圣遗物秘境）生效，自动秘境设置中的祝圣移涌秘境“{chosen}”不是祝圣秘境。");
                    return;
                }

                if (await PlanOverflowUsageAsync(param, run, step, overflow, ct) is not { } usage)
                {
                    return;
                }

                // 跟随培养计划：确认有树脂可领后再读取提升指南，避免树脂不足时白跑一趟
                if (follow)
                {
                    chosen = await FindTrainingPlanArtifactDomainAsync(ct);
                    if (chosen.Length == 0)
                    {
                        Skip(step, ResinPlanReasons.EventDomainMissing,
                            $"{name}跟随培养计划，但提升指南中没有圣遗物秘境：请先在游戏内把要培养圣遗物的角色加入养成计划，或在自动秘境设置中指定祝圣移涌秘境。");
                        return;
                    }

                    step.Target = chosen;
                }

                var domain = BuildManualDomainParam(param.Domain.Param, chosen, string.Empty, usage);
                await ExecuteAsync(run, step, async () => await new AutoDomainTask(_runtime, _config).ExecuteAsync(domain, ct), ct);
                return;
            }
        }
    }

    /// <summary>
    /// 生成移涌领取的树脂设置：今日双倍已用完时跳过；否则按剩余预算与最新持有量计算次数与档位。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="run">运行状态。</param>
    /// <param name="step">本步记录。</param>
    /// <param name="overflow">识别到的移涌活动。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>树脂设置；本步跳过时为 null。</returns>
    private async Task<ResinUsage?> PlanOverflowUsageAsync(ResinPlanParam param, ResinPlanRun run, ResinPlanStep step,
        ResinPlanOverflowStatus overflow, CancellationToken ct)
    {
        var name = ResinPlanEventDetector.DisplayName(overflow.Event);
        var doubles = OverflowDoubles(overflow);
        if (doubles == 0)
        {
            Skip(step, ResinPlanReasons.EventClaimsUsedUp, $"{name}今日双倍次数已用完，不再单独领取。");
            return null;
        }

        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildOverflowUsage(run.Remaining, stock, doubles, param.EventStrictResin), ct);
        if (usage is not null)
        {
            Logger.LogInformation("{Name}：{Event}今日剩余双倍 {Doubles} 次，原粹 {Tier} 档领取 {Count} 次", TaskName, name, doubles,
                usage.OriginalTier == OriginalResinTier.Only20 ? ResinPlanBudget.MinClaimPoints : ResinPlanBudget.OriginalHighTierPoints,
                usage.Counts.Original);
        }

        return usage;
    }

    /// <summary>
    /// 紊乱爆发期进行中：危战用掉剩余预算。危战发现爆发期已结束、失败，或勋望达到目标时，首领与秘境养成照常执行；
    /// 其余情况暂停这两个阶段。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="run">运行状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否暂停首领与秘境养成。</returns>
    private async Task<bool> RunStygianBurstAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var stygianParam = param.Stygian.Param;
        var step = run.AddStep(ResinPlanStage.Event, "紊乱爆发期·幽境危战", AutoStygianOnslaughtTask.TaskId, $"首领 {stygianParam.BossNum}");
        if (!param.Stygian.Enabled)
        {
            Skip(step, ResinPlanReasons.EventTaskDisabled, "紊乱爆发期需要开启自动幽境危战，本次不优先使用树脂。");
            return false;
        }

        var usage = await PlanUsageAsync(run, step, ResinPlanBudget.MinClaimPoints,
            stock => ResinPlanBudget.BuildClaimUsage(run.Remaining, stock), ct);
        if (usage is null)
        {
            return false;
        }

        // 勋望达到目标即停止，剩余预算回到首领与秘境养成；识别实现前不会提前停止
        var stygian = BuildStygianParam(stygianParam, usage);
        stygian.DirePrestigeTarget = StygianDirePrestigeReader.CommendationPrestige;
        await ExecuteAsync(run, step, async () => await new AutoStygianOnslaughtTask(_runtime, _config).ExecuteAsync(stygian, ct), ct);

        var resume = step.Kind == SoloTaskResultKind.Failed
                     || step is { Kind: SoloTaskResultKind.Skipped, ReasonCode: AutoStygianOnslaughtTask.EventEndedReason }
                     || step.Resin?.StopReason == ResinStopReason.TargetReached;
        return !resume;
    }

    /// <summary>紊乱爆发期优先使用树脂时，记录首领或秘境养成被跳过。</summary>
    /// <param name="run">运行状态。</param>
    /// <param name="stage">被暂停的阶段。</param>
    /// <param name="name">阶段中文名称。</param>
    /// <param name="taskId">子任务标识。</param>
    /// <param name="target">本步对象。</param>
    private static void SkipForBurst(ResinPlanRun run, ResinPlanStage stage, string name, string taskId, string target)
        => Skip(run.AddStep(stage, name, taskId, target), ResinPlanReasons.StygianBurst, $"紊乱爆发期优先使用树脂，跳过{name}。");

    /// <summary>
    /// 合成浓缩前为移涌领取预留的原粹：只计开启优先满足活动时会执行的领取（前提满足且今日还有双倍）。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="run">运行状态。</param>
    /// <returns>预留点数。</returns>
    private static int PlannedOverflowReserve(ResinPlanParam param, ResinPlanRun run)
    {
        if (!param.EventPriority || run.Data.Events is not { Recognized: true } events)
        {
            return 0;
        }

        return events.Overflows
            .Where(overflow => CanClaimOverflow(param, overflow.Event))
            .Sum(overflow => ResinPlanBudget.OverflowReserve(OverflowDoubles(overflow), param.EventStrictResin));
    }

    /// <summary>
    /// 移涌领取的前提是否满足：对应子任务已开启；指定了移涌活动秘境时，秘境类型须与活动一致。
    /// 祝圣移涌跟随培养计划时，提升指南中有没有圣遗物秘境要到领取时才知道，这里按会领取预留。
    /// </summary>
    /// <param name="param">体力计划参数。</param>
    /// <param name="kind">移涌活动。</param>
    /// <returns>前提满足时返回 true。</returns>
    private static bool CanClaimOverflow(ResinPlanParam param, ResinPlanOverflowEvent kind)
    {
        var domain = param.Domain.Param;
        return kind switch
        {
            ResinPlanOverflowEvent.LeyLineOverflow => param.LeyLine.Enabled,
            ResinPlanOverflowEvent.MaterialOverflow => param.Domain.Enabled
                && OverflowDomainName(domain.MaterialOverflowDomainName) is var material
                && (material.Length == 0 || IsMaterialDomain(material)),
            ResinPlanOverflowEvent.BlessingOverflow => param.Domain.Enabled
                && OverflowDomainName(domain.BlessingOverflowDomainName) is var blessing
                && (blessing.Length == 0 || IsBlessDomain(blessing)),
            _ => false
        };
    }

    /// <summary>
    /// 读取提升指南，取列表中第一个祝圣秘境（养成计划角色推荐圣遗物所在的秘境）；没有或读取失败时返回空字符串。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>祝圣秘境名；没有时为空字符串。</returns>
    private async Task<string> FindTrainingPlanArtifactDomainAsync(CancellationToken ct)
    {
        try
        {
            var domains = await new AutoDomainTask(_runtime, _config).ScanTrainingGuideArtifactDomainsAsync(ct);
            return domains.FirstOrDefault(IsBlessDomain) ?? string.Empty;
        }
        catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
        {
            // 提升指南识别失败按“没有圣遗物秘境”处理，只跳过本次祝圣移涌
            Logger.LogWarning("{Name}：读取提升指南中的圣遗物秘境失败：{Message}", TaskName, e.Message);
            await SafeReturnMainUiAsync(ct);
            return string.Empty;
        }
    }

    /// <summary>
    /// 自动秘境设置中的移涌活动秘境；空字符串、“跟随培养计划”或“根据提升指南选择秘境”都表示跟随培养计划，返回空字符串。
    /// </summary>
    /// <param name="value">参数中保存的秘境名。</param>
    /// <returns>指定的秘境名；跟随培养计划时为空字符串。</returns>
    private static string OverflowDomainName(string? value)
        => string.IsNullOrWhiteSpace(value)
           || value == TrainingGuideDomainOptions.FollowTrainingPlanOption
           || value == AutoDomainTask.TrainingGuideOption
            ? string.Empty
            : value;

    /// <summary>今日剩余双倍次数；读不到时按每日次数安排，多出的领取按普通奖励结算，树脂不会浪费。</summary>
    /// <param name="overflow">识别到的移涌活动。</param>
    /// <returns>剩余双倍次数。</returns>
    private static int OverflowDoubles(ResinPlanOverflowStatus overflow)
        => Math.Max(0, overflow.RemainingClaims ?? ResinPlanBudget.OverflowDailyClaims);

    /// <summary>自动秘境设置中的备选秘境；未设置或误选为“根据提升指南选择秘境”时为空字符串。</summary>
    /// <param name="domain">自动秘境参数。</param>
    /// <returns>备选秘境名。</returns>
    private static string FallbackDomainName(AutoDomainParam domain)
    {
        var name = domain.TrainingGuideFallbackDomainName;
        return string.IsNullOrWhiteSpace(name) || name == AutoDomainTask.TrainingGuideOption ? string.Empty : name;
    }

    /// <summary>判断秘境是否为祝圣秘境（圣遗物秘境）；祝圣移涌只在祝圣秘境中生效。</summary>
    /// <param name="name">秘境名。</param>
    /// <returns>是祝圣秘境时返回 true。</returns>
    private static bool IsBlessDomain(string name) => HasDomainType(name, "BlessDomain");

    /// <summary>判断秘境是否为精通秘境或炼武秘境；盛材移涌只在这两类秘境中生效。</summary>
    /// <param name="name">秘境名。</param>
    /// <returns>是精通或炼武秘境时返回 true。</returns>
    private static bool IsMaterialDomain(string name) => HasDomainType(name, "MasteryDomain", "ForgeryDomain");

    /// <summary>按 tp.json 的秘境类型判断。</summary>
    /// <param name="name">秘境名。</param>
    /// <param name="types">允许的秘境类型。</param>
    /// <returns>秘境存在且类型匹配时返回 true。</returns>
    private static bool HasDomainType(string name, params string[] types)
        => name.Length > 0
           && MapLazyAssets.Get().DomainPositionMap.TryGetValue(name, out var position)
           && position.Type is { } type && types.Contains(type);

    /// <summary>生成提升指南养成的秘境参数：活动阶段的盛材移涌与秘境养成共用，只有树脂设置不同。</summary>
    /// <param name="source">节点中的自动秘境参数。</param>
    /// <param name="usage">本步树脂设置。</param>
    /// <returns>改写后的独立副本。</returns>
    private static AutoDomainParam BuildTrainingDomainParam(AutoDomainParam source, ResinUsage usage)
    {
        var domain = Clone(source);
        domain.DomainName = AutoDomainTask.TrainingGuideOption;
        domain.TrainingGuideCalculateRunsEnabled = true;
        domain.TrainingTargetsJson = null;
        // 备选秘境改到剩余树脂阶段执行，保证承接顺序固定
        domain.TrainingGuideFallbackDomainName = string.Empty;
        domain.TrainingGuideFallbackSundaySelectedValue = string.Empty;
        domain.RewardRecognitionEnabled = true;
        domain.TargetItems = [];
        domain.ResinUsage = usage;
        return domain;
    }

    /// <summary>生成指定秘境的参数：活动阶段指定的移涌活动秘境与剩余树脂的备选秘境共用，只有秘境与树脂设置不同。</summary>
    /// <param name="source">节点中的自动秘境参数。</param>
    /// <param name="domainName">要刷取的秘境名。</param>
    /// <param name="sundaySelectedValue">周日或限时全开时的奖励序号；空字符串表示使用游戏默认入口。</param>
    /// <param name="usage">本步树脂设置。</param>
    /// <returns>改写后的独立副本。</returns>
    private static AutoDomainParam BuildManualDomainParam(AutoDomainParam source, string domainName, string sundaySelectedValue,
        ResinUsage usage)
    {
        var domain = Clone(source);
        domain.DomainName = domainName;
        domain.SundaySelectedValue = sundaySelectedValue;
        domain.TrainingGuideCalculateRunsEnabled = false;
        domain.TrainingTargetsJson = null;
        domain.TrainingGuideFallbackDomainName = string.Empty;
        domain.RewardRecognitionEnabled = true;
        domain.TargetItems = [];
        domain.ResinUsage = usage;
        return domain;
    }

    /// <summary>生成地脉花参数：活动阶段的地脉移涌与剩余树脂的地脉花共用。</summary>
    /// <param name="source">节点中的地脉花参数。</param>
    /// <param name="usage">本步树脂设置。</param>
    /// <returns>改写后的独立副本。</returns>
    private static AutoLeyLineOutcropParam BuildLeyLineParam(AutoLeyLineOutcropParam source, ResinUsage usage)
    {
        var leyLine = Clone(source);
        leyLine.ResinUsage = usage;
        leyLine.TargetItems = [];
        return leyLine;
    }

    /// <summary>生成幽境危战参数：活动阶段的紊乱爆发期与剩余树脂的危战共用。</summary>
    /// <param name="source">节点中的幽境危战参数。</param>
    /// <param name="usage">本步树脂设置。</param>
    /// <returns>改写后的独立副本。</returns>
    private static AutoStygianOnslaughtParam BuildStygianParam(AutoStygianOnslaughtParam source, ResinUsage usage)
    {
        var stygian = Clone(source);
        stygian.ResinUsage = usage;
        return stygian;
    }

    /// <summary>
    /// 按剩余预算与最新持有量生成本阶段的树脂设置；不足领取一次时记录跳过原因并返回 null。
    /// </summary>
    /// <param name="run">运行状态。</param>
    /// <param name="step">本阶段记录。</param>
    /// <param name="required">领取一次所需的最少点数。</param>
    /// <param name="build">按持有量生成树脂设置。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>树脂设置；本阶段跳过时为 null。</returns>
    private async Task<ResinUsage?> PlanUsageAsync(ResinPlanRun run, ResinPlanStep step, int required,
        Func<ResinPlanStock, ResinUsage?> build, CancellationToken ct)
    {
        // 1. 预算已不足一次领取时不再读取树脂
        if (run.Remaining is { } remaining && remaining < required)
        {
            Skip(step, ResinPlanReasons.BudgetReached, "已达到本次体力用量上限。");
            return null;
        }

        // 2. 子任务执行后重新读取持有量，避免按过期数量安排
        var stock = await GetStockAsync(run, ct);
        if (stock is null)
        {
            Skip(step, ResinPlanReasons.ResinUnknown, "未能读取当前树脂数量，跳过此步。");
            return null;
        }

        var usage = build(stock);
        if (usage is null)
        {
            Skip(step, SoloTaskReasons.ResinExhausted, ResinUsageText.Skipped);
        }

        return usage;
    }

    /// <summary>
    /// 调用子任务并记录结果；子任务的业务异常记为本阶段失败并回到主界面，取消与运行环境停止向外抛出。
    /// </summary>
    /// <param name="run">运行状态。</param>
    /// <param name="step">本阶段记录。</param>
    /// <param name="invoke">调用子任务。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task ExecuteAsync(ResinPlanRun run, ResinPlanStep step, Func<Task<ISoloTaskResult>> invoke, CancellationToken ct)
    {
        step.Executed = true;
        run.MarkStockStale();
        Logger.LogInformation("{Name}：开始{Step}", TaskName, step.Name);
        try
        {
            var result = await invoke();
            step.Kind = result.Kind;
            step.ReasonCode = result.ReasonCode;
            step.Message = result.Message;
            switch (result.Data)
            {
                case ResinClaimData claim:
                    step.Resin = claim.Resin;
                    step.Rewards = claim.Rewards;
                    break;
                case CraftCondensedResinData craft:
                    step.CraftedCount = craft.CraftedCount;
                    break;
            }
        }
        catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
        {
            // 业务异常只结束本阶段；后续阶段从主界面重新开始
            step.Kind = SoloTaskResultKind.Failed;
            step.ReasonCode = ResinPlanReasons.TaskException;
            step.Message = e.Message;
            Logger.LogWarning("{Name}：{Step}异常结束：{Message}", TaskName, step.Name, e.Message);
            await SafeReturnMainUiAsync(ct);
        }

        run.Complete(step);
        Logger.LogInformation("{Name}：{Step}结束：{Message}", TaskName, step.Name, step.Message);
    }

    /// <summary>标记阶段在开始前跳过。</summary>
    /// <param name="step">阶段记录。</param>
    /// <param name="reasonCode">原因码。</param>
    /// <param name="message">跳过原因。</param>
    private static void Skip(ResinPlanStep step, string reasonCode, string message)
    {
        step.Kind = SoloTaskResultKind.Skipped;
        step.ReasonCode = reasonCode;
        step.Message = message;
        Logger.LogInformation("{Name}：跳过{Step}：{Message}", TaskName, step.Name, message);
    }

    /// <summary>返回最新持有量；子任务执行后才重新读取。</summary>
    /// <param name="run">运行状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>持有量；读取失败时为 null。</returns>
    private async Task<ResinPlanStock?> GetStockAsync(ResinPlanRun run, CancellationToken ct)
    {
        if (!run.StockStale && run.Stock is { } cached)
        {
            return cached;
        }

        var stock = await ReadStockAsync(ct);
        if (stock is not null)
        {
            run.UpdateStock(stock);
        }

        return stock;
    }

    /// <summary>
    /// 打开大地图读取顶栏的原粹与浓缩树脂，结束后回到主界面。
    /// 浓缩树脂为 0 时游戏不显示图标；只有图标已匹配但数量识别失败时，浓缩数量记为未知。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>持有量；原粹识别失败时为 null。</returns>
    private async Task<ResinPlanStock?> ReadStockAsync(CancellationToken ct)
    {
        try
        {
            await new ReturnMainUiTask(_runtime).Start(ct);
            await new TpTask(_runtime, _config, ct).OpenBigMapUi();
            // 打开地图后等待界面稳定再截图，避免读到切换过程中的旧帧
            await _runtime.Delay(300, ct);
            using var capture = _runtime.CaptureToRectArea();
            if (ResinRecognition.RecognizeInBigMapTopBar(capture) is not { } resin)
            {
                Logger.LogWarning("{Name}：大地图原粹树脂识别失败", TaskName);
                return null;
            }

            int? condensed = resin.Condensed ?? (resin.CondensedIconRect is null ? 0 : null);
            Logger.LogInformation("{Name}：原粹树脂 {Current}/{Max}，浓缩树脂 {Condensed}", TaskName,
                resin.Current, resin.Max, condensed?.ToString() ?? "数量未识别");
            return new ResinPlanStock(resin.Current, resin.Max, condensed);
        }
        catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
        {
            Logger.LogWarning("{Name}：读取树脂失败：{Message}", TaskName, e.Message);
            return null;
        }
        finally
        {
            await SafeReturnMainUiAsync(ct);
        }
    }

    /// <summary>回到主界面；失败只记录日志，不影响后续阶段的判断。</summary>
    /// <param name="ct">取消令牌。</param>
    private async Task SafeReturnMainUiAsync(CancellationToken ct)
    {
        try
        {
            await new ReturnMainUiTask(_runtime).Start(ct);
        }
        catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
        {
            Logger.LogDebug(e, "{Name}：回到主界面失败", TaskName);
        }
    }

    /// <summary>按统一 JSON 约定复制参数，子任务的改写不影响原参数。</summary>
    /// <param name="value">原参数。</param>
    /// <returns>独立副本。</returns>
    private static T Clone<T>(T value) where T : class => SoloTaskJson.Deserialize<T>(SoloTaskJson.Serialize(value));
}
