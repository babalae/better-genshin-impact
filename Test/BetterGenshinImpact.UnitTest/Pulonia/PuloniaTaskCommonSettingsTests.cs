using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 校验配置组表单接入后的参数类型、继承、持久化与恢复继承，不启动游戏或 WPF 窗口。
/// </summary>
public sealed class PuloniaTaskCommonSettingsTests : IDisposable
{
    /// <summary>
    /// 当前测试独占的资源与存储目录。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-settings-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 新增的完整默认配置必须符合执行器声明，原有参数名继续可用。
    /// </summary>
    [Fact]
    public void ExecutorDefaults_MatchSchemasAndKeepLegacyNames()
    {
        IPuloniaTaskExecutor[] executors =
            [new PuloniaPathingTaskExecutor(), new PuloniaJavaScriptTaskExecutor(), new PuloniaShellTaskExecutor()];
        foreach (var definition in executors.SelectMany(executor => executor.Definitions))
        {
            PuloniaTaskValidator.ValidateParameterSchema(definition.ParameterSchema, definition.TaskType);
            PuloniaTaskValidator.ValidateParameters(definition.DefaultParameters, definition.ParameterSchema, definition.TaskType);
            if (definition.TaskType == "shell")
                continue;
            Assert.Equal("", definition.DefaultParameters.Value<string>("party_name"));
            Assert.True(definition.DefaultParameters.Value<bool>("auto_pick_enabled"));
            Assert.Null(definition.DefaultParameters["task_cycle_config"]);
            Assert.Null(definition.DefaultParameters["avatar_index_list"]);
        }
    }

    /// <summary>
    /// 战斗嵌套配置、显式 false、枚举、空食物名称与运行跳队开关均需无损往返。
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
        config.PathingConfig.AutoFightConfig.StrategyName = "测试战斗策略";
        config.PathingConfig.AutoFightConfig.FinishDetectConfig.FastCheckEnabled = true;
        config.PathingConfig.AutoFightConfig.BattleThresholdForLoot = null;
        config.PathingConfig.AutoEatConfig.DefaultAtkBoostingDishName = null;
        var parameters = PuloniaTaskCommonSettings.ToParameters("pathing", config);
        var definition = new PuloniaPathingTaskExecutor().Definitions[0];
        PuloniaTaskValidator.ValidateParameters(parameters, definition.ParameterSchema, "pathing");

        var restored = PuloniaTaskCommonSettings.FromParameters("pathing", parameters).PathingConfig;
        Assert.Equal("中文采集队", restored.PartyName);
        Assert.Equal("2", restored.MainAvatarIndex);
        Assert.False(restored.AutoPickEnabled);
        Assert.True(restored.AutoEatEnabled);
        Assert.True(restored.SkipPartySwitch);
        Assert.Equal(RecoverTiming.Never, restored.RecoverTiming);
        Assert.Equal("测试战斗策略", restored.AutoFightConfig.StrategyName);
        Assert.True(restored.AutoFightConfig.FinishDetectConfig.FastCheckEnabled);
        Assert.Null(restored.AutoFightConfig.BattleThresholdForLoot);
        Assert.Null(restored.AutoEatConfig.DefaultAtkBoostingDishName);
    }

    /// <summary>
    /// 跨 JS 资源覆盖仅接受宿主公共设置，不能夹带任意脚本参数或错误类型。
    /// </summary>
    [Fact]
    public void JavaScriptCommonScope_RejectsCustomOrInvalidParameters()
    {
        PuloniaTaskValidator.ValidateScope("javascript", null, 1,
            new JObject { ["party_name"] = "公共队伍", ["auto_pick_enabled"] = false }, "group");
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateScope("javascript", null, 1,
            new JObject { ["settings"] = new JObject { ["party_name"] = "脚本私有值" } }, "group"));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateScope("javascript", null, 1,
            new JObject { ["auto_pick_enabled"] = "false" }, "group"));
        PuloniaTaskValidator.ValidateScope("javascript", "script-a", 1,
            new JObject { ["settings"] = new JObject { ["custom"] = true } }, "group");
    }

    /// <summary>
    /// 分组设置经过保存与运行构建仍按祖先、资源和叶子顺序继承，空值和 false 能覆盖父组。
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
            await File.WriteAllTextAsync(Path.Combine(resource, "main.js"), "// 测试资源，不实际执行。");
        }
        var plan = new PuloniaTaskPlan();
        plan.RootTask.ParameterOverrides.Add(new PuloniaTaskParameterOverride
        {
            TaskType = taskType,
            Values = new JObject { ["party_name"] = "父组队伍", ["auto_eat_enabled"] = true }
        });
        plan.RootTask.Children.Add(new PuloniaTask { Name = "继承", TaskType = taskType, Path = resource });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "覆盖", TaskType = taskType, Path = resource,
            Parameters = new JObject { ["party_name"] = "", ["auto_eat_enabled"] = false }
        });
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var saved = await store.SavePlanAsync(plan);
        var loaded = await store.LoadPlanAsync(saved.Id);
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(loaded!, new PuloniaTaskBuildOptions
        {
            Definitions = [.. new PuloniaPathingTaskExecutor().Definitions, .. new PuloniaJavaScriptTaskExecutor().Definitions]
        });
        var inherited = PuloniaTaskCommonSettings.FromParameters(taskType, snapshot.RootTask.Children[0].Parameters).PathingConfig;
        var overridden = PuloniaTaskCommonSettings.FromParameters(taskType, snapshot.RootTask.Children[1].Parameters).PathingConfig;
        Assert.Equal("父组队伍", inherited.PartyName);
        Assert.True(inherited.AutoEatEnabled);
        Assert.Equal("", overridden.PartyName);
        Assert.False(overridden.AutoEatEnabled);
        Assert.EndsWith(plan.RootTask.Id, snapshot.RootTask.Children[0].ParameterSources["party_name"]);
    }

    /// <summary>
    /// 分组禁用 Shell 后不启动进程、不重试，也不阻止后续纯 C# 节点执行。
    /// </summary>
    [Fact]
    public async Task DisabledShell_IsSkippedAndFollowingTaskContinues()
    {
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var plan = new PuloniaTaskPlan();
        plan.RootTask.ParameterOverrides.Add(new PuloniaTaskParameterOverride
        {
            TaskType = "shell",
            Values = new JObject { ["shell_config_enabled"] = true, ["shell_disabled"] = true }
        });
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
    /// 表单只保存变化字段，恢复继承保留脚本私有设置，撤销可以恢复删除前的覆盖。
    /// </summary>
    [Fact]
    public void DocumentEdits_PreserveUnrelatedValuesAndSupportUndo()
    {
        var leaf = new PuloniaTask
        {
            TaskType = "javascript", Path = "example",
            Parameters = new JObject { ["settings"] = new JObject { ["custom"] = 42 } }
        };
        var plan = new PuloniaTaskPlan();
        plan.RootTask.Children.Add(leaf);
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], new PuloniaTaskClipboardService(), false);
        var root = document.RootNode;
        var original = PuloniaTaskCommonSettings.CreateDefaults("javascript");
        var edited = (JObject)original.DeepClone();
        edited["party_name"] = "组内队伍";
        document.ApplyCommonSettings(root, "javascript", original, edited, false);
        Assert.Single(plan.RootTask.ParameterOverrides);
        Assert.Single(plan.RootTask.ParameterOverrides[0].Values);
        PuloniaTaskValidator.ValidatePlan(plan);

        var node = root.Children[0];
        Assert.Equal("组内队伍", document.GetCommonSettingsParameters(node, "javascript").Value<string>("party_name"));
        edited["party_name"] = "节点队伍";
        document.ApplyCommonSettings(node, "javascript", original, edited, false);
        Assert.Equal(42, leaf.Parameters["settings"]!.Value<int>("custom"));
        document.ApplyCommonSettings(node, "javascript", original, edited, true);
        Assert.Null(leaf.Parameters["party_name"]);
        Assert.Equal(42, leaf.Parameters["settings"]!.Value<int>("custom"));
        Assert.Equal("组内队伍", document.GetCommonSettingsParameters(node, "javascript").Value<string>("party_name"));
        document.UndoCommand.Execute(null);
        Assert.Equal("节点队伍", document.RootNode.Children[0].Model.Parameters.Value<string>("party_name"));
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
