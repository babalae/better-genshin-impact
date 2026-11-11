using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 不启动游戏的路线 CD 回归：刷新规则与时间计算、路线身份、按运行账户存放的完成记录（写入顺序、幂等、清除、账户隔离、
/// 日切时区）、运行预览的账户选择，以及失败结算和重启。
/// </summary>
public sealed class PuloniaRouteCooldownTests : IDisposable
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
        var rule = Resolve(material);
        Assert.Equal(mode, PuloniaRouteCooldown.FormatMode(rule.Mode));
        Assert.Equal(hours, rule.Hours);
        Assert.Equal(days, rule.Days);
    }

    /// <summary>
    /// 官方脚本仓库的实际目录结构（2026-10-28 提交 d775a4b 抽样）：魔物与锄地路线由类别目录兜底为每日 04:00，
    /// 材料目录仍优先；仓库目录名的别名与混合类别按规则表识别，不访问网络。
    /// </summary>
    [Theory]
    [InlineData("pathing/敌人与魔物/丘丘人射手/丘丘人射手@曦/01.json", "daily", "敌人与魔物", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/敌人与魔物/盗宝团/盗宝团@作者/01.json", "daily", "盗宝团", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/锄地专区/精英400@汐/1-精英/01.json", "daily", "锄地专区", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/矿物/魔晶矿/01.json", "calendar_days", "魔晶矿", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/其他/提瓦特钓鱼指南/蒙德/01.json", "rolling", "提瓦特钓鱼指南", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/其他/木材伐木点/01.json", "disabled", "木材伐木点", PuloniaRouteRefreshRuleSource.Mixed)]
    [InlineData("pathing/地方特产/蒙德/落落莓/01.json", "rolling", "落落莓", PuloniaRouteRefreshRuleSource.Directory)]
    [InlineData("pathing/食材与炼金/松茸/01.json", "disabled", null, PuloniaRouteRefreshRuleSource.Unmatched)]
    public void RepositoryDirectories_AreRecognized(string relativePath, string mode, string? material,
        PuloniaRouteRefreshRuleSource source)
    {
        var rule = PuloniaRouteCooldown.Resolve(null,
            new ScriptResourceReference { RepositoryId = "rep_aaaaaaaaaaaaaaaa", RelativePath = relativePath }, new JObject());
        Assert.Equal(mode, PuloniaRouteCooldown.FormatMode(rule.Mode));
        Assert.Equal(material, rule.MaterialName);
        Assert.Equal(source, rule.Source);
        // 从完成记录中的小写路线身份重新识别得到同一规则，目录引用计数与运行时判定一致
        Assert.Equal(rule.Description,
            PuloniaRouteCooldown.ResolveFromFileIdentity(rule.FileIdentity, null).Description);
    }

    /// <summary>状态判定：没有记录、CD 中、恰好到期、一次性完成、未识别与关闭限制各自得到不同状态。</summary>
    [Fact]
    public void Status_DistinguishesStates()
    {
        var success = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var rule = Resolve("落落莓");
        var completion = Completion(rule, success);
        Assert.Equal(PuloniaRouteCooldownState.Ready, PuloniaRouteCooldown.GetStatus(rule, null, success, 480).State);
        var cooling = PuloniaRouteCooldown.GetStatus(rule, completion, success.AddHours(1), 480);
        Assert.Equal(PuloniaRouteCooldownState.CoolingDown, cooling.State);
        Assert.Same(completion, cooling.Completion);
        Assert.Equal(PuloniaRouteCooldown.ReasonCooldownActive, PuloniaRouteCooldown.GetReasonCode(cooling));
        Assert.Equal(PuloniaRouteCooldownState.Ready, PuloniaRouteCooldown.GetStatus(rule, completion, success.AddHours(46), 480).State);
        var once = PuloniaRouteCooldown.GetStatus(Resolve("宝箱"), completion, success.AddYears(1), 480);
        Assert.Equal(PuloniaRouteCooldownState.Completed, once.State);
        Assert.Equal(PuloniaRouteCooldown.ReasonCooldownCompleted, PuloniaRouteCooldown.GetReasonCode(once));
        Assert.Equal(PuloniaRouteCooldownState.Unmatched, PuloniaRouteCooldown.GetStatus(Resolve("未知材料"), completion, success, 480).State);
        Assert.Equal(PuloniaRouteCooldownState.NotChecked,
            PuloniaRouteCooldown.GetStatus(Resolve("落落莓", new JObject { ["mode"] = "disabled" }), completion, success, 480).State);
        Assert.Equal("不检查", PuloniaRouteCooldown.DescribeSettings(new JObject { ["mode"] = "disabled" }));
        Assert.Equal("每日 04:00", PuloniaRouteCooldown.DescribeSettings(new JObject { ["mode"] = "daily" }));
        Assert.Equal("自动识别", PuloniaRouteCooldown.DescribeSettings(PuloniaRouteCooldown.CreateDefaults()));
    }

    /// <summary>仓库材料从原始路径识别，版本变化和不同缓存位置不改变路线身份。</summary>
    [Fact]
    public void RepositoryIdentity_UsesOriginalPathAndIgnoresRevision()
    {
        var reference = new ScriptResourceReference { RepositoryId = "rep_aaaaaaaaaaaaaaaa", RelativePath = "pathing/落落莓/蒙德/A.json", ApprovedRevision = new string('1', 40) };
        var first = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "cache", "A.json"), reference, new JObject());
        var second = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "other", "A.json"), reference.WithRevision(new string('2', 40)), new JObject());
        Assert.Equal("落落莓", first.MaterialName);
        Assert.Equal(first.FileIdentity, second.FileIdentity);
        // 路线身份直接写入完成记录，能看出是哪个文件。
        Assert.Equal("repository:rep_aaaaaaaaaaaaaaaa/pathing/落落莓/蒙德/a.json", first.FileIdentity);
        var other = PuloniaRouteCooldown.Resolve(null, new ScriptResourceReference { RepositoryId = "rep_bbbbbbbbbbbbbbbb", RelativePath = reference.RelativePath }, new JObject());
        Assert.NotEqual(first.FileIdentity, other.FileIdentity);
    }

    /// <summary>以最近匹配的目录为准；非精确材料名不进行包含匹配。</summary>
    [Fact]
    public void NearestDirectory_WinsWithoutFuzzyMatching()
    {
        var rule = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "落落莓", "薄荷", "A.json"), null, new JObject());
        Assert.Equal("薄荷", rule.MaterialName);
        Assert.Equal(12, rule.Hours);
        Assert.Null(Resolve("落落莓备份").MaterialName);
    }

    /// <summary>服务器四点、午夜保守估算及周一边界按 UTC 转换，边界本身属于新周期。</summary>
    [Fact]
    public void CalendarRules_RespectServerBoundaries()
    {
        var success = new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(success.AddHours(46).ToUniversalTime(), PuloniaRouteCooldown.GetNextEligibleAt(Resolve("落落莓"), success, 480));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime(),
            PuloniaRouteCooldown.GetNextEligibleAt(Resolve("水晶块"), success, 480));
        var beforeReset = new DateTimeOffset(2026, 10, 10, 3, 59, 59, TimeSpan.FromHours(8));
        var boundary = beforeReset.AddSeconds(1);
        Assert.Equal(boundary.ToUniversalTime(), PuloniaRouteCooldown.GetNextEligibleAt(Resolve("晶核"), beforeReset, 480));
        Assert.Equal(boundary.AddDays(1).ToUniversalTime(), PuloniaRouteCooldown.GetNextEligibleAt(Resolve("晶核"), boundary, 480));
        var weekly = Resolve("未知", new JObject { ["mode"] = "weekly", ["reset_weekday"] = 1, ["reset_hour"] = 4 });
        var monday = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(monday.AddDays(7).ToUniversalTime(), PuloniaRouteCooldown.GetNextEligibleAt(weekly, monday, -300));
    }

    /// <summary>账户服务器时区与分钟偏移的对照：UtcPlus8 480、Europe 60、America -300，日切因此不同。</summary>
    [Fact]
    public void AccountServerTimeZone_ChangesDailyBoundary()
    {
        Assert.Equal(480, PuloniaServerTime.GetUtcOffsetMinutes(PuloniaTaskServerTimeZone.UtcPlus8));
        Assert.Equal(60, PuloniaServerTime.GetUtcOffsetMinutes(PuloniaTaskServerTimeZone.Europe));
        Assert.Equal(-300, PuloniaServerTime.GetUtcOffsetMinutes(PuloniaTaskServerTimeZone.America));
        // 同一完成时刻：国服下一次 04:00（UTC+8）与美服下一次 04:00（UTC-5）是不同的时刻
        var success = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var daily = Resolve("晶核");
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.FromHours(8)).ToUniversalTime(),
            PuloniaRouteCooldown.GetNextEligibleAt(daily, success, 480));
        Assert.Equal(new DateTimeOffset(2026, 10, 11, 4, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime(),
            PuloniaRouteCooldown.GetNextEligibleAt(daily, success, -300));
    }

    /// <summary>时间范围、缺失相对时长及不合法重置选项在准备前被拒绝。</summary>
    [Theory]
    [InlineData("{\"mode\":\"rolling\",\"hours\":0}")]
    [InlineData("{\"mode\":\"rolling\"}")]
    [InlineData("{\"mode\":\"calendar_days\",\"days\":0}")]
    [InlineData("{\"mode\":\"daily\",\"reset_hour\":24}")]
    [InlineData("{\"mode\":\"weekly\",\"reset_weekday\":0}")]
    public void InvalidSettings_AreRejected(string json)
        => Assert.Throws<PuloniaTaskValidationException>(() => PuloniaRouteCooldown.ValidateSettings(JObject.Parse(json), "测试"));

    /// <summary>规则变化只重算执行资格，不改写完成记录；恰好到期允许执行；记录时规则可还原为说明文字。</summary>
    [Fact]
    public void Eligibility_RecalculatesRuleAndAllowsExactBoundary()
    {
        var success = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var original = Resolve("落落莓");
        var completion = Completion(original, success);
        var shortened = Resolve("落落莓", new JObject { ["mode"] = "rolling", ["hours"] = 12 });
        var status = PuloniaRouteCooldown.GetStatus(shortened, completion, success.AddHours(12), 480);
        Assert.False(status.IsSkipped);
        var data = PuloniaRouteCooldown.Describe(shortened, status.LastSuccessAt, status.NextEligibleAt);
        Assert.Equal(success.AddHours(12).ToString("O"), data.Value<string>("route_next_eligible_at"));
        Assert.True(PuloniaRouteCooldown.GetStatus(original, completion, success.AddHours(12), 480).IsSkipped);
        var once = Resolve("落落莓", new JObject { ["mode"] = "once" });
        Assert.True(PuloniaRouteCooldown.GetStatus(once, completion, success.AddYears(10), 480).IsSkipped);
        // 记录时规则只保存当前模式用到的字段，还原后说明文字一致
        Assert.Equal(original.Description, completion.Rule.ToRefreshRule(completion.Route).Description);
        var json = JObject.Parse(PuloniaTaskJson.Write(completion.Rule));
        Assert.Equal(new[] { "mode", "hours", "material", "source" }, json.Properties().Select(item => item.Name));
    }

    /// <summary>
    /// 完成记录每个账户每份路线只保留最新一条；同一运行、节点、尝试重复写入时幂等；不同账户互不影响；
    /// 清除只删除指定账户的指定路线，并发布账户进度变化。
    /// </summary>
    [Fact]
    public async Task Completions_KeepLatestPerRouteAreIdempotentAndIsolatedByAccount()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var my = (await accounts.LoadAccountsAsync()).Accounts[0].Id;
        const string other = "acc_bbbbbbbbbbbbbbbb";
        var changes = new List<string>();
        accounts.AccountProgressChanged += (_, e) => changes.Add(e.AccountId);
        var rule = Resolve("落落莓");
        var t0 = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        var first = Completion(rule, t0);
        // 1. 首次写入；同一（运行、节点、尝试）再写一次不改变记录
        Assert.True(await accounts.RecordRouteCompletionAsync(my, first));
        Assert.False(await accounts.RecordRouteCompletionAsync(my, Completion(rule, t0.AddHours(1), first.RunId)));
        Assert.Equal(t0, Assert.Single(await accounts.ListRouteCompletionsAsync(my)).CompletedAt);
        // 2. 同一路线的新成功替换旧记录，只保留最新一条
        var second = Completion(rule, t0.AddDays(2));
        Assert.True(await accounts.RecordRouteCompletionAsync(my, second));
        Assert.Equal(second.RunId, Assert.Single(await accounts.ListRouteCompletionsAsync(my)).RunId);
        // 3. 另一个账户互不影响
        Assert.Empty(await accounts.ListRouteCompletionsAsync(other));
        Assert.True(await accounts.RecordRouteCompletionAsync(other, Completion(Resolve("宝箱"), t0)));
        Assert.Single(await accounts.ListRouteCompletionsAsync(my));
        Assert.Single(await accounts.ListRouteCompletionsAsync(other));
        // 4. 清除：只删除指定账户的指定路线
        Assert.Equal(1, await accounts.ClearRouteCompletionsAsync(my, [rule.FileIdentity]));
        Assert.Equal(0, await accounts.ClearRouteCompletionsAsync(my, [rule.FileIdentity]));
        Assert.Empty(await accounts.ListRouteCompletionsAsync(my));
        Assert.Single(await accounts.ListRouteCompletionsAsync(other));
        Assert.Equal(new[] { my, my, other, my }, changes);
        // 文件格式：schema_version 1 与 completions 列表，每条只有设计中的字段
        var file = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "state", "accounts", other, "route-completions.json")));
        Assert.Equal(1, file.Value<int>("schema_version"));
        Assert.Equal(new[] { "route", "completed_at", "run_id", "task_address", "attempt", "instance", "rule" },
            ((JObject)((JArray)file["completions"]!)[0]).Properties().Select(item => item.Name));
    }

    /// <summary>按文件修改时间缓存：其他实例（另一个账户服务与存储对象）写入后，下一次读取发现变化并重新读取。</summary>
    [Fact]
    public async Task Completions_AreReloadedWhenAnotherInstanceWrites()
    {
        var directory = Path.Combine(_directory, "state");
        using var storeA = new PuloniaTaskStore(directory);
        using var storeB = new PuloniaTaskStore(directory);
        var accountsA = new PuloniaAccountService(storeA, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var accountsB = new PuloniaAccountService(storeB, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.ChildSession));
        var my = (await accountsA.LoadAccountsAsync()).Accounts[0].Id;
        Assert.Empty(await accountsA.ListRouteCompletionsAsync(my));
        var unchanged = await accountsA.GetRouteCompletionIndexAsync(my);
        Assert.Same(unchanged, await accountsA.GetRouteCompletionIndexAsync(my));
        await accountsB.RecordRouteCompletionAsync(my, Completion(Resolve("落落莓"), DateTimeOffset.UtcNow));
        Assert.Single(await accountsA.ListRouteCompletionsAsync(my));
        await accountsB.RecordRouteCompletionAsync(my, Completion(Resolve("薄荷"), DateTimeOffset.UtcNow));
        Assert.Equal(2, (await accountsA.ListRouteCompletionsAsync(my)).Count);
    }

    /// <summary>不同文件独立，同文件跨计划和重复节点共享完成记录，重启后仍跳过；快照字段为 refresh_rule。</summary>
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
            Assert.Equal(2, (await CompletionsAsync(store)).Count);
            var snapshot = PuloniaTaskJson.ReadSnapshot(run.SnapshotJson);
            Assert.Equal(PuloniaRouteRefreshMode.Rolling, snapshot.RootTask.Children[0].RefreshRule!.Mode);
            Assert.Equal(46, snapshot.RootTask.Children[0].RefreshRule!.Hours);
            // 规则对象的模式与来源仍按原有字符串写入快照的 refresh_rule
            Assert.Contains("\"refresh_rule\": {", run.SnapshotJson);
            Assert.Contains("\"mode\": \"rolling\"", run.SnapshotJson);
            Assert.Contains("\"source\": \"目录自动识别\"", run.SnapshotJson);
            Assert.Contains("\"route_cd\"", run.SnapshotJson);
            Assert.DoesNotContain("collection", run.SnapshotJson);
            // 同一文件第二次出现时因 CD 跳过，原因码可区分其他跳过，结果数据为 route_*
            Assert.Equal(PuloniaRouteCooldown.ReasonCooldownActive, run.NodeResults[1].ReasonCode);
            Assert.True(PuloniaRouteCooldown.IsCooldownSkip(run.NodeResults[1]));
            Assert.Equal("落落莓", run.NodeResults[1].Data.Value<string>("route_material"));
        }
        await using (var restarted = Service(store, executor))
        {
            var run = await Run(restarted, other);
            Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single(run.NodeResults).Status);
            Assert.Contains("CD", run.NodeResults[0].Message);
            Assert.Equal(2, executor.Executions);
        }
    }

    /// <summary>失败、提前放弃及旧结束标记不写完成记录，后续完整成功仍可重新执行。</summary>
    [Theory]
    [InlineData(PuloniaTaskOutcomeKind.Failed)]
    [InlineData(PuloniaTaskOutcomeKind.ExecutedUnverified)]
    public async Task IncompleteFile_DoesNotWriteCompletion(PuloniaTaskOutcomeKind kind)
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor { Execute = (_, _) => Task.FromResult(new PuloniaTaskOutcome(kind, "提前放弃")) };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor);
        Assert.Equal(PuloniaTaskRunStatus.Failed, (await Run(service, plan)).Status);
        Assert.Empty(await CompletionsAsync(store));
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
        Assert.Single(await CompletionsAsync(store));
    }

    /// <summary>提前结束的失败对象不能写完成记录；后续成功对象仍可正常结算。</summary>
    [Fact]
    public async Task IncompleteReturn_DoesNotWriteCompletion()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor { Execute = (_, _) => Task.FromResult(PuloniaPathingTaskExecutor.CreateOutcome(new PathingResult(false, errorMessage: "提前放弃"), null)) };
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor);
        var failedRun = await Run(service, plan);
        Assert.Equal(PuloniaTaskRunStatus.Failed, failedRun.Status);
        Assert.Equal("提前放弃", failedRun.NodeResults[0].Data.Value<string>("error_message"));
        Assert.Empty(await CompletionsAsync(store));
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
        Assert.Single(await CompletionsAsync(store));
    }

    /// <summary>关闭限制仍写入完成记录（记录时规则为不检查），恢复规则或改变时长时从该记录重算。</summary>
    [Fact]
    public async Task RuleChanges_RecalculateExistingSuccess()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var executor = new FileExecutor();
        var task = CreateTask("未知材料", "A.json", new JObject { ["mode"] = "disabled" });
        var plan = await store.SavePlanAsync(Plan(task));
        await using var service = Service(store, executor);
        await Run(service, plan);
        Assert.Equal(PuloniaRouteRefreshMode.Disabled, Assert.Single(await CompletionsAsync(store)).Rule.Mode);
        plan.RootTask.Children[0].Settings[PuloniaSettingCatalog.RouteCooldownKey] = new JObject { ["mode"] = "rolling", ["hours"] = 24 };
        plan = await store.SavePlanAsync(plan);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single((await Run(service, plan)).NodeResults).Status);
        plan.RootTask.Children[0].Settings[PuloniaSettingCatalog.RouteCooldownKey] = new JObject { ["mode"] = "disabled" };
        plan = await store.SavePlanAsync(plan);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, Assert.Single((await Run(service, plan)).NodeResults).Status);
        Assert.Equal(2, executor.Executions);
    }

    /// <summary>未知目录不限制运行，但成功后仍写入完成记录，供之后改为手动规则时使用。</summary>
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
        Assert.Contains("未识别刷新规则", second.NodeResults[0].Data.Value<string>("route_rule"));
        Assert.Equal(PuloniaRouteRefreshRuleSource.Unmatched, Assert.Single(await CompletionsAsync(store)).Rule.Source);
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
    /// 预览不写完成记录，清除经账户服务并发布账户进度变化。
    /// </summary>
    [Fact]
    public async Task Preview_MatchesRunAndClearRestoresExecution()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var executor = new FileExecutor();
        var a = CreateTask("落落莓", "A.json");
        var plan = await store.SavePlanAsync(Plan(a, new PuloniaTask { Name = "重复 A", TaskType = "pathing", Path = a.Path },
            CreateTask("未知材料", "B.json")));
        await using var service = Service(store, executor, accounts);
        var request = new PuloniaTaskRequest { PlanId = plan.Id };
        var my = (await accounts.LoadAccountsAsync()).Accounts[0];

        var preview = await service.PreviewAsync(request);
        Assert.Equal(new[] { PuloniaTaskPreviewOutcome.Execute, PuloniaTaskPreviewOutcome.RepeatedInRun, PuloniaTaskPreviewOutcome.Execute },
            preview.Items.Select(item => item.Outcome));
        Assert.Equal(1, preview.UnmatchedCount);
        Assert.Equal((my.Id, my.Name, (string?)null), (preview.AccountId, preview.AccountName, preview.AccountNotice));
        Assert.Empty(await CompletionsAsync(store));

        await Run(service, plan);
        var after = await service.PreviewAsync(request);
        Assert.Equal(PuloniaTaskPreviewOutcome.CoolingDown, after.Items[0].Outcome);
        Assert.Equal(2, after.CooldownSkipCount);
        Assert.NotNull(after.Items[0].Cooldown!.Completion);

        var changed = 0;
        accounts.AccountProgressChanged += (_, _) => changed++;
        Assert.Equal(1, await accounts.ClearRouteCompletionsAsync(my.Id, [after.Items[0].Rule!.FileIdentity]));
        Assert.Equal(1, changed);
        Assert.Single(await CompletionsAsync(store));
        Assert.Equal(PuloniaTaskPreviewOutcome.Execute, (await service.PreviewAsync(request)).Items[0].Outcome);
    }

    /// <summary>
    /// 运行预览的账户（统筹设计 5.6）：跟随当前账户为本实例当前账户，计划指定账户为该账户，调用方可以临时指定其他账户；
    /// 本实例当前账户未确定时退回账户列表第一个并说明。日切偏移取所选账户的服务器时区。
    /// </summary>
    [Fact]
    public async Task Preview_SelectsAccountBySettings()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        var my = settings.Accounts[0];
        var alt = new PuloniaGenshinAccountProfile { Name = "欧服号", Uid = "100000002", ServerTimeZone = PuloniaTaskServerTimeZone.Europe };
        settings.Accounts.Add(alt);
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        var follow = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        var specified = Plan(CreateTask("落落莓", "B.json"));
        specified.AccountId = alt.Id;
        specified = await store.SavePlanAsync(specified);
        await using var service = Service(store, new FileExecutor(), accounts);

        var current = await service.PreviewAsync(new PuloniaTaskRequest { PlanId = follow.Id });
        Assert.Equal((my.Id, PuloniaServerTime.GetUtcOffsetMinutes(my.ServerTimeZone)), (current.AccountId, current.ServerUtcOffsetMinutes));
        var viewed = await service.PreviewAsync(new PuloniaTaskRequest { PlanId = follow.Id }, alt.Id);
        Assert.Equal((alt.Id, "欧服号", 60), (viewed.AccountId, viewed.AccountName, viewed.ServerUtcOffsetMinutes));
        Assert.Equal(alt.Id, (await service.PreviewAsync(new PuloniaTaskRequest { PlanId = specified.Id })).AccountId);

        await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, null);
        var undetermined = await service.PreviewAsync(new PuloniaTaskRequest { PlanId = follow.Id });
        Assert.Equal(my.Id, undetermined.AccountId);
        Assert.Contains("未确定", undetermined.AccountNotice);
    }

    /// <summary>
    /// 完成记录按运行账户存放：本实例当前账户换成另一个账户后，同一路线不受前一个账户的记录影响；换回后仍按原记录跳过。
    /// </summary>
    [Fact]
    public async Task Runs_UseInstanceCurrentAccountProgress()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        var my = settings.Accounts[0];
        var alt = new PuloniaGenshinAccountProfile { Name = "小号", Uid = "100000002" };
        settings.Accounts.Add(alt);
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        var executor = new FileExecutor();
        var plan = await store.SavePlanAsync(Plan(CreateTask("落落莓", "A.json")));
        await using var service = Service(store, executor, accounts);

        var first = await Run(service, plan);
        Assert.Equal(my.Id, first.AccountId);
        var completion = Assert.Single(await accounts.ListRouteCompletionsAsync(my.Id));
        Assert.Equal((first.RunId, PuloniaInstanceKey.Primary, 1), (completion.RunId, completion.Instance, completion.Attempt));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
        Assert.Equal(1, executor.Executions);

        await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, alt.Id);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, Assert.Single((await Run(service, plan)).NodeResults).Status);
        Assert.Equal(2, executor.Executions);
        Assert.Single(await accounts.ListRouteCompletionsAsync(alt.Id));

        await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, my.Id);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single((await Run(service, plan)).NodeResults).Status);
        Assert.Equal(2, executor.Executions);
    }

    /// <summary>
    /// 日切时区按运行账户：运行开始确定运行账户后，请求的服务器偏移改为该账户的服务器时区，节点结果的下次可执行时间按它计算；
    /// 不含游戏节点、没有运行账户的运行保留通用配置的偏移。
    /// </summary>
    [Fact]
    public async Task ServerOffset_FollowsRunAccount()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "state"));
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        settings.Accounts[0].ServerTimeZone = PuloniaTaskServerTimeZone.America;
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        var plan = await store.SavePlanAsync(Plan(CreateTask("晶核", "A.json")));
        var pure = await store.SavePlanAsync(Plan(new PuloniaTask { Name = "计算", TaskType = "csharp",
            Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) } }));
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new FileExecutor(), new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts)
        {
            GameLeaseFactory = _ => Task.FromResult(new PuloniaGameTaskLease(null!, () => { }))
        };

        var run = await Run(service, plan);
        Assert.Equal(-300, run.Request.ServerUtcOffsetMinutes);
        var result = Assert.Single(run.NodeResults);
        Assert.Equal(PuloniaRouteCooldown.GetNextEligibleAt(Resolve("晶核"), result.FinishedAt, -300)!.Value.ToString("O"),
            result.Data.Value<string>("route_next_eligible_at"));
        // 历史中的请求同样记录账户的偏移，续跑沿用
        Assert.Equal(-300, (await store.LoadHistoryRecordAsync(plan.Id, run.RunId))!.Request.ServerUtcOffsetMinutes);
        Assert.Equal(-300, (await service.PreviewAsync(new PuloniaTaskRequest { PlanId = plan.Id })).ServerUtcOffsetMinutes);

        var pureRun = await Run(service, pure);
        Assert.Null(pureRun.AccountId);
        Assert.Equal(PuloniaServerTime.GetUtcOffsetMinutes(), pureRun.Request.ServerUtcOffsetMinutes);
    }

    /// <summary>完成记录与节点结果在下一节点执行前都已落盘，后续取消不会撤销成功文件。</summary>
    [Fact]
    public async Task SuccessAndCompletion_ArePersistedBeforeAdvancing()
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
        var state = (await store.LoadStateAsync(PuloniaInstanceKey.Primary)).State;
        var result = Assert.Single(state.ActiveRun!.NodeResults);
        var completion = Assert.Single(await CompletionsAsync(store));
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, result.Status);
        Assert.Equal(result.FinishedAt, completion.CompletedAt);
        Assert.Equal(completion.Route, Assert.Single(state.ActiveRun.RouteCompletions).Route);
        Assert.DoesNotContain("ledger", await File.ReadAllTextAsync(store.GetStatePath(PuloniaInstanceKey.Primary)));
        await service.CancelAsync(id);
        await service.WaitForCompletionAsync(id);
        Assert.Single(await CompletionsAsync(store));
    }

    /// <summary>本次完成的路线随历史归档，重启后仍可查看；历史文件无 BOM；执行记录页显示“本次完成的路线”。</summary>
    [Fact]
    public async Task RouteCompletion_IsArchivedAndSurvivesRestart()
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
            var completion = Assert.Single(run.RouteCompletions);
            Assert.Equal(1, completion.Attempt);
            Assert.Equal(run.NodeResults[0].TaskAddress, completion.TaskAddress);
            Assert.Equal(runId, completion.RunId);
            Assert.Single(await CompletionsAsync(store));
        }
        await using var restarted = Service(store, new FileExecutor());
        var restored = await restarted.GetRunAsync(runId);
        Assert.Single(restored.RouteCompletions);
        var history = Assert.Single(await store.ListHistoryAsync());
        Assert.Single(history.RouteCompletions);
        var path = Path.Combine(_directory, "state", "history", plan.Id, history.RunId + ".json");
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Contains("\"route_completions\"", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Contains(new PuloniaTaskRunItemViewModel(restored).RouteCompletionFields,
            field => field.Label.StartsWith("本次完成的路线") && field.Value.Contains("路线：local:"));
    }

    /// <summary>提交后修改计划不会改变已固定的刷新规则，未成功的排队文件也没有提前占用。</summary>
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
        Assert.Empty(await CompletionsAsync(store));
        second.RootTask.Children[0].Settings[PuloniaSettingCatalog.RouteCooldownKey] = new JObject { ["mode"] = "disabled" };
        await store.SavePlanAsync(second);
        release.SetResult();
        await service.WaitForCompletionAsync(firstId);
        var run = await service.WaitForCompletionAsync(secondId);
        var rule = Assert.Single(run.RouteCompletions).Rule;
        Assert.Equal((PuloniaRouteRefreshMode.Rolling, (double?)5), (rule.Mode, rule.Hours));
    }

    /// <summary>每份路线只保留最新一条完成记录，每次成功的明细仍在各自运行历史的“本次完成的路线”中。</summary>
    [Fact]
    public async Task Completions_KeepOnlyLatestSuccessPerRoute()
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
        var completions = await CompletionsAsync(store);
        Assert.Equal(2, completions.Count);
        Assert.All(completions, completion => Assert.Equal(runs[^1].RunId, completion.RunId));
        Assert.All(runs, run => Assert.Equal(2, run.RouteCompletions.Count));
        Assert.All(await store.ListHistoryAsync(), record => Assert.Equal(2, record.RouteCompletions.Count));
    }

    /// <summary>超时后，即使旧执行器返回完整成功也不能写入完成记录或成功结果。</summary>
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
        Assert.Empty(await CompletionsAsync(store));
        Assert.Empty(run.RouteCompletions);
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
        Assert.Empty(await CompletionsAsync(store));
        executor.Execute = null;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await Run(service, plan)).Status);
    }

    /// <summary>
    /// 写入顺序：先写完成记录、再写节点结果。第 2 步（state.json）写盘失败时熔断、不重试，成功也不出现在内存结果中；
    /// 但完成记录已经存在，重启后下次运行按 CD 跳过这份路线，不会重复执行。
    /// </summary>
    [Fact]
    public async Task StateWriteFailure_KeepsCompletionAndNextRunSkips()
    {
        var stateDirectory = Path.Combine(_directory, "state");
        using var store = new PuloniaTaskStore(stateDirectory);
        var stateFile = store.GetStatePath(PuloniaInstanceKey.Primary);
        var executor = new FileExecutor { Execute = (_, _) =>
        {
            File.Move(stateFile, stateFile + ".saved");
            Directory.CreateDirectory(stateFile);
            return Task.FromResult(Completed());
        } };
        var task = CreateTask("落落莓", "A.json");
        task.Policy.MaxRetries = 2;
        var plan = await store.SavePlanAsync(Plan(task));
        await using (var service = Service(store, executor))
        {
            try
            {
                var run = await Run(service, plan);
                // state.json 仍不可写，结束时的状态保存同样失败，消息为持久化失败说明
                Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
                Assert.Equal(1, executor.Executions);
                Assert.DoesNotContain(run.NodeResults, result => result.Status == PuloniaTaskNodeStatus.Succeeded);
                Assert.Empty(run.RouteCompletions);
                // 第 1 步已经完成：完成记录在账户目录中
                Assert.Single(await CompletionsAsync(store));
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }));
            }
            finally
            {
                Directory.Delete(stateFile);
                File.Move(stateFile + ".saved", stateFile);
            }
        }
        // 重启：上次活动运行转为中断，下次运行按完成记录跳过
        executor.Execute = null;
        await using var restarted = Service(store, executor);
        var next = await Run(restarted, plan);
        var skipped = Assert.Single(next.NodeResults);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, skipped.Status);
        Assert.Equal(PuloniaRouteCooldown.ReasonCooldownActive, skipped.ReasonCode);
        Assert.Equal(1, executor.Executions);
    }

    /// <summary>第 1 步（完成记录）写盘失败时熔断，不写节点成功结果，也不重试。</summary>
    [Fact]
    public async Task CompletionWriteFailure_StopsAutomationWithoutSuccess()
    {
        var stateDirectory = Path.Combine(_directory, "state");
        using var store = new PuloniaTaskStore(stateDirectory);
        var my = (await store.LoadAccountSettingsAsync()).Accounts[0].Id;
        var completionsPath = Path.Combine(stateDirectory, "accounts", my, "route-completions.json");
        var executor = new FileExecutor { Execute = (_, _) =>
        {
            // 完成记录的位置被目录占用，原子替换失败
            Directory.CreateDirectory(completionsPath);
            return Task.FromResult(Completed());
        } };
        var task = CreateTask("落落莓", "A.json");
        task.Policy.MaxRetries = 2;
        var plan = await store.SavePlanAsync(Plan(task));
        await using var service = Service(store, executor);
        var run = await Run(service, plan);
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
        Assert.Contains("路线完成记录写盘失败", run.Message);
        Assert.Equal(1, executor.Executions);
        Assert.DoesNotContain(run.NodeResults, result => result.Status == PuloniaTaskNodeStatus.Succeeded);
        Assert.Empty(run.RouteCompletions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }));
    }

    /// <summary>生成本测试独占文件，不运行真实地图追踪。</summary>
    private PuloniaTask CreateTask(string material, string file, JObject? config = null)
    {
        var path = Path.Combine(_directory, "routes", material, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return new PuloniaTask { Name = file, TaskType = "pathing", Path = path,
            Settings = config is null ? new JObject() : new JObject { [PuloniaSettingCatalog.RouteCooldownKey] = config } };
    }

    /// <summary>建立材料目录解析输入。</summary>
    private PuloniaRouteRefreshRule Resolve(string material, JObject? settings = null)
        => PuloniaRouteCooldown.Resolve(Path.Combine(_directory, material, "A.json"), null,
            settings is null ? new JObject() : new JObject { [PuloniaRouteCooldown.ParameterName] = settings });

    /// <summary>为一份路线建立完成记录；未给运行 ID 时新建一个。</summary>
    private static PuloniaRouteCompletion Completion(PuloniaRouteRefreshRule rule, DateTimeOffset completedAt, string? runId = null) => new()
    {
        Route = rule.FileIdentity, CompletedAt = completedAt.ToUniversalTime(),
        RunId = runId ?? PuloniaId.NewRunId(DateTimeOffset.Now) + BetterGenshinImpact.Helpers.ShortId.Create(null, 4),
        TaskAddress = "plan/task", Attempt = 1, Instance = PuloniaInstanceKey.Primary, Rule = PuloniaRouteCompletionRule.From(rule)
    };

    /// <summary>读取“我的账户”（初始唯一账户）的完成记录。</summary>
    private static async Task<IReadOnlyList<PuloniaRouteCompletion>> CompletionsAsync(PuloniaTaskStore store)
        => await store.ReadRouteCompletionsAsync((await store.LoadAccountSettingsAsync()).Accounts[0].Id);

    /// <summary>建立不触碰游戏的串行服务：地图追踪节点需要游戏会话，租约替换为空租约，运行账户为本实例当前账户。</summary>
    private static PuloniaTaskService Service(PuloniaTaskStore store, FileExecutor executor, IPuloniaAccountService? accounts = null)
        => new(store, new PuloniaTaskBuilder(store), [executor], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts)
        {
            GameLeaseFactory = _ => Task.FromResult(new PuloniaGameTaskLease(null!, () => { }))
        };

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

    /// <summary>受控文件执行器，复用真实构建和结算路径，不访问游戏；与真实地图追踪一样需要游戏会话。</summary>
    private sealed class FileExecutor : IPuloniaTaskExecutor
    {
        /// <summary>实际执行次数，跳过任务不增加。</summary>
        public int Executions { get; private set; }
        /// <summary>可替换的失败、取消或写盘异常模拟。</summary>
        public Func<PuloniaTaskPreparedTask, CancellationToken, Task<PuloniaTaskOutcome>>? Execute { get; set; }
        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new() { TaskType = "pathing", RequiresGameSession = true,
            DefaultParameters = new JObject { [PuloniaRouteCooldown.ParameterName] = PuloniaRouteCooldown.CreateDefaults() },
            SettingBindings = new Dictionary<string, string> { [PuloniaSettingCatalog.RouteCooldownKey] = PuloniaRouteCooldown.ParameterName },
            ParameterSchema = new JObject { ["type"] = "object", ["additionalProperties"] = true } }];
        /// <inheritdoc />
        public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
        {
            Executions++;
            return Execute?.Invoke(task, ct) ?? Task.FromResult(Completed());
        }
    }
}
