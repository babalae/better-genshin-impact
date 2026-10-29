using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>不启动游戏的文件级 CD 时间、资源身份、失败结算及重启回归。</summary>
public sealed class PuloniaCollectionCooldownTests : IDisposable
{
    /// <summary>本测试独占的临时资源和状态目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>材料目录和别名使用明确的规则，不把矿物外形误当作日期刷新。</summary>
    [Theory]
    [InlineData("落落莓", "rolling", 46, 0)]
    [InlineData("蒲公英", "rolling", 46, 0)]
    [InlineData("帕蒂莎兰", "rolling", 46, 0)]
    [InlineData("清水玉", "rolling", 46, 0)]
    [InlineData("甜甜花", "rolling", 12, 0)]
    [InlineData("蘑菇", "rolling", 24, 0)]
    [InlineData("晶核", "daily", 0, 0)]
    [InlineData("铁块", "calendar_days", 0, 1)]
    [InlineData("白铁块", "calendar_days", 0, 2)]
    [InlineData("水晶块", "calendar_days", 0, 3)]
    [InlineData("宝箱", "once", 0, 0)]
    [InlineData("圣遗物", "disabled", 0, 0)]
    [InlineData("未知材料", "disabled", 0, 0)]
    public void DirectoryRules_AreExplicit(string material, string mode, double hours, int days)
    {
        var policy = Resolve(material);
        Assert.Equal(mode, PuloniaCollectionCooldown.FormatMode(policy.Mode));
        Assert.Equal(hours, policy.Hours);
        Assert.Equal(days, policy.Days);
    }

    /// <summary>
    /// 官方脚本仓库的实际目录结构（2026-10-28 提交 d775a4b 抽样）：魔物与锄地路线由类别目录兜底为每日 04:00，
    /// 材料目录仍优先；仓库目录名的别名与混合类别按规则表识别，不访问网络。
    /// </summary>
    [Theory]
    [InlineData("pathing/敌人与魔物/丘丘人射手/丘丘人射手@曦/01.json", "daily", "敌人与魔物", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/敌人与魔物/盗宝团/盗宝团@作者/01.json", "daily", "盗宝团", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/锄地专区/精英400@汐/1-精英/01.json", "daily", "锄地专区", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/矿物/魔晶矿/01.json", "calendar_days", "魔晶矿", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/其他/提瓦特钓鱼指南/蒙德/01.json", "rolling", "提瓦特钓鱼指南", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/其他/木材伐木点/01.json", "disabled", "木材伐木点", PuloniaCollectionCooldownRuleSource.Mixed)]
    [InlineData("pathing/地方特产/蒙德/落落莓/01.json", "rolling", "落落莓", PuloniaCollectionCooldownRuleSource.Directory)]
    [InlineData("pathing/食材与炼金/松茸/01.json", "disabled", null, PuloniaCollectionCooldownRuleSource.Unmatched)]
    public void RepositoryDirectories_AreRecognized(string relativePath, string mode, string? material,
        PuloniaCollectionCooldownRuleSource source)
    {
        var policy = PuloniaCollectionCooldown.Resolve(null,
            new ScriptResourceReference { RepositoryId = "rep_aaaaaaaaaaaaaaaa", RelativePath = relativePath }, new JObject());
        Assert.Equal(mode, PuloniaCollectionCooldown.FormatMode(policy.Mode));
        Assert.Equal(material, policy.MaterialName);
        Assert.Equal(source, policy.Source);
        // 从账本中的小写文件身份重新识别得到同一规则，目录引用计数与运行时判定一致
        Assert.Equal(policy.Description,
            PuloniaCollectionCooldown.ResolveFromFileIdentity(policy.FileIdentity, null).Description);
    }

    /// <summary>状态判定：没有记录、CD 中、恰好到期、一次性完成、未识别与关闭限制各自得到不同状态。</summary>
    [Fact]
    public void Status_DistinguishesStates()
    {
        var success = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var policy = Resolve("落落莓");
        var entry = new PuloniaTaskLedgerEntry { EffectKey = PuloniaCollectionCooldown.GetEffectKey(policy),
            ScopeKey = PuloniaCollectionCooldown.BuildScopeKey(), OccurredAt = success, NextEligibleAt = success.AddHours(46) };
        Assert.Equal(PuloniaCollectionCooldownState.Ready, PuloniaCollectionCooldown.GetStatus(policy, null, success, 480).State);
        var cooling = PuloniaCollectionCooldown.GetStatus(policy, entry, success.AddHours(1), 480);
        Assert.Equal(PuloniaCollectionCooldownState.CoolingDown, cooling.State);
        Assert.Equal(PuloniaCollectionCooldown.ReasonCooldownActive, PuloniaCollectionCooldown.GetReasonCode(cooling));
        Assert.Equal(PuloniaCollectionCooldownState.Ready, PuloniaCollectionCooldown.GetStatus(policy, entry, success.AddHours(46), 480).State);
        var once = PuloniaCollectionCooldown.GetStatus(Resolve("宝箱"), entry, success.AddYears(1), 480);
        Assert.Equal(PuloniaCollectionCooldownState.Completed, once.State);
        Assert.Equal(PuloniaCollectionCooldown.ReasonCooldownCompleted, PuloniaCollectionCooldown.GetReasonCode(once));
        Assert.Equal(PuloniaCollectionCooldownState.Unmatched, PuloniaCollectionCooldown.GetStatus(Resolve("未知材料"), entry, success, 480).State);
        Assert.Equal(PuloniaCollectionCooldownState.NotChecked,
            PuloniaCollectionCooldown.GetStatus(Resolve("落落莓", new JObject { ["mode"] = "disabled" }), entry, success, 480).State);
        Assert.Equal("不检查", PuloniaCollectionCooldown.DescribeSettings(new JObject { ["mode"] = "disabled" }));
        Assert.Equal("每日 04:00", PuloniaCollectionCooldown.DescribeSettings(new JObject { ["mode"] = "daily" }));
        Assert.Equal("自动识别", PuloniaCollectionCooldown.DescribeSettings(PuloniaCollectionCooldown.CreateDefaults()));
    }

    /// <summary>仓库材料从原始路径识别，版本变化和不同缓存位置不改变文件身份。</summary>
    [Fact]
    public void RepositoryIdentity_UsesOriginalPathAndIgnoresRevision()
    {
        var reference = new ScriptResourceReference { RepositoryId = "rep_aaaaaaaaaaaaaaaa", RelativePath = "pathing/落落莓/蒙德/A.json", ApprovedRevision = new string('1', 40) };
        var first = PuloniaCollectionCooldown.Resolve(Path.Combine(_directory, "cache", "A.json"), reference, new JObject());
        var second = PuloniaCollectionCooldown.Resolve(Path.Combine(_directory, "other", "A.json"), reference.WithRevision(new string('2', 40)), new JObject());
        Assert.Equal("落落莓", first.MaterialName);
        Assert.Equal(first.FileIdentity, second.FileIdentity);
        Assert.Equal(PuloniaCollectionCooldown.GetEffectKey(first), PuloniaCollectionCooldown.GetEffectKey(second));
        // 资源键直接使用文件身份原文，历史中能看出是哪个文件。
        Assert.Equal("pathing.file:repository:rep_aaaaaaaaaaaaaaaa/pathing/落落莓/蒙德/a.json", PuloniaCollectionCooldown.GetEffectKey(first));
        var other = PuloniaCollectionCooldown.Resolve(null, new ScriptResourceReference { RepositoryId = "rep_bbbbbbbbbbbbbbbb", RelativePath = reference.RelativePath }, new JObject());
        Assert.NotEqual(PuloniaCollectionCooldown.GetEffectKey(first), PuloniaCollectionCooldown.GetEffectKey(other));
    }

    /// <summary>以最近匹配的目录为准；非精确材料名不进行包含匹配。</summary>
    [Fact]
    public void NearestDirectory_WinsWithoutFuzzyMatching()
    {
        var policy = PuloniaCollectionCooldown.Resolve(Path.Combine(_directory, "落落莓", "薄荷", "A.json"), null, new JObject());
        Assert.Equal("薄荷", policy.MaterialName);
        Assert.Equal(12, policy.Hours);
        Assert.Null(Resolve("落落莓备份").MaterialName);
    }

    /// <summary>服务器四点、午夜保守估算及周一边界按 UTC 转换，边界本身属于新周期。</summary>
    [Fact]
    public void CalendarRules_RespectServerBoundaries()
    {
        var success = new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(success.AddHours(46).ToUniversalTime(), PuloniaCollectionCooldown.GetNextEligibleAt(Resolve("落落莓"), success, 480));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime(),
            PuloniaCollectionCooldown.GetNextEligibleAt(Resolve("水晶块"), success, 480));
        var beforeReset = new DateTimeOffset(2026, 10, 10, 3, 59, 59, TimeSpan.FromHours(8));
        var boundary = beforeReset.AddSeconds(1);
        Assert.Equal(boundary.ToUniversalTime(), PuloniaCollectionCooldown.GetNextEligibleAt(Resolve("晶核"), beforeReset, 480));
        Assert.Equal(boundary.AddDays(1).ToUniversalTime(), PuloniaCollectionCooldown.GetNextEligibleAt(Resolve("晶核"), boundary, 480));
        var weekly = Resolve("未知", new JObject { ["mode"] = "weekly", ["reset_weekday"] = 1, ["reset_hour"] = 4 });
        var monday = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(monday.AddDays(7).ToUniversalTime(), PuloniaCollectionCooldown.GetNextEligibleAt(weekly, monday, -300));
    }

    /// <summary>时间范围、缺失相对时长及不合法重置选项在准备前被拒绝。</summary>
    [Theory]
    [InlineData("{\"mode\":\"rolling\",\"hours\":0}")]
    [InlineData("{\"mode\":\"rolling\"}")]
    [InlineData("{\"mode\":\"calendar_days\",\"days\":0}")]
    [InlineData("{\"mode\":\"daily\",\"reset_hour\":24}")]
    [InlineData("{\"mode\":\"weekly\",\"reset_weekday\":0}")]
    public void InvalidSettings_AreRejected(string json)
        => Assert.Throws<PuloniaTaskValidationException>(() => PuloniaCollectionCooldown.ValidateSettings(JObject.Parse(json), "测试"));

    /// <summary>规则变化只重算执行资格，不改写历史估计；恰好到期允许执行。</summary>
    [Fact]
    public void Eligibility_RecalculatesRuleAndAllowsExactBoundary()
    {
        var request = new PuloniaTaskRequest();
        var success = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var original = Resolve("落落莓");
        var storedNext = success.AddHours(46);
        var entry = new PuloniaTaskLedgerEntry { EffectKey = PuloniaCollectionCooldown.GetEffectKey(original),
            ScopeKey = PuloniaCollectionCooldown.BuildScopeKey(), OccurredAt = success, NextEligibleAt = storedNext };
        var shortened = Resolve("落落莓", new JObject { ["mode"] = "rolling", ["hours"] = 12 });
        var status = PuloniaCollectionCooldown.GetStatus(shortened, entry, success.AddHours(12), request.ServerUtcOffsetMinutes);
        Assert.False(status.IsSkipped);
        var data = PuloniaCollectionCooldown.Describe(shortened, status.LastSuccessAt, status.NextEligibleAt);
        Assert.Equal(success.AddHours(12).ToString("O"), data.Value<string>("collection_next_eligible_at"));
        Assert.Equal(storedNext, entry.NextEligibleAt);
        Assert.True(PuloniaCollectionCooldown.GetStatus(original, entry, success.AddHours(12), request.ServerUtcOffsetMinutes).IsSkipped);
        var once = Resolve("落落莓", new JObject { ["mode"] = "once" });
        Assert.True(PuloniaCollectionCooldown.GetStatus(once, entry, success.AddYears(10), request.ServerUtcOffsetMinutes).IsSkipped);
    }

    /// <summary>不同文件独立，同文件跨计划和重复节点共享成功记录，重启后仍跳过。</summary>
    [Fact]
    public async Task Success_IsSharedAcrossPlansAndSurvivesRestart()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var a = CreateTask("落落莓", "A.json");
        var b = CreateTask("落落莓", "B.json");
        var plan = await store.SavePlanAsync(Plan(a, new PuloniaTask { Name = "重复 A", TaskType = "pathing", Path = a.Path }, b));
        var other = await store.SavePlanAsync(Plan(new PuloniaTask { Name = "跨计划 A", TaskType = "pathing", Path = a.Path }));
        await using (var service = Service(store, executor))
        {
            var run = await Run(service, plan);
            Assert.Equal(new[] { PuloniaTaskNodeStatus.Succeeded, PuloniaTaskNodeStatus.Skipped, PuloniaTaskNodeStatus.Succeeded }, run.NodeResults.Select(result => result.Status));
            Assert.Equal(2, executor.Executions);
            Assert.Equal(2, (await service.ListLedgerAsync()).Count);
            var snapshot = PuloniaTaskJson.ReadSnapshot(run.SnapshotJson);
            Assert.Equal(PuloniaCollectionCooldownMode.Rolling, snapshot.RootTask.Children[0].CollectionCooldown!.Mode);
            Assert.Equal(46, snapshot.RootTask.Children[0].CollectionCooldown!.Hours);
            // 规则对象的模式与来源仍按原有字符串写入快照
            Assert.Contains("\"mode\": \"rolling\"", run.SnapshotJson);
            Assert.Contains("\"source\": \"目录自动识别\"", run.SnapshotJson);
            // 同一文件第二次出现时因 CD 跳过，原因码可区分其他跳过
            Assert.Equal(PuloniaCollectionCooldown.ReasonCooldownActive, run.NodeResults[1].ReasonCode);
            Assert.True(PuloniaCollectionCooldown.IsCooldownSkip(run.NodeResults[1]));
        }
        await using (var restarted = Service(store, executor))
        {
            var run = await Run(restarted, other);
            Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single(run.NodeResults).Status);
            Assert.Contains("CD", run.NodeResults[0].Message);
            Assert.Equal(2, executor.Executions);
        }
    }

    /// <summary>失败、提前放弃及旧结束标记不生成 CD，后续完整成功仍可重新执行。</summary>
    [Theory]
    [InlineData(PuloniaTaskOutcomeKind.Failed)]
    [InlineData(PuloniaTaskOutcomeKind.ExecutedUnverified)]
    public async Task IncompleteFile_DoesNotWriteCooldown(PuloniaTaskOutcomeKind kind)
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor { Execute = (_, _) => Task.FromResult(new PuloniaTaskOutcome(kind, "提前放弃")) };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor);
        Assert.Equal(PuloniaTaskRunStatus.Failed, (await Run(service, plan)).Status);
        Assert.Empty(await service.ListLedgerAsync());
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
        Assert.Single(await service.ListLedgerAsync());
    }

    /// <summary>提前结束的失败对象不能写 CD；后续成功对象仍可正常结算。</summary>
    [Fact]
    public async Task IncompleteReturn_DoesNotWriteCooldown()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor { Execute = (_, _) => Task.FromResult(PuloniaPathingTaskExecutor.CreateOutcome(new PathingResult(false, errorMessage: "提前放弃"), null)) };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor);
        var failedRun = await Run(service, plan);
        Assert.Equal(PuloniaTaskRunStatus.Failed, failedRun.Status);
        Assert.Equal("提前放弃", failedRun.NodeResults[0].Data.Value<string>("error_message"));
        Assert.Empty(await service.ListLedgerAsync());
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
        Assert.Single(await service.ListLedgerAsync());
    }

    /// <summary>关闭限制仍保存成功时间，恢复规则或改变时长时从该事实重算。</summary>
    [Fact]
    public async Task RuleChanges_RecalculateExistingSuccess()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var task = CreateTask("未知材料", "A.json", new JObject { ["mode"] = "disabled" });
        var plan = await store.SavePlanAsync(Plan(task));
        await using var service = Service(store, executor);
        await Run(service, plan);
        Assert.Null(Assert.Single(await service.ListLedgerAsync()).NextEligibleAt);
        plan.RootTask.Children[0].Settings[PuloniaSettingCatalog.CollectionCooldownKey] = new JObject { ["mode"] = "rolling", ["hours"] = 24 };
        plan = await store.SavePlanAsync(plan);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single((await Run(service, plan)).NodeResults).Status);
        plan.RootTask.Children[0].Settings[PuloniaSettingCatalog.CollectionCooldownKey] = new JObject { ["mode"] = "disabled" };
        plan = await store.SavePlanAsync(plan);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, Assert.Single((await Run(service, plan)).NodeResults).Status);
        Assert.Equal(2, executor.Executions);
    }

    /// <summary>未知目录不限制运行，但成功时间会为后续手动规则保留。</summary>
    [Fact]
    public async Task UnknownMaterial_RecordsSuccessWithoutLimitingExecution()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var plan = await store.SavePlanAsync(Plan(CreateTask("未知材料", "A.json")));
        await using var service = Service(store, executor);
        await Run(service, plan);
        var second = await Run(service, plan);
        Assert.Equal(2, executor.Executions);
        Assert.Contains("未识别刷新规则", second.NodeResults[0].Data.Value<string>("collection_rule"));
        Assert.All(await service.ListLedgerAsync(), entry => Assert.Null(entry.NextEligibleAt));
    }

    /// <summary>整个文件一次性规则持久保存，重复执行被跳过。</summary>
    [Fact]
    public async Task OneTimeFile_IsSkippedAfterSuccess()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var plan = await store.SavePlanAsync(Plan(CreateTask("宝箱", "A.json")));
        await using var service = Service(store, executor);
        await Run(service, plan);
        var second = await Run(service, plan);
        Assert.Equal(1, executor.Executions);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, second.NodeResults[0].Status);
        Assert.Contains("一次性", second.NodeResults[0].Message);
    }

    /// <summary>
    /// 运行预览与实际运行一致：同一文件第二次出现预测为本次重复；成功后再次预览为 CD 中，清除记录后恢复可执行。
    /// 预览不写账本，清除会通知账本变化。
    /// </summary>
    [Fact]
    public async Task Preview_MatchesRunAndClearRestoresExecution()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var a = CreateTask("落落莓", "A.json");
        var plan = await store.SavePlanAsync(Plan(a, new PuloniaTask { Name = "重复 A", TaskType = "pathing", Path = a.Path },
            CreateTask("未知材料", "B.json")));
        await using var service = Service(store, executor);
        var request = new PuloniaTaskRequest { PlanId = plan.Id };

        var preview = await service.PreviewAsync(request);
        Assert.Equal(new[] { PuloniaTaskPreviewOutcome.Execute, PuloniaTaskPreviewOutcome.RepeatedInRun, PuloniaTaskPreviewOutcome.Execute },
            preview.Items.Select(item => item.Outcome));
        Assert.Equal(1, preview.UnmatchedCount);
        Assert.Empty(await service.ListLedgerAsync());

        await Run(service, plan);
        var after = await service.PreviewAsync(request);
        Assert.Equal(PuloniaTaskPreviewOutcome.CoolingDown, after.Items[0].Outcome);
        Assert.Equal(2, after.CooldownSkipCount);

        var changed = 0;
        service.LedgerChanged += (_, _) => changed++;
        var key = new PuloniaCollectionCooldownKey(PuloniaCollectionCooldown.BuildScopeKey(),
            PuloniaCollectionCooldown.GetEffectKey(after.Items[0].Policy!));
        Assert.Equal(1, await service.ClearCollectionCooldownAsync([key]));
        Assert.Equal(1, changed);
        Assert.Single(await service.ListLedgerAsync());
        Assert.Equal(PuloniaTaskPreviewOutcome.Execute, (await service.PreviewAsync(request)).Items[0].Outcome);
    }

    /// <summary>成功结果与账本在下一节点执行前已一起落盘，后续取消不会撤销成功文件。</summary>
    [Fact]
    public async Task SuccessAndLedger_ArePersistedBeforeAdvancing()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var enteredSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FileExecutor { Execute = async (task, ct) =>
        {
            if (Path.GetFileName(task.Path) == "B.json")
            {
                enteredSecond.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return Completed();
        } };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json"), CreateTask("落落莓", "B.json")));
        await using var service = Service(store, executor);
        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await enteredSecond.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var state = (await store.LoadStateAsync()).State;
        var result = Assert.Single(state.ActiveRun!.NodeResults);
        var entry = Assert.Single(state.Ledger);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, result.Status);
        Assert.Equal(result.FinishedAt, entry.OccurredAt);
        Assert.Single(state.ActiveRun.ConfirmedEffects);
        await service.CancelAsync(id);
        await service.WaitForCompletionAsync(id);
        Assert.Single(await service.ListLedgerAsync());
    }

    /// <summary>文件成功的确认事实随历史归档，重启后仍可查看；查询结果是独立副本，历史文件无 BOM。</summary>
    [Fact]
    public async Task ConfirmedEffect_IsArchivedAndSurvivesRestart()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        string runId;
        await using (var service = Service(store, new FileExecutor()))
        {
            var run = await Run(service, plan);
            runId = run.RunId;
            Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
            Assert.True(run.IsHistorical);
            var effect = Assert.Single(run.ConfirmedEffects);
            Assert.Equal(1, effect.Attempt);
            Assert.Equal(run.NodeResults[0].TaskAddress, effect.TaskAddress);
            Assert.Single(await service.ListLedgerAsync());

            // 查询返回对象的修改不能污染运行内部已落盘的确认事实
            effect.Entry.Evidence.Data["probe"] = true;
            Assert.Null((await service.GetRunAsync(runId)).ConfirmedEffects[0].Entry.Evidence.Data["probe"]);
        }
        await using var restarted = Service(store, new FileExecutor());
        var restored = await restarted.GetRunAsync(runId);
        Assert.Single(restored.ConfirmedEffects);
        var history = Assert.Single(await store.ListHistoryAsync());
        Assert.Single(history.ConfirmedEffects);
        var path = Path.Combine(_directory, "state", "history", plan.Id, history.RunId + ".json");
        Assert.False((await File.ReadAllBytesAsync(path)).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Contains(new PuloniaTaskRunItemViewModel(restored).ConfirmedEffectFields,
            field => field.Label.StartsWith("路线 CD 记录") && field.Value.Contains("资源：pathing.file:"));
    }

    /// <summary>提交后修改计划不会改变已固定的采集规则，未成功的排队文件也没有提前占用。</summary>
    [Fact]
    public async Task QueuedSnapshot_KeepsOriginalRule()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var blocking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FileExecutor { Execute = async (task, ct) =>
        {
            if (Path.GetFileName(task.Path) == "block.json") { blocking.SetResult(); await release.Task.WaitAsync(ct); }
            return Completed();
        } };
        var first = await store.SavePlanAsync(Plan(CreateTask("落落莓", "block.json")));
        var second = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json", new JObject { ["mode"] = "rolling", ["hours"] = 5 })));
        await using var service = Service(store, executor);
        var firstId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = first.Id });
        await blocking.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var secondId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = second.Id });
        Assert.Empty(await service.ListLedgerAsync());
        second.RootTask.Children[0].Settings[PuloniaSettingCatalog.CollectionCooldownKey] = new JObject { ["mode"] = "disabled" };
        await store.SavePlanAsync(second);
        release.SetResult();
        await service.WaitForCompletionAsync(firstId);
        var run = await service.WaitForCompletionAsync(secondId);
        var fact = Assert.Single(run.ConfirmedEffects).Entry;
        Assert.Equal(TimeSpan.FromHours(5), fact.NextEligibleAt - fact.OccurredAt);
    }

    /// <summary>
    /// 世界拥有者字段删除后的作用域规则：未切换账号的运行（普通计划不论选择哪份账号资料）都代表当前登录账号，
    /// 共用默认作用域的采集 CD；切换到指定账号时才按核验 UID 隔离（见 <see cref="PuloniaGenshinAccountTests"/>）。
    /// </summary>
    [Fact]
    public async Task UnswitchedRuns_ShareDefaultScope()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var plan = Plan(CreateTask("落落莓", "A.json"));
        plan.Accounts = [new PuloniaTaskAccountBinding { AccountId = "profile-one" }, new PuloniaTaskAccountBinding { AccountId = "profile-two" }];
        plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, executor);
        foreach (var account in new[] { "profile-one", "profile-two", null })
        {
            var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, AccountId = account });
            await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Equal(1, executor.Executions);
        Assert.Equal("cn/world/default", Assert.Single(await service.ListLedgerAsync()).ScopeKey);
    }

    /// <summary>账本对每个（作用域，文件）只保留最新一条，每次成功的明细仍在各自运行历史的确认事件中。</summary>
    [Fact]
    public async Task Ledger_KeepsOnlyLatestSuccessPerFile()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        // 未知材料不限制执行，同一文件可以连续成功多次。
        var plan = await store.SavePlanAsync(Plan(CreateTask("未知材料", "A.json"), CreateTask("未知材料", "B.json")));
        await using var service = Service(store, executor);
        var runs = new List<PuloniaTaskRunView>();
        for (var i = 0; i < 3; i++)
            runs.Add(await Run(service, plan));

        Assert.Equal(6, executor.Executions);
        var ledger = await service.ListLedgerAsync();
        Assert.Equal(2, ledger.Count);
        var latest = runs[^1].ConfirmedEffects.Select(effect => effect.Entry.EventKey).Order().ToArray();
        Assert.Equal(latest, ledger.Select(entry => entry.EventKey).Order().ToArray());
        Assert.All(runs, run => Assert.Equal(2, run.ConfirmedEffects.Count));
        Assert.Equal(2, (await store.LoadStateAsync()).State.Ledger.Count);
        Assert.All(await store.ListHistoryAsync(), record => Assert.Equal(2, record.ConfirmedEffects.Count));
    }

    /// <summary>超时后，即使旧执行器返回完整成功也不能写入 CD 或成功结果。</summary>
    [Fact]
    public async Task Timeout_RejectsLateSuccess()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor { Execute = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }); return Completed(); } };
        var task = CreateTask("落落莓", "A.json");
        task.Policy.TimeoutSeconds = 0.1;
        var plan = await store.SavePlanAsync(Plan(task));
        await using var service = Service(store, executor);
        var run = await Run(service, plan);
        Assert.Equal(PuloniaTaskNodeStatus.TimedOut, Assert.Single(run.NodeResults).Status);
        Assert.Empty(await service.ListLedgerAsync());
        Assert.Empty((await store.LoadStateAsync()).State.Ledger);
    }

    /// <summary>取消不会留下采集占用；整份文件下次可从头重新运行。</summary>
    [Fact]
    public async Task Cancellation_DoesNotReserveFile()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FileExecutor { Execute = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Completed(); } };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor);
        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await service.CancelAsync(id);
        Assert.Equal(PuloniaTaskRunStatus.Cancelled, (await service.WaitForCompletionAsync(id)).Status);
        Assert.Empty(await service.ListLedgerAsync());
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
    }

    /// <summary>模拟本测试状态文件写入失败，成功不能被当作可重试的普通节点失败。</summary>
    [Fact]
    public async Task CommitFailure_DoesNotRetryOrExposeInMemorySuccess()
    {
        var stateDirectory = Path.Combine(_directory, "state");
        using var store = new PuloniaTaskStore(stateDirectory);
        var stateFile = Path.Combine(stateDirectory, "state.json");
        var executor = new FileExecutor { Execute = (_, _) =>
        {
            File.Move(stateFile, stateFile + ".saved");
            Directory.CreateDirectory(stateFile);
            return Task.FromResult(Completed());
        } };
        var task = CreateTask("落落莓", "A.json");
        task.Policy.MaxRetries = 2;
        var plan = await store.SavePlanAsync(Plan(task));
        await using var service = Service(store, executor);
        try
        {
            var run = await Run(service, plan);
            Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
            Assert.Equal(1, executor.Executions);
            Assert.Empty(await service.ListLedgerAsync());
            Assert.DoesNotContain(run.NodeResults, result => result.Status == PuloniaTaskNodeStatus.Succeeded);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }));
        }
        finally
        {
            Directory.Delete(stateFile);
            File.Move(stateFile + ".saved", stateFile);
        }
    }

    /// <summary>生成本测试独占文件，不运行真实地图追踪。</summary>
    private PuloniaTask CreateTask(string material, string file, JObject? config = null)
    {
        var path = Path.Combine(_directory, "routes", material, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new PuloniaTask { Name = file, TaskType = "pathing", Path = path,
            Settings = config is null ? new JObject() : new JObject { [PuloniaSettingCatalog.CollectionCooldownKey] = config } };
    }

    /// <summary>建立材料目录解析输入。</summary>
    private PuloniaCollectionCooldownPolicy Resolve(string material, JObject? settings = null)
        => PuloniaCollectionCooldown.Resolve(Path.Combine(_directory, material, "A.json"), null,
            settings is null ? new JObject() : new JObject { [PuloniaCollectionCooldown.ParameterName] = settings });

    /// <summary>建立无账号准备、无游戏会话的串行服务。</summary>
    private static PuloniaTaskService Service(PuloniaTaskStore store, FileExecutor executor) => new(store,
        new PuloniaTaskBuilder(store), [executor], new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>把真实资源引用节点放入标准根分组。</summary>
    private static PuloniaTaskPlan Plan(params PuloniaTask[] tasks) => new() { Name = "路线 CD 测试", RootTask = new PuloniaTask { Children = tasks.ToList() } };

    /// <summary>等待一次受控运行完成，避免无限等待。</summary>
    private static async Task<PuloniaTaskRunView> Run(PuloniaTaskService service, PuloniaTaskPlan plan)
    {
        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        return await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20));
    }

    /// <summary>使用与实际执行器相同的结果对象转换，成功字段和统计均来自本次返回值。</summary>
    private static PuloniaTaskOutcome Completed() => PuloniaPathingTaskExecutor.CreateOutcome(new PathingResult(true), null);

    /// <summary>仅清理本测试生成的临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>受控文件执行器，复用真实构建和结算路径，不访问游戏。</summary>
    private sealed class FileExecutor : IPuloniaTaskExecutor
    {
        /// <summary>实际执行次数，跳过任务不增加。</summary>
        public int Executions { get; private set; }
        /// <summary>可替换的失败、取消或写盘异常模拟。</summary>
        public Func<PuloniaTaskPreparedTask, CancellationToken, Task<PuloniaTaskOutcome>>? Execute { get; set; }
        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new() { TaskType = "pathing",
            DefaultParameters = new JObject { [PuloniaCollectionCooldown.ParameterName] = PuloniaCollectionCooldown.CreateDefaults() },
            SettingBindings = new Dictionary<string, string> { [PuloniaSettingCatalog.CollectionCooldownKey] = PuloniaCollectionCooldown.ParameterName },
            ParameterSchema = new JObject { ["type"] = "object", ["additionalProperties"] = true } }];
        /// <inheritdoc />
        public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
        {
            Executions++;
            return Execute?.Invoke(task, ct) ?? Task.FromResult(Completed());
        }
    }
}
