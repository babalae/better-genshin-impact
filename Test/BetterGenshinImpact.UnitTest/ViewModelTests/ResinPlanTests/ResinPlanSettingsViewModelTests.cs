using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.UnitTest.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ViewModelTests.ResinPlanTests;

/// <summary>体力计划设置页与子任务设置弹窗的草稿行为，不创建 WPF 窗口。</summary>
public sealed class ResinPlanSettingsViewModelTests
{
    /// <summary>打开设置不产生修改；开关子任务时整体写回对象并保留参数。</summary>
    [Fact]
    public void Settings_OnlyWriteChangedFieldsAndKeepSubTaskParameters()
    {
        var defaults = PuloniaSoloTestDefinitions.ResinPlan().DefaultParameters;
        var settings = new ResinPlanSettingsViewModel(defaults);
        Assert.Empty(Changes(settings));
        Assert.False(settings.LimitResin);
        Assert.False(settings.Boss.Enabled);
        Assert.True(settings.Domain.Enabled);

        settings.LimitResin = true;
        settings.ResinLimit = 120;
        settings.Boss.Enabled = true;
        var changes = Changes(settings);

        Assert.Equal(120, changes.Value<int>("max_resin"));
        var boss = Assert.IsType<JObject>(changes["boss"]);
        Assert.True(boss.Value<bool>("enabled"));
        Assert.True(JToken.DeepEquals(defaults["boss"]!["param"], boss["param"]));
        Assert.False(changes.ContainsKey("domain"));
    }

    /// <summary>关闭限定用量写回 0；修改合成地区只改 country，其余合成字段保留。</summary>
    [Fact]
    public void Settings_ResinLimitOffAndCraftCountry()
    {
        var original = PuloniaSoloTestDefinitions.ResinPlan().DefaultParameters;
        original["max_resin"] = 200;
        original["craft"]!["min_resin_to_keep"] = 40;
        var settings = new ResinPlanSettingsViewModel(original) { LimitResin = false, CraftCountry = "璃月" };

        var changes = Changes(settings);

        Assert.Equal(0, changes.Value<int>("max_resin"));
        Assert.Equal("璃月", changes["craft"]!.Value<string>("country"));
        Assert.Equal(40, changes["craft"]!.Value<int>("min_resin_to_keep"));
    }

    /// <summary>子任务弹窗隐藏体力计划决定的字段，未修改时返回原参数。</summary>
    [Fact]
    public void SubTaskDialog_HidesControlledFields()
    {
        var parameters = new JObject { ["party_name"] = "养成队", ["domain_name"] = "某秘境", ["revive_retry_count"] = 3 };
        var dialog = new ResinPlanSubTaskSettingsViewModel(ResinPlanSubTaskSpec.Domain,
            PuloniaSoloTaskSchema.Create(typeof(AutoDomainParam)), parameters);

        var names = dialog.Fields.Select(field => field.Name).ToArray();
        Assert.DoesNotContain("resin_usage", names);
        Assert.DoesNotContain("domain_name", names);
        Assert.DoesNotContain("reward_recognition_enabled", names);
        Assert.Contains("party_name", names);
        Assert.Contains("training_guide_fallback_domain_name", names);
        Assert.True(JToken.DeepEquals(parameters, dialog.TryBuild()));
    }

    /// <summary>首领弹窗修改战斗策略时同时写入任务优先读取的策略路径，与专属设置页一致。</summary>
    [Fact]
    public void SubTaskDialog_BossStrategyAlsoWritesStrategyPath()
    {
        var parameters = new JObject { ["strategy_name"] = "根据队伍自动选择", ["combat_strategy_path"] = "" };
        var dialog = new ResinPlanSubTaskSettingsViewModel(ResinPlanSubTaskSpec.Boss,
            PuloniaSoloTaskSchema.Create(typeof(AutoBossParam)), parameters);
        Assert.DoesNotContain(dialog.Fields, field => field.Name == "combat_strategy_path");

        dialog.Fields.Single(field => field.Name == "strategy_name").TextValue = "测试策略";
        var result = dialog.TryBuild();

        Assert.NotNull(result);
        Assert.Equal("测试策略", result.Value<string>("strategy_name"));
        Assert.Equal("测试策略", result.Value<string>("combat_strategy_path"));
        Assert.Null(dialog.ValidationMessage);
    }

    /// <summary>首领沿用自己的树脂设置：卡片摘要显示讨伐次数，弹窗中的树脂卡片去掉须臾、脆弱补充，未修改时保留原值。</summary>
    [Fact]
    public void Boss_UsesOwnResinUsageForRunCount()
    {
        var resin = new ResinUsage
        {
            Mode = ResinUsageMode.RunCount, RunCount = 3, Allowed = new() { Original = true, Transient = true }
        };
        var parameters = new JObject { ["boss_name"] = "急冻树", ["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(resin)) };
        var original = PuloniaSoloTestDefinitions.ResinPlan().DefaultParameters;
        original["boss"]!["param"] = parameters;

        var settings = new ResinPlanSettingsViewModel(original);
        Assert.Contains("3 次", settings.Boss.Summary);
        Assert.DoesNotContain("须臾", settings.Boss.Summary);

        var dialog = new ResinPlanSubTaskSettingsViewModel(ResinPlanSubTaskSpec.Boss,
            PuloniaSoloTaskSchema.Create(typeof(AutoBossParam)), parameters);
        var field = dialog.Fields.Single(item => item.Name == "resin_usage");
        Assert.True(field.IsResinUsage);
        Assert.False(field.Resin!.Value.Allowed.Transient);
        Assert.True(JToken.DeepEquals(parameters, dialog.TryBuild()));
    }

    /// <summary>收集设置页的变化字段。</summary>
    private static JObject Changes(SoloTaskSettingsViewModel settings)
    {
        var changes = new JObject();
        settings.CollectChanges(changes);
        return changes;
    }
}
