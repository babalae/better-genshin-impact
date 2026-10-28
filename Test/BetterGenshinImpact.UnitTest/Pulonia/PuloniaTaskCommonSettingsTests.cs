using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 校验地图追踪、JS 与 Shell 宿主参数的类型、共享设置绑定、继承、持久化与恢复继承，不启动游戏或 WPF 窗口。
/// </summary>
public sealed class PuloniaTaskCommonSettingsTests : IDisposable
{
    /// <summary>
    /// 当前测试独占的资源与存储目录。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 战斗嵌套配置、显式 false、枚举、空食物名称、运行跳队开关与条件选队均需无损往返；总开关固定开启。
    /// </summary>
    [Fact]
    public void ConfigurationRoundTrip_PreservesNestedValuesAndNulls()
    {
        var config = new ScriptGroupConfig();
        config.PathingConfig.PartyName = "中文采集队";
        config.PathingConfig.MainAvatarIndex = "2";
        config.PathingConfig.AutoPickEnabled = false;
        config.PathingConfig.AutoEatEnabled = true;
        config.PathingConfig.RecoverTiming = RecoverTiming.Never;
        config.PathingConfig.SkipPartySwitch = true;
        config.PathingConfig.PartyByCondition = true;
        config.PathingConfig.AutoFightConfig.StrategyName = "测试战斗策略";
        config.PathingConfig.AutoFightConfig.FinishDetectConfig.FastCheckEnabled = true;
        config.PathingConfig.AutoFightConfig.BattleThresholdForLoot = null;
        config.PathingConfig.AutoEatConfig.DefaultAtkBoostingDishName = null;
        var parameters = PuloniaTaskCommonSettings.ToParameters("pathing", config);
        var definition = new PuloniaPathingTaskExecutor(new AllConfig()).Definitions[0];
        PuloniaTaskValidator.ValidateParameters(parameters, definition.ParameterSchema, "pathing");

        var restored = PuloniaTaskCommonSettings.FromParameters("pathing", parameters).PathingConfig;
        Assert.Equal("中文采集队", restored.PartyName);
        Assert.Equal("2", restored.MainAvatarIndex);
        Assert.False(restored.AutoPickEnabled);
        Assert.True(restored.AutoEatEnabled);
        Assert.True(restored.SkipPartySwitch);
        Assert.True(restored.PartyByCondition);
        Assert.True(restored.Enabled);
        Assert.True(restored.AutoFightEnabled);
        Assert.Equal(RecoverTiming.Never, restored.RecoverTiming);
        Assert.Equal("测试战斗策略", restored.AutoFightConfig.StrategyName);
        Assert.True(restored.AutoFightConfig.FinishDetectConfig.FastCheckEnabled);
        Assert.Null(restored.AutoFightConfig.BattleThresholdForLoot);
        Assert.Null(restored.AutoEatConfig.DefaultAtkBoostingDishName);
    }

    /// <summary>
    /// 共享设置只接受目录中的设置项和符合值类型的值。
    /// </summary>
    [Fact]
    public void SharedSettings_RejectUnknownOrInvalidValues()
    {
        PuloniaTaskValidator.ValidateSettings(new JObject
        {
            ["party.party_name"] = "公共队伍", ["pickup.auto_pick"] = false, ["fight_end.fast_check_enabled"] = true
        }, "group");
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateSettings(
            new JObject { ["settings"] = new JObject { ["party_name"] = "脚本私有值" } }, "group"));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateSettings(
            new JObject { ["pickup.auto_pick"] = "false" }, "group"));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateSettings(
            new JObject { ["party.source"] = "random" }, "group"));
    }

    /// <summary>
    /// 分组共享设置经过保存与运行构建仍按祖先到叶子的顺序继承，空值和 false 能覆盖父组。
    /// </summary>
    [Theory]
    [InlineData("pathing")]
    [InlineData("javascript")]
    public async Task GroupSettings_SurvivePersistenceAndReachPreparedTasks(string taskType)
    {
        Directory.CreateDirectory(_directory);
        var resource = Path.Combine(_directory, "route.json");
        await File.WriteAllTextAsync(resource, "{}");
        if (taskType == "javascript")
        {
            resource = Path.Combine(_directory, "script");
            Directory.CreateDirectory(resource);
            await File.WriteAllTextAsync(Path.Combine(resource, "manifest.json"), "{\"name\":\"测试资源\",\"version\":\"1.0\"}");
            await File.WriteAllTextAsync(Path.Combine(resource, "main.js"), "// 测试资源，不实际执行。");
        }
        var plan = new PuloniaTaskPlan();
        plan.RootTask.Settings = new JObject { ["party.party_name"] = "父组队伍", ["eat.auto_eat"] = true };
        plan.RootTask.Children.Add(new PuloniaTask { Name = "继承", TaskType = taskType, Path = resource });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "覆盖", TaskType = taskType, Path = resource,
            Settings = new JObject { ["party.party_name"] = "", ["eat.auto_eat"] = false }
        });
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var saved = await store.SavePlanAsync(plan);
        var loaded = await store.LoadPlanAsync(saved.Id);
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(loaded!, new PuloniaTaskBuildOptions
        {
            Definitions = [.. new PuloniaPathingTaskExecutor(new AllConfig()).Definitions, .. new PuloniaJavaScriptTaskExecutor().Definitions]
        });
        var inherited = PuloniaTaskCommonSettings.FromParameters(taskType, snapshot.RootTask.Children[0].Parameters).PathingConfig;
        var overridden = PuloniaTaskCommonSettings.FromParameters(taskType, snapshot.RootTask.Children[1].Parameters).PathingConfig;
        Assert.Equal("父组队伍", inherited.PartyName);
        Assert.True(inherited.AutoEatEnabled);
        Assert.Equal("", overridden.PartyName);
        Assert.False(overridden.AutoEatEnabled);
        Assert.Equal("settings", snapshot.RootTask.Children[0].ParameterSources["party_name"]);
        Assert.EndsWith(plan.RootTask.Id, snapshot.RootTask.Children[0].SettingSources["party.party_name"]);
    }

    /// <summary>
    /// 分组禁用 Shell 后不启动进程、不重试，也不阻止后续纯 C# 节点执行。
    /// </summary>
    [Fact]
    public async Task DisabledShell_IsSkippedAndFollowingTaskContinues()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var plan = new PuloniaTaskPlan();
        plan.RootTask.Settings = new JObject { ["shell.disabled"] = true };
        plan.RootTask.Children.Add(new PuloniaTask
        {
            TaskType = "shell", Name = "禁用的 Shell",
            Parameters = new JObject { ["file_name"] = "must-not-start.exe", ["arguments"] = new JArray() },
            Policy = new PuloniaTaskPolicy { MaxRetries = 2 }
        });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            TaskType = "csharp", Name = "后续计算",
            Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) }
        });
        var saved = await store.SavePlanAsync(plan);
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaShellTaskExecutor(), new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));
        var requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = saved.Id });
        var run = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(PuloniaTaskNodeStatus.Skipped, Assert.Single(run.NodeResults.Where(node => node.TaskType == "shell")).Status);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, Assert.Single(run.NodeResults.Where(node => node.TaskType == "csharp")).Status);
    }

    /// <summary>
    /// 旧配置组设置转为共享设置时只保留与默认值不同的项；行走总开关关闭时改为条件选队并固定当时的全局战斗配置。
    /// </summary>
    [Fact]
    public void LegacyGroupConfig_ConvertsOnlyChangedValues()
    {
        var global = new AllConfig();
        global.AutoFightConfig.StrategyName = "全局策略";

        var enabled = new ScriptGroupConfig();
        enabled.PathingConfig.PartyName = "采集队";
        var notes = new List<string>();
        var settings = PuloniaLegacySettingsConverter.FromGroupConfig(enabled, global, true, notes);
        Assert.Equal("采集队", settings.Value<string>("party.party_name"));
        Assert.Single(settings);
        Assert.Empty(notes);

        var disabled = new ScriptGroupConfig();
        disabled.PathingConfig.Enabled = false;
        disabled.PathingConfig.PartyName = "被忽略的队伍";
        settings = PuloniaLegacySettingsConverter.FromGroupConfig(disabled, global, true, notes);
        Assert.Equal("condition", settings.Value<string>(PuloniaSettingCatalog.PartySourceKey));
        Assert.Equal("全局策略", settings.Value<string>("fight.strategy_name"));
        Assert.Null(settings["party.party_name"]);
        Assert.Single(notes);
    }

    /// <summary>
    /// 删除当前测试创建的独占临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
