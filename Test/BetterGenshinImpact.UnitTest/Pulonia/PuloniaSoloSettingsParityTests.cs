using BetterGenshinImpact.GameTask.Common.Resin;
using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>新旧设置的参数保存、每周继承和首领累计行为回归，不启动应用或游戏。</summary>
public sealed class PuloniaSoloSettingsParityTests
{
    /// <summary>每个专属页与补充卡片共同覆盖整个能力 Schema，补充输入也必须真正提交。</summary>
    [Fact]
    public void DedicatedEditors_KeepEverySchemaParameterEditable()
    {
        var registry = new SoloTaskRegistry([typeof(SoloTaskRegistry).Assembly]);
        using var services = new ServiceCollection().BuildServiceProvider();
        foreach (var definition in PuloniaSoloTestDefinitions.OneDragonCandidates())
        {
            var editor = PuloniaTaskSettingsEditorViewModel.Create(registry, definition, definition.DefaultParameters, services);
            var properties = Assert.IsType<JObject>(definition.ParameterSchema["properties"]);
            var available = (editor.DedicatedSettings?.EditedParameterNames ?? [])
                .Concat(editor.Fields.Select(field => field.Name)).ToArray();
            Assert.Equal(available.Length, available.Distinct().Count());
            Assert.Equal(properties.Properties().Select(property => property.Name).Order(), available.Order());
            Assert.Empty(editor.CollectValidatedChanges());
            if (definition.TaskType == "solo.auto_domain")
            {
                editor.Fields.Single(field => field.Name == "revive_retry_count").NumberValue = 5;
                Assert.Equal(5, editor.CollectValidatedChanges().Value<int>("revive_retry_count"));
            }
        }
    }

    /// <summary>保留树脂数必须可保存，其他未修改设置继续继承。</summary>
    [Fact]
    public void Craft_ChangingRetainedResinOnlyWritesThatParameter()
    {
        var settings = new CraftCondensedResinSettingsViewModel(new JObject
            { ["country"] = "枫丹", ["min_resin_to_keep"] = 40 }) { MinResinToKeep = 80 };
        var changes = new JObject();
        settings.CollectChanges(changes);
        Assert.Single(changes);
        Assert.Equal(80, changes.Value<int>("min_resin_to_keep"));
    }

    /// <summary>打开已有每周设置不生成覆盖；编辑一个字段保留不由专属页编辑的按日参数。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WeeklySettings_PreserveSparseOverridesAndUneditedFields(bool domain)
    {
        var originalDay = new JObject { ["enabled"] = false, ["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(new ResinUsage { Mode = ResinUsageMode.RunCount, RunCount = 2 })), ["party_name"] = "原队伍" };
        var original = new JObject { ["weekday_overrides"] = new JObject { ["1"] = originalDay } };
        SoloTaskSettingsViewModel settings = domain
            ? new AutoDomainSettingsViewModel(original)
            : new AutoLeyLineOutcropSettingsViewModel(original);
        var changes = new JObject();
        settings.CollectChanges(changes);
        Assert.Empty(changes);

        if (settings is AutoDomainSettingsViewModel domainSettings) domainSettings.Weekdays[1].PartyName = "新队伍";
        if (settings is AutoLeyLineOutcropSettingsViewModel leySettings) leySettings.Weekdays[1].Country = "璃月";
        settings.CollectChanges(changes);
        var weekly = Assert.IsType<JObject>(changes["weekday_overrides"]);
        Assert.Single(weekly);
        Assert.False(weekly["1"]!.Value<bool>("enabled"));
        Assert.Equal(2, weekly["1"]!["resin_usage"]!.Value<int>("run_count"));
        Assert.Equal("原队伍", originalDay.Value<string>("party_name"));
    }

    /// <summary>关闭每周安排只修改开关，原配置继续保留，重新开启后恢复。</summary>
    [Fact]
    public void DomainWeekly_DisablingDoesNotDiscardConfiguredDays()
    {
        var original = new JObject { ["weekday_overrides"] = new JObject
            { ["1"] = new JObject { ["domain_name"] = "测试秘境", ["party_name"] = "周一队伍" } } };
        var settings = new AutoDomainSettingsViewModel(original) { WeeklyEnabled = false };
        var changes = new JObject();
        settings.CollectChanges(changes);
        Assert.Single(changes);
        Assert.False(changes.Value<bool>(PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter));
        original.Merge(changes);
        var reopened = new AutoDomainSettingsViewModel(original);
        Assert.False(reopened.WeeklyEnabled);
        Assert.Equal("周一队伍", reopened.Weekdays[1].PartyName);
        reopened.WeeklyEnabled = true;
        changes = new JObject();
        reopened.CollectChanges(changes);
        Assert.Single(changes);
        Assert.True(changes.Value<bool>(PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter));
    }

    /// <summary>树脂使用卡片保持嵌套对象合同，未修改时不生成覆盖。</summary>
    [Fact]
    public void ResinUsage_GenericCardKeepsNestedObjectContract()
    {
        var value = JObject.Parse(SoloTaskJson.Serialize(ResinCapabilities.Domain.Defaults));
        var schema = PuloniaOneDragonSettingChoices.Decorate("resin_usage",
            PuloniaSoloTaskSchema.Create(typeof(ResinUsage)), value, "solo.auto_domain");
        Assert.Equal("solo.auto_domain", schema.Value<string>("x-resin-usage"));
        var field = new PuloniaOneDragonParameterFieldViewModel("resin_usage", schema, value);
        Assert.False(field.HasChanges());
        Assert.True(field.IsResinUsage);
        Assert.Empty(field.Children);
        var edited = field.Resin!.Value;
        edited.RunCount = 6;
        field.Resin.Reset(edited);
        Assert.Equal(6, Assert.IsType<JObject>(field.BuildValue()).Value<int>("run_count"));
    }

    /// <summary>累计上限与单次上限共同生效；耗尽方式转为指定次数后保留补充开关，且不修改共享原值。</summary>
    [Theory]
    [InlineData(false, 1, 10, 7, true, 3)]
    [InlineData(true, 2, 10, 7, true, 2)]
    [InlineData(true, 5, 10, 7, true, 3)]
    [InlineData(true, 5, 10, 10, false, 5)]
    [InlineData(true, 5, 0, 10, true, 5)]
    public void BossLimit_RespectsRemainingAndPerRunLimits(bool specified, int runCount, int limit,
        int completed, bool expectedRun, int expectedCount)
    {
        // 此方法只操作次数字段，绕过依赖正在运行应用的语言服务构造函数。
        var boss = (AutoBossParam)RuntimeHelpers.GetUninitializedObject(typeof(AutoBossParam));
        boss.ResinUsage = ResinCapabilities.Boss.Defaults;
        boss.SpecifyRunCount = specified;
        boss.RunCount = runCount;
        boss.UseTransientResin = true;
        boss.UseFragileResin = true;
        var shared = boss.ResinUsage;
        var snapshot = shared.Clone();
        Assert.Equal(expectedRun, PuloniaSoloTaskExecutor.ApplyBossRewardLimit(boss, limit, completed));
        Assert.Equal(snapshot, shared);
        Assert.Equal(expectedCount, boss.RunCount);
        Assert.True(boss.UseTransientResin);
        Assert.True(boss.UseFragileResin);
        Assert.Equal(expectedRun && limit > 0 ? ResinUsageMode.RunCount : snapshot.Mode, boss.ResinUsage.Mode);

    }

    /// <summary>累计跨存储实例持久化，并按任务、账号与清空批次分别统计；清空仅生成草稿。</summary>
    [Fact]
    public async Task BossProgress_PersistsAndSeparatesTasksAccountsAndResetDrafts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));
        try
        {
            using (var store = new PuloniaTaskStore(directory))
            {
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-a");
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-a");
                await store.IncrementBossRewardCountAsync("plan", "task", "", "account-b");
            }
            using var reopened = new PuloniaTaskStore(directory);
            var counts = await reopened.ReadBossRewardCountsAsync("plan", "task", "");
            Assert.Equal(2, counts["account-a"]);
            Assert.Equal(1, counts["account-b"]);
            Assert.Empty(await reopened.ReadBossRewardCountsAsync("plan", "other-task", ""));
            Assert.Empty(await reopened.ReadBossRewardCountsAsync("plan", "task", "new-generation"));
            var settings = new AutoBossSettingsViewModel(new JObject(), reopened);
            await settings.SetProgressScopeAsync("plan", "task");
            Assert.Contains("account-a：2 次", settings.CompletedRunCountText);
            settings.ResetProgressCommand.Execute(null);
            var changes = new JObject();
            settings.CollectChanges(changes);
            Assert.NotEmpty(changes.Value<string>(PuloniaSoloTaskExecutor.BossRewardCountResetParameter)!);
            Assert.Equal(2, (await reopened.ReadBossRewardCountsAsync("plan", "task", ""))["account-a"]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
