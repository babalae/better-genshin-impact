using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 参数中文名、节点结果解释与运行状态归类的展示约定，不依赖 WPF。
/// </summary>
public sealed class PuloniaTaskResultDisplayTests
{
    /// <summary>新结果结构的奖励与树脂报告各显示一次，树脂原始 JSON 不重复展示。</summary>
    [Fact]
    public void ResinResult_ShowsNestedRewardsAndFormattedReport()
    {
        var data = new ResinClaimData
        {
            Rewards = RewardSummary.From(new Dictionary<string, int> { ["摩拉"] = 60000 }),
            Resin = new ResinUsageReport
            {
                Mode = ResinUsageMode.RunCount, ClaimCount = 4, TargetCount = 6,
                Claims = new() { Condensed = 2, Original = 2 }, Consumed = new() { Condensed = 2, Original = 80 },
                StopReason = ResinStopReason.ResinExhausted
            }
        };
        var node = CreateNode("solo.auto_domain", JObject.Parse(SoloTaskJson.Serialize(data)));
        Assert.Equal("摩拉 ×60000", Field(node, "奖励"));
        Assert.Equal("领奖 4/6 次（浓缩 2、原粹 80），树脂不足，提前结束。", Field(node, "树脂使用"));
        Assert.Equal(2, node.ResultFields.Count);
    }

    /// <summary>体力计划结果展示合并奖励、合计树脂、预算与逐步说明，不重复展示原始阶段 JSON。</summary>
    [Fact]
    public void ResinPlanResult_ShowsBudgetAndSteps()
    {
        var data = new ResinPlanData
        {
            Rewards = RewardSummary.From(new Dictionary<string, int> { ["教导"] = 3 }),
            Resin = new ResinUsageReport
            {
                Mode = ResinUsageMode.Exhaust, ClaimCount = 3, Claims = new() { Original = 3 },
                Consumed = new() { Original = 60 }, StopReason = ResinStopReason.ResinExhausted
            },
            Budget = new ResinPlanBudgetReport { MaxResin = 0, StartOriginal = 60, StartCondensed = 0, SpentPoints = 60 },
            Steps =
            [
                new ResinPlanStep
                {
                    Name = "秘境养成", Target = "根据提升指南选择秘境", Executed = true, Kind = SoloTaskResultKind.Succeeded,
                    Message = "秘境流程已结束", RewardsRecognized = true,
                    Resin = new ResinUsageReport { ClaimCount = 3 },
                    Rewards = RewardSummary.From(new Dictionary<string, int> { ["教导"] = 3 })
                },
                new ResinPlanStep
                {
                    Name = "地脉花", Executed = true, Kind = SoloTaskResultKind.Unverified, Message = "地脉花流程已结束",
                    Resin = new ResinUsageReport { ClaimCount = 1 }
                },
                new ResinPlanStep { Name = "备选秘境", Message = "未设置备选秘境，剩余树脂保留。" }
            ]
        };

        var node = CreateNode("solo.resin_plan", JObject.Parse(SoloTaskJson.Serialize(data)));

        Assert.Equal("教导 ×3", Field(node, "奖励"));
        Assert.Equal("领奖 3 次（原粹 60），树脂已用完。", Field(node, "树脂使用"));
        Assert.Equal("全部可用树脂；开始时原粹 60、浓缩 0；领奖共消耗 60 点", Field(node, "体力预算"));
        Assert.Equal("成功（根据提升指南选择秘境）：秘境流程已结束；奖励：教导 ×3", Field(node, "第 1 步 · 秘境养成"));
        Assert.Equal("已执行，未核验：地脉花流程已结束；此任务暂不识别奖励", Field(node, "第 2 步 · 地脉花"));
        Assert.Equal("跳过：未设置备选秘境，剩余树脂保留。", Field(node, "第 3 步 · 备选秘境"));
        Assert.Equal(6, node.ResultFields.Count);
    }

    /// <summary>测试使用的固定时间。</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>编辑器、一条龙卡片和历史详情共用一份中文名；冲突项取信息更完整的名称，未知键保持原样。</summary>
    [Fact]
    public void FieldNames_AreSharedByEditorsAndHistory()
    {
        Assert.Equal("程序或命令", PuloniaTaskFieldNames.GetDisplayName("file_name"));
        Assert.Equal("计算输入", PuloniaTaskFieldNames.GetDisplayName("values"));
        Assert.Equal("合成后保留原粹树脂", PuloniaTaskFieldNames.GetDisplayName("min_resin_to_keep"));
        Assert.Equal("树脂使用", PuloniaTaskFieldNames.GetDisplayName("resin_usage"));
        foreach (var removed in new[] { "specify_resin_use", "resin_priority_list", "original_resin_use_count",
            "original_resin20_use_count", "original_resin40_use_count", "condensed_resin_use_count",
            "transient_resin_use_count", "fragile_resin_use_count", "domain_round_num", "use_transient_resin",
            "use_fragile_resin", "is_resin_exhaustion_mode", "open_mode_count_min", "is_go_to_synthesizer",
            "specify_run_count", "run_count", "count", "round_count" })
            Assert.Equal(removed, PuloniaTaskFieldNames.GetDisplayName(removed));
        Assert.Equal("extension_key", PuloniaTaskFieldNames.GetDisplayName("extension_key"));

        var stringSchema = new JObject { ["type"] = "string" };
        Assert.Equal("程序或命令", new PuloniaTaskParameterFieldViewModel("file_name", stringSchema, null, false, true).DisplayName);
        // 一条龙子设置的帕斯卡命名先转为蛇形再取中文名，Schema 标题优先。
        Assert.Equal("战斗队伍", new PuloniaOneDragonParameterFieldViewModel("TeamNames", stringSchema, null).DisplayName);
        Assert.Equal("自定义标题", new PuloniaOneDragonParameterFieldViewModel("TeamNames",
            new JObject { ["type"] = "string", ["title"] = "自定义标题" }, null).DisplayName);

        var node = CreateNode("javascript", new JObject { ["file_name"] = "cmd.exe" });
        Assert.Equal("cmd.exe", Field(node, "程序或命令"));
    }

    /// <summary>新版路线结果按固定顺序解释失败原因、复活与拾取统计，缺失值不冒充成功或零。</summary>
    [Fact]
    public void PathingResult_ExplainsFailureReasonAndPickups()
    {
        var failed = CreateNode("pathing", new JObject
        {
            ["is_success"] = false, ["success_fight_count"] = 1, ["collected_items"] = new JObject()
        });
        Assert.Equal("否", Field(failed, "路线完整执行"));
        Assert.Equal("未提供", Field(failed, "失败原因"));
        Assert.Equal("未统计", Field(failed, "复活次数"));
        Assert.Equal("已统计，未拾取物品", Field(failed, "拾取物品"));

        var succeeded = CreateNode("pathing", new JObject
        {
            ["is_success"] = true, ["revive_count"] = 0, ["collected_items"] = new JObject { ["甜甜花"] = 3 },
            ["collection_material"] = "甜甜花"
        });
        Assert.Equal(["路线完整执行", "成功战斗次数", "失败原因", "复活次数", "拾取物品", "识别的材料或类别"],
            succeeded.ResultFields.Select(field => field.Label));
        Assert.Equal("未返回（可能在结果产生前失败或取消）", Field(succeeded, "成功战斗次数"));
        Assert.Equal("无", Field(succeeded, "失败原因"));
        Assert.Equal("0", Field(succeeded, "复活次数"));
        Assert.Equal("甜甜花 × 3", Field(succeeded, "拾取物品"));
    }

    /// <summary>一条龙行状态按完整地址前缀汇总，优先级为执行中、失败、待核验、完成。</summary>
    [Fact]
    public void SubtreeStatus_UsesAddressPrefixAndPriority()
    {
        const string row = "plan/root/row";
        Assert.Equal(PuloniaTaskSubtreeStatus.NotRun, PuloniaTaskRunClassifier.ClassifySubtree(null, "root", "row"));
        Assert.Equal(PuloniaTaskSubtreeStatus.NotRun, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result("plan/root/row2", PuloniaTaskNodeStatus.Failed)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Running, Classify(PuloniaTaskRunStatus.Cancelling, row + "/child",
            Result(row + "/other", PuloniaTaskNodeStatus.Failed)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Failed, Classify(PuloniaTaskRunStatus.Failed, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded, PuloniaTaskOutcomeKind.ExecutedUnverified),
            Result(row + "/child", PuloniaTaskNodeStatus.TimedOut)));
        Assert.Equal(PuloniaTaskSubtreeStatus.NeedsVerification, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded, PuloniaTaskOutcomeKind.ExecutedUnverified)));
        Assert.Equal(PuloniaTaskSubtreeStatus.Completed, Classify(PuloniaTaskRunStatus.Succeeded, null,
            Result(row, PuloniaTaskNodeStatus.Succeeded)));
        Assert.Equal(PuloniaTaskSubtreeStatus.SkippedOrStopped, Classify(PuloniaTaskRunStatus.Cancelled, null,
            Result(row, PuloniaTaskNodeStatus.Skipped)));

        PuloniaTaskSubtreeStatus Classify(PuloniaTaskRunStatus status, string? current, params PuloniaTaskNodeResult[] results)
            => PuloniaTaskRunClassifier.ClassifySubtree(row, status, current, results);
    }

    /// <summary>核验提示看任一尝试，重放确认只看每个节点最后一次尝试与运行是否待处理。</summary>
    [Fact]
    public void UncertainOutcomes_DistinguishAnyAttemptFromLastAttempt()
    {
        var retried = new[]
        {
            Result("plan/root/a", PuloniaTaskNodeStatus.Failed, PuloniaTaskOutcomeKind.PartiallySucceeded),
            Result("plan/root/a", PuloniaTaskNodeStatus.Succeeded)
        };
        Assert.True(PuloniaTaskRunClassifier.HasUncertainAttempt(retried));
        Assert.False(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.Failed, retried));
        Assert.True(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.NeedsAttention, retried));
        // 最后一次尝试仍是部分成功时需要确认。
        Assert.True(PuloniaTaskRunClassifier.RequiresReplayConfirmation(PuloniaTaskRunStatus.Failed, [retried[1], retried[0]]));

        Assert.Equal(PuloniaTaskRunStatusCategory.Running, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Cancelling));
        Assert.Equal(PuloniaTaskRunStatusCategory.Failed, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.TimedOut));
        Assert.Equal(PuloniaTaskRunStatusCategory.Attention, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Interrupted));
        Assert.Equal(PuloniaTaskRunStatusCategory.Neutral, PuloniaTaskRunClassifier.Categorize(PuloniaTaskRunStatus.Queued));
        Assert.True(PuloniaTaskRunClassifier.IsActive(PuloniaTaskRunStatus.Queued));
        Assert.False(PuloniaTaskRunClassifier.IsActive(PuloniaTaskRunStatus.Interrupted));
    }

    /// <summary>建立一次节点尝试结果。</summary>
    private static PuloniaTaskNodeResult Result(string address, PuloniaTaskNodeStatus status,
        PuloniaTaskOutcomeKind? outcome = null)
        => new(address, "节点", "solo.test", 1, status, "结果", Now, Now, outcomeKind: outcome);

    /// <summary>建立没有快照输入的节点详情。</summary>
    private static PuloniaTaskNodeResultViewModel CreateNode(string taskType, JObject data) => new(
        new PuloniaTaskNodeResult("plan/node", "节点", taskType, 1, PuloniaTaskNodeStatus.Succeeded, "结果", Now, Now, data),
        null, "类型", []);

    /// <summary>读取指定标签的唯一结果字段文本。</summary>
    private static string Field(PuloniaTaskNodeResultViewModel node, string label)
        => node.ResultFields.Single(field => field.Label == label).Value;
}
