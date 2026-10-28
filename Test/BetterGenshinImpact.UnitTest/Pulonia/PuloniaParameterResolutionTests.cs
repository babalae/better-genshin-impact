using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 固定共享设置的设置链顺序、深写入与来源，计划引用的传播顺序，运行准备的报错，以及界面预览与运行准备一致。
/// </summary>
public sealed class PuloniaParameterResolutionTests : IDisposable
{
    /// <summary>测试使用的纯数据能力类型，不需要资源文件或执行器。</summary>
    private const string EchoType = "sample.echo";

    /// <summary>入口计划中有自身覆盖的叶子的运行地址。</summary>
    private const string LeafAAddress = "pln-main/tsk-root/tsk-outer/tsk-inner/tsk-leaf-a";

    /// <summary>入口计划中没有自身覆盖的叶子的运行地址。</summary>
    private const string LeafBAddress = "pln-main/tsk-root/tsk-outer/tsk-inner/tsk-leaf-b";

    /// <summary>被引用计划中叶子的运行地址。</summary>
    private const string ChildLeafAddress = "pln-main/tsk-root/tsk-ref/@pln-child/tsk-child-root/tsk-child-group/tsk-child-leaf";

    /// <summary>当前测试独占的存储目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 能力默认值 → 共享设置（由外到内，每层先方案后自身，写入参数路径末端）→ 任务参数 → 调用覆盖；
    /// 同一对象参数的不同子项来自不同层，每项来源都按设置链记录。
    /// </summary>
    [Fact]
    public async Task Builder_AppliesSettingChainInFixedOrder()
    {
        using var store = await CreateStoreAsync();
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(CreateMainPlan(), new PuloniaTaskBuildOptions
        {
            Definitions = CreateDefinitions(),
            ParameterOverrides = new Dictionary<string, JObject> { [LeafAAddress] = new() { ["note"] = "call" } }
        });

        // 1. 有自身覆盖的叶子：方案在外层分组生效，内层分组和叶子自身覆盖更近，调用覆盖只改任务参数
        var leafA = Find(snapshot.RootTask, LeafAAddress);
        AssertParameters(leafA, new JObject
        {
            ["note"] = "call", ["party_name"] = "profile-party", ["auto_run_enabled"] = false,
            ["fight"] = new JObject { ["StrategyName"] = "leaf-strategy", ["Timeout"] = 200 }
        });
        AssertSources(leafA.ParameterSources, new Dictionary<string, string>
        {
            ["note"] = "call/" + LeafAAddress, ["party_name"] = "settings", ["auto_run_enabled"] = "settings", ["fight"] = "settings"
        });
        AssertSources(leafA.SettingSources, new Dictionary<string, string>
        {
            ["party.party_name"] = "profile/prf-a@pln-main/tsk-root/tsk-outer",
            ["travel.auto_run"] = "node/pln-main/tsk-root/tsk-outer",
            ["fight.strategy_name"] = "node/" + LeafAAddress,
            ["fight.timeout"] = "node/pln-main/tsk-root/tsk-outer/tsk-inner"
        });

        // 2. 没有自身覆盖的叶子：全部继承上层，未设置的项为默认值
        var leafB = Find(snapshot.RootTask, LeafBAddress);
        AssertParameters(leafB, new JObject
        {
            ["note"] = "default", ["party_name"] = "profile-party", ["auto_run_enabled"] = false,
            ["fight"] = new JObject { ["StrategyName"] = "profile-strategy", ["Timeout"] = 200 }
        });
        Assert.Equal("default/" + EchoType, leafB.ParameterSources["note"]);

        // 3. 引用计划叶子：外部祖先 → 被引用计划根 → 引用节点 → 内部节点；引用节点替换被引用计划的计划级设置，内部设置仍优先
        var child = Find(snapshot.RootTask, ChildLeafAddress);
        AssertParameters(child, new JObject
        {
            ["note"] = "child-node", ["party_name"] = "ref-party", ["auto_run_enabled"] = true,
            ["fight"] = new JObject { ["StrategyName"] = "child-group-strategy", ["Timeout"] = 90 }
        });
        AssertSources(child.SettingSources, new Dictionary<string, string>
        {
            ["party.party_name"] = "node/pln-main/tsk-root/tsk-ref",
            ["travel.auto_run"] = "node/pln-main/tsk-root/tsk-ref/@pln-child/tsk-child-root",
            ["fight.strategy_name"] = "node/pln-main/tsk-root/tsk-ref/@pln-child/tsk-child-root/tsk-child-group",
            ["fight.timeout"] = "node/pln-main/tsk-root/tsk-ref/@pln-child/tsk-child-root"
        });

        // 4. 禁用节点不解析设置链，只保留自身参数；快照固定实际使用的方案
        var off = Find(snapshot.RootTask, "pln-main/tsk-root/tsk-off");
        AssertParameters(off, new JObject { ["note"] = "off" });
        Assert.Empty(off.SettingSources);
        Assert.Equal(new[] { "prf-a" }, snapshot.ProfileJsonById.Keys);
        Assert.Empty(snapshot.PresetJsonById);
    }

    /// <summary>
    /// 运行准备遇到方案缺失、任务参数或调用覆盖直接写共享设置、叶子使用能力不用的设置项、未知设置项或调用覆盖未消费时明确报错。
    /// </summary>
    [Fact]
    public async Task Builder_RejectsMissingProfilesAndSettingConflicts()
    {
        using var store = await CreateStoreAsync();
        var builder = new PuloniaTaskBuilder(store);
        var options = new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() };

        var missing = CreateMainPlan();
        FindTask(missing.RootTask, "tsk-outer").ProfileId = "prf-missing";
        var missingError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(missing, options));
        Assert.Equal("pln-main/tsk-root/tsk-outer：设置方案 prf-missing 不存在。", missingError.Message);

        var conflict = CreateMainPlan();
        FindTask(conflict.RootTask, "tsk-leaf-b").Parameters["party_name"] = "x";
        var conflictError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(conflict, options));
        Assert.Equal(LeafBAddress + "：任务参数 party_name 属于共享设置，请在共享设置中修改。", conflictError.Message);

        var unused = CreateMainPlan();
        FindTask(unused.RootTask, "tsk-leaf-b").Settings["shell.timeout_seconds"] = 5;
        var unusedError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(unused, options));
        Assert.Equal(LeafBAddress + "：任务类型 sample.echo 不使用共享设置 shell.timeout_seconds。", unusedError.Message);

        var unknown = CreateMainPlan();
        unknown.RootTask.Settings["nope.key"] = 1;
        var unknownError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(unknown, options));
        Assert.Contains("不存在的共享设置项", unknownError.Message);

        var callConflict = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(CreateMainPlan(),
            new PuloniaTaskBuildOptions
            {
                Definitions = CreateDefinitions(),
                ParameterOverrides = new Dictionary<string, JObject> { [LeafBAddress] = new() { ["fight"] = new JObject() } }
            }));
        Assert.Equal(LeafBAddress + "：调用覆盖不能直接修改共享设置覆盖的参数 fight。", callConflict.Message);

        var unusedCall = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => builder.BuildAsync(CreateMainPlan(),
            new PuloniaTaskBuildOptions
            {
                Definitions = CreateDefinitions(),
                ParameterOverrides = new Dictionary<string, JObject> { ["pln-main/tsk-root/tsk-off"] = new() { ["note"] = 1 } }
            }));
        Assert.Equal("pln-main/tsk-root/tsk-off：调用覆盖未指向启用的具体任务。", unusedCall.Message);
    }

    /// <summary>
    /// 没有调用覆盖时，界面预览与运行准备对同一节点得到完全相同的有效参数。
    /// </summary>
    [Fact]
    public async Task Preview_MatchesBuilder()
    {
        using var store = await CreateStoreAsync();
        var plan = CreateMainPlan();
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() });
        var document = CreateDocument(plan);

        foreach (var (taskId, address) in new[] { ("tsk-leaf-a", LeafAAddress), ("tsk-leaf-b", LeafBAddress) })
        {
            var node = document.EnumerateNodes().Single(item => item.Id == taskId);
            var preview = document.GetOneDragonParameters(node);
            Assert.True(JToken.DeepEquals(Find(snapshot.RootTask, address).Parameters, preview), $"{taskId} 预览：{preview}");
        }
    }

    /// <summary>
    /// 方案缺失时界面预览不抛异常，跳过该方案并在清单摘要中提示。
    /// </summary>
    [Fact]
    public void Preview_SkipsMissingProfileAndWarns()
    {
        var plan = CreateMainPlan();
        FindTask(plan.RootTask, "tsk-outer").ProfileId = "prf-missing";
        var document = CreateDocument(plan);
        var node = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-b");

        Assert.Equal("root-party", document.GetOneDragonParameters(node).Value<string>("party_name"));
        Assert.Contains("⚠ 设置方案缺失", document.GetOneDragonConfigurationSummary(node));
        Assert.True(document.EnumerateNodes().Single(item => item.Id == "tsk-outer").IsProfileMissing);
    }

    /// <summary>
    /// 编辑界面的来源文本：顶层参数显示层级，设置项显示具体节点、方案与“本节点”，并能列出整条链。
    /// </summary>
    [Fact]
    public void Preview_DescribesSettingSourcesWithDisplayNames()
    {
        var document = CreateDocument(CreateMainPlan());
        var leaf = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-a");

        var parameterSources = document.GetEffectiveParameterSources(leaf).ToDictionary(item => item.Name, item => item.Source);
        Assert.Equal("本任务", parameterSources["note"]);
        Assert.Equal("共享设置", parameterSources["fight"]);

        var resolution = document.ResolveNodeSettings(leaf);
        Assert.Equal("方案「方案A」（分组「外层」）", document.DescribeSettingSource(resolution.Sources["party.party_name"], leaf));
        Assert.Equal("本节点", document.DescribeSettingSource(resolution.Sources["fight.strategy_name"], leaf));
        Assert.Equal("分组「内层」", document.DescribeSettingSource(resolution.Sources["fight.timeout"], leaf));
        Assert.Equal("默认", document.DescribeSettingSource(resolution.Sources["hurry.avatar"], leaf));

        var history = document.GetSettingHistory(leaf, "party.party_name");
        Assert.Equal(new[] { "默认", "计划", "方案「方案A」（分组「外层」）" }, history.Select(entry => entry.Location));
        Assert.True(history[^1].IsEffective);
    }

    /// <summary>
    /// 单项覆盖、恢复继承、清除冗余与选择方案都是可撤销编辑；同一项的连续输入合并为一次撤销。
    /// </summary>
    [Fact]
    public void Document_EditsSettingsWithUndo()
    {
        var document = CreateDocument(CreateMainPlan());
        var inner = document.EnumerateNodes().Single(item => item.Id == "tsk-inner");
        var leaf = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-b");

        // 1. 连续修改同一项只占一次撤销
        document.SetSetting(inner, "party.party_name", "a");
        document.SetSetting(inner, "party.party_name", "ab");
        Assert.Equal("ab", document.GetOneDragonParameters(leaf).Value<string>("party_name"));
        Assert.Equal("分组「内层」", document.DescribeSettingSource(document.ResolveNodeSettings(leaf).Sources["party.party_name"], leaf));
        document.UndoCommand.Execute(null);
        inner = document.EnumerateNodes().Single(item => item.Id == "tsk-inner");
        leaf = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-b");
        Assert.False(inner.Model.Settings.ContainsKey("party.party_name"));
        Assert.Equal("profile-party", document.GetOneDragonParameters(leaf).Value<string>("party_name"));

        // 2. 值无效时拒绝，不修改模型
        Assert.Throws<PuloniaTaskValidationException>(() => document.SetSetting(inner, "fight.timeout", "很久"));

        // 3. 与继承值相同的覆盖可以一次清除；恢复继承只移除自身覆盖
        document.SetSetting(inner, "travel.auto_run", false);
        Assert.Equal(1, document.RemoveRedundantSettings(inner));
        Assert.False(inner.Model.Settings.ContainsKey("travel.auto_run"));
        Assert.True(inner.Model.Settings.ContainsKey("fight.timeout"));
        document.ResetSetting(inner, "fight.timeout");
        Assert.Equal(150, document.GetOneDragonParameters(leaf)["fight"]!.Value<int>("Timeout"));

        // 4. 选择方案即时生效，去掉方案后回到上层值
        var outer = document.EnumerateNodes().Single(item => item.Id == "tsk-outer");
        document.SelectProfile(outer, null);
        Assert.Equal("root-party", document.GetOneDragonParameters(leaf).Value<string>("party_name"));
        Assert.False(outer.HasProfile);
    }

    /// <summary>
    /// 分组设置页列出子树中任务实际使用的设置项与各类别影响的任务数；叶子只列出其能力绑定的设置项。
    /// </summary>
    [Fact]
    public void Document_ReportsSettingUsage()
    {
        var document = CreateDocument(CreateMainPlan());
        var inner = document.EnumerateNodes().Single(item => item.Id == "tsk-inner");
        var usage = document.GetSharedSettingUsage(inner);
        Assert.Equal(CreateDefinitions()[0].SettingBindings.Keys.Order(), usage.Keys!.Order());
        Assert.Equal(2, usage.CategoryCounts["fight"].Values.Sum());
        Assert.False(usage.CategoryCounts.ContainsKey("shell"));

        // 含计划引用时无法确定被引用计划使用哪些设置项，按全部设置项处理
        Assert.Null(document.GetSharedSettingUsage(document.RootNode).Keys);
    }

    /// <summary>
    /// 方案保存后修订递增；仍被已保存计划使用时拒绝删除，不再使用后可以删除。
    /// </summary>
    [Fact]
    public async Task Store_SavesAndGuardsProfiles()
    {
        using var store = await CreateStoreAsync();
        var list = await store.ListProfilesAsync();
        var profile = Assert.Single(list.Items);
        Assert.Equal(1, profile.Revision);

        var plan = CreateMainPlan();
        await store.SavePlanAsync(plan);
        var inUse = await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => store.DeleteProfileAsync("prf-a", profile.Revision));
        Assert.Contains("参数层级", inUse.Message);

        var saved = await store.LoadPlanAsync("pln-main");
        FindTask(saved!.RootTask, "tsk-outer").ProfileId = null;
        await store.SavePlanAsync(saved);
        await store.DeleteProfileAsync("prf-a", profile.Revision);
        Assert.Empty((await store.ListProfilesAsync()).Items);
    }

    /// <summary>
    /// 能力绑定的默认值必须与设置项默认值一致，否则注册时拒绝。
    /// </summary>
    [Fact]
    public void Validator_RejectsBindingWithDifferentDefault()
    {
        var definition = new PuloniaTaskDefinition
        {
            TaskType = EchoType,
            DefaultParameters = new JObject { ["party_name"] = "不同的默认值" },
            SettingBindings = new Dictionary<string, string> { ["party.party_name"] = "party_name" }
        };
        var error = Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateDefinitions([definition]));
        Assert.Contains("默认值与能力默认值不一致", error.Message);
        PuloniaTaskValidator.ValidateDefinitions(CreateDefinitions());
    }

    /// <summary>建立已加载测试方案与能力说明的文档。</summary>
    private static PuloniaTaskPlanDocumentViewModel CreateDocument(PuloniaTaskPlan plan)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(plan, CreateProfiles(), new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(CreateDefinitions());
        return document;
    }

    /// <summary>
    /// 建立保存了方案和被引用计划的独立存储。
    /// </summary>
    private async Task<PuloniaTaskStore> CreateStoreAsync()
    {
        var store = new PuloniaTaskStore(_directory);
        foreach (var profile in CreateProfiles())
            await store.SaveProfileAsync(profile);
        await store.SavePlanAsync(CreateChildPlan());
        return store;
    }

    /// <summary>
    /// 测试能力：一个任务参数 note，加四个绑定的共享设置；其中两个写入同一对象参数 fight 的不同子项。
    /// 默认值与设置目录默认值一致。
    /// </summary>
    private static List<PuloniaTaskDefinition> CreateDefinitions() =>
    [
        new()
        {
            TaskType = EchoType,
            DefaultParameters = new JObject
            {
                ["note"] = "default", ["party_name"] = "", ["auto_run_enabled"] = true,
                ["fight"] = new JObject { ["StrategyName"] = "根据队伍自动选择", ["Timeout"] = 120 }
            },
            SettingBindings = new Dictionary<string, string>
            {
                ["party.party_name"] = "party_name",
                ["travel.auto_run"] = "auto_run_enabled",
                ["fight.strategy_name"] = "fight.StrategyName",
                ["fight.timeout"] = "fight.Timeout"
            }
        }
    ];

    /// <summary>外层分组使用的设置方案。</summary>
    private static List<PuloniaSettingsProfile> CreateProfiles() =>
    [
        new()
        {
            Id = "prf-a", Name = "方案A",
            Settings = new JObject { ["party.party_name"] = "profile-party", ["fight.strategy_name"] = "profile-strategy" }
        }
    ];

    /// <summary>
    /// 入口计划：根、外层（挂方案）、内层三层设置，两个叶子、一个计划引用和一个禁用叶子。
    /// </summary>
    private static PuloniaTaskPlan CreateMainPlan()
    {
        var plan = new PuloniaTaskPlan { Id = "pln-main", Name = "参数层级" };
        plan.RootTask.Id = "tsk-root";
        plan.RootTask.Settings = new JObject { ["party.party_name"] = "root-party", ["fight.timeout"] = 150 };
        var inner = new PuloniaTask
        {
            Id = "tsk-inner", Name = "内层", Settings = new JObject { ["fight.timeout"] = 200 },
            Children =
            [
                new PuloniaTask
                {
                    Id = "tsk-leaf-a", Name = "叶子A", TaskType = EchoType,
                    Settings = new JObject { ["fight.strategy_name"] = "leaf-strategy" },
                    Parameters = new JObject { ["note"] = "node" }
                },
                new PuloniaTask { Id = "tsk-leaf-b", Name = "叶子B", TaskType = EchoType }
            ]
        };
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-outer", Name = "外层", ProfileId = "prf-a", Settings = new JObject { ["travel.auto_run"] = false },
            Children = [inner]
        });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-ref", Name = "引用", Source = new PuloniaTaskSource { Kind = "plan", PlanId = "pln-child" },
            Settings = new JObject { ["party.party_name"] = "ref-party", ["fight.strategy_name"] = "ref-strategy" }
        });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-off", Name = "禁用叶子", TaskType = EchoType, IsEnabled = false,
            Parameters = new JObject { ["note"] = "off" }
        });
        return plan;
    }

    /// <summary>
    /// 被引用计划：根提供计划级设置，内部分组覆盖战斗策略，叶子带任务参数。
    /// </summary>
    private static PuloniaTaskPlan CreateChildPlan()
    {
        var plan = new PuloniaTaskPlan { Id = "pln-child", Name = "被引用" };
        plan.RootTask.Id = "tsk-child-root";
        plan.RootTask.Settings = new JObject
        {
            ["party.party_name"] = "child-root-party", ["fight.timeout"] = 90, ["travel.auto_run"] = true
        };
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-child-group", Name = "内部分组", Settings = new JObject { ["fight.strategy_name"] = "child-group-strategy" },
            Children = [new PuloniaTask { Id = "tsk-child-leaf", Name = "引用叶子", TaskType = EchoType, Parameters = new JObject { ["note"] = "child-node" } }]
        });
        return plan;
    }

    /// <summary>按运行地址查找展开节点。</summary>
    private static PuloniaTaskPreparedTask Find(PuloniaTaskPreparedTask task, string address)
        => TryFind(task, address) ?? throw new InvalidOperationException("未找到节点 " + address);

    /// <summary>按运行地址查找展开节点，未找到时返回 null。</summary>
    private static PuloniaTaskPreparedTask? TryFind(PuloniaTaskPreparedTask task, string address)
        => task.TaskAddress == address
            ? task
            : task.Children.Select(child => TryFind(child, address)).FirstOrDefault(found => found is not null);

    /// <summary>按节点 ID 查找编辑树节点。</summary>
    private static PuloniaTask FindTask(PuloniaTask task, string id)
        => FindTaskOrNull(task, id) ?? throw new InvalidOperationException("未找到节点 " + id);

    /// <summary>按节点 ID 查找编辑树节点，未找到时返回 null。</summary>
    private static PuloniaTask? FindTaskOrNull(PuloniaTask task, string id)
        => task.Id == id ? task : task.Children.Select(child => FindTaskOrNull(child, id)).FirstOrDefault(found => found is not null);

    /// <summary>比较有效参数（含字段顺序）。</summary>
    private static void AssertParameters(PuloniaTaskPreparedTask task, JObject parameters)
        => Assert.Equal(parameters.ToString(Newtonsoft.Json.Formatting.None), task.Parameters.ToString(Newtonsoft.Json.Formatting.None));

    /// <summary>比较来源字典。</summary>
    private static void AssertSources(IReadOnlyDictionary<string, string> actual, Dictionary<string, string> expected)
        => Assert.Equal(expected.OrderBy(item => item.Key, StringComparer.Ordinal), actual.OrderBy(item => item.Key, StringComparer.Ordinal));

    /// <summary>删除当前测试创建的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
