using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
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
/// 顺序：读取树脂 → 识别活动 → 合成浓缩（可选）→ 首领 → 秘境养成 → 剩余树脂（幽境危战 → 地脉花 → 备选秘境）。
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
        // 活动优先的调度（移涌领取、爆发期暂停养成）尚未实现，目前只记录结果，并在爆发期已结束时跳过危战
        run.Data.Events = await new ResinPlanEventDetector(_runtime).DetectAsync(ct);
        await SafeReturnMainUiAsync(ct);

        // 2. 合成浓缩树脂（可选），为首领预留原粹
        if (param.CraftCondensedFirst)
        {
            await RunCraftAsync(param, run, ct);
        }

        // 3. 首领讨伐：固定次数，只用原粹
        if (param.Boss.Enabled)
        {
            await RunBossAsync(param, run, ct);
        }

        // 4. 秘境养成：根据提升指南选择秘境，可连续刷多个候选秘境
        if (param.Domain.Enabled)
        {
            await RunTrainingDomainAsync(param, run, ct);
        }

        // 5. 剩余树脂依次承接：幽境危战（紊乱爆发期结束时由任务自行跳过）→ 地脉花 → 备选秘境
        if (param.Stygian.Enabled)
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

        // 6. 汇总奖励与消耗
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
    private async Task RunCraftAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
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

        // 首领只能使用原粹，合成前先为其留出原粹；首领刷至耗尽时不合成
        var reserve = ResinPlanBudget.BossReserve(param.Boss.Enabled, ResinPlanBudget.BossRunLimit(param.Boss.Param.ResinUsage));
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

        var domain = Clone(param.Domain.Param);
        domain.DomainName = AutoDomainTask.TrainingGuideOption;
        domain.TrainingGuideCalculateRunsEnabled = true;
        domain.TrainingTargetsJson = null;
        domain.TrainingGuideFallbackDomainName = string.Empty;
        domain.TrainingGuideFallbackSundaySelectedValue = string.Empty;
        domain.RewardRecognitionEnabled = true;
        domain.TargetItems = [];
        domain.ResinUsage = usage;
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

        var stygian = Clone(stygianParam);
        stygian.ResinUsage = usage;
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

        var leyLine = Clone(leyLineParam);
        leyLine.ResinUsage = usage;
        leyLine.TargetItems = [];
        await ExecuteAsync(run, step, async () => await new AutoLeyLineOutcropTask(_runtime, _config).ExecuteAsync(leyLine, ct), ct);
    }

    /// <summary>剩余树脂：自动秘境设置中的备选秘境；未设置时保留剩余树脂。</summary>
    private async Task RunFallbackDomainAsync(ResinPlanParam param, ResinPlanRun run, CancellationToken ct)
    {
        var domainParam = param.Domain.Param;
        var fallback = domainParam.TrainingGuideFallbackDomainName;
        if (string.IsNullOrWhiteSpace(fallback) || fallback == AutoDomainTask.TrainingGuideOption)
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

        var domain = Clone(domainParam);
        domain.DomainName = fallback;
        domain.SundaySelectedValue = domainParam.TrainingGuideFallbackSundaySelectedValue;
        domain.TrainingGuideCalculateRunsEnabled = false;
        domain.TrainingTargetsJson = null;
        domain.TrainingGuideFallbackDomainName = string.Empty;
        domain.RewardRecognitionEnabled = true;
        domain.TargetItems = [];
        domain.ResinUsage = usage;
        await ExecuteAsync(run, step, async () => await new AutoDomainTask(_runtime, _config).ExecuteAsync(domain, ct), ct);
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
