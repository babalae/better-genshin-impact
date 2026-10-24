using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 固定运行准备的有效参数层级、顺序与来源，并核对任务计划界面预览与运行准备得到相同结果。
/// </summary>
public sealed class PuloniaParameterResolutionTests : IDisposable
{
    /// <summary>测试使用的纯数据能力类型，不需要资源文件或执行器。</summary>
    private const string EchoType = "sample.echo";

    /// <summary>资源专用能力和资源专用覆盖使用的资源标识。</summary>
    private const string ResourceKey = "res-a";

    /// <summary>入口计划中第一个叶子的运行地址。</summary>
    private const string LeafAAddress = "pln-main/tsk-root/tsk-outer/tsk-inner/tsk-leaf-a";

    /// <summary>入口计划中资源专用叶子的运行地址。</summary>
    private const string LeafResAddress = "pln-main/tsk-root/tsk-outer/tsk-inner/tsk-leaf-res";

    /// <summary>被引用计划中叶子的运行地址。</summary>
    private const string ChildLeafAddress = "pln-main/tsk-root/tsk-ref/@pln-child/tsk-child-root/tsk-child-leaf";

    /// <summary>当前测试独占的存储目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 能力默认值 → 预设（账号替换）→ 祖先分组（通用先于资源专用）→ 节点 → 引用公共参数 → 调用覆盖，
    /// 每个字段的值与来源都按固定顺序得到；引用边界不继承外部分组参数，禁用节点只保留自身参数。
    /// </summary>
    [Fact]
    public async Task Builder_AppliesEveryLayerInFixedOrder()
    {
        using var store = await CreateStoreAsync();
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(CreateMainPlan(), new PuloniaTaskBuildOptions
        {
            Definitions = CreateDefinitions(),
            AccountId = "acc-main",
            ParameterOverrides = new Dictionary<string, JObject>
            {
                [LeafAAddress] = new() { ["h"] = "call" },
                [ChildLeafAddress] = new() { ["g"] = "call-child" }
            }
        });

        // 1. 通用能力叶子：自身预设、三层分组通用覆盖、节点参数和调用覆盖
        AssertPrepared(Find(snapshot.RootTask, LeafAAddress),
            new JObject
            {
                ["a"] = "default", ["b"] = "preset-node", ["c"] = "root", ["d"] = "outer",
                ["e"] = "inner", ["f"] = "default", ["g"] = "node", ["h"] = "call"
            },
            new Dictionary<string, string>
            {
                ["a"] = "default/" + EchoType, ["b"] = "preset/pre-node", ["c"] = "pln-main/tsk-root",
                ["d"] = "pln-main/tsk-root/tsk-outer", ["e"] = "pln-main/tsk-root/tsk-outer/tsk-inner",
                ["f"] = "default/" + EchoType, ["g"] = LeafAAddress, ["h"] = "call/" + LeafAAddress
            });

        // 2. 资源专用叶子：账号替换预设，同组资源专用覆盖晚于通用覆盖生效
        AssertPrepared(Find(snapshot.RootTask, LeafResAddress),
            new JObject
            {
                ["a"] = "res-default", ["b"] = "preset-account", ["c"] = "root", ["d"] = "outer-res",
                ["e"] = "inner", ["f"] = "res-default", ["g"] = "res-default", ["h"] = "node"
            },
            new Dictionary<string, string>
            {
                ["a"] = "default/" + EchoType, ["b"] = "preset/pre-account", ["c"] = "pln-main/tsk-root",
                ["d"] = "pln-main/tsk-root/tsk-outer", ["e"] = "pln-main/tsk-root/tsk-outer/tsk-inner",
                ["f"] = "default/" + EchoType, ["g"] = "default/" + EchoType, ["h"] = LeafResAddress
            });

        // 3. 引用计划叶子：外部分组参数不跨入，引用节点公共参数晚于节点参数，调用覆盖最后生效
        AssertPrepared(Find(snapshot.RootTask, ChildLeafAddress),
            new JObject
            {
                ["a"] = "default", ["b"] = "default", ["c"] = "default", ["d"] = "default",
                ["e"] = "child-root", ["f"] = "ref-public", ["g"] = "call-child", ["h"] = "default"
            },
            new Dictionary<string, string>
            {
                ["a"] = "default/" + EchoType, ["b"] = "default/" + EchoType, ["c"] = "default/" + EchoType,
                ["d"] = "default/" + EchoType, ["e"] = "pln-child/tsk-child-root",
                ["f"] = "pln-main/tsk-root/tsk-ref/public", ["g"] = "call/" + ChildLeafAddress,
                ["h"] = "default/" + EchoType
            });

        // 4. 禁用节点不解析继承层，只保留自身参数且没有来源
        AssertPrepared(Find(snapshot.RootTask, "pln-main/tsk-root/tsk-off"),
            new JObject { ["a"] = "off" }, new Dictionary<string, string>());
        Assert.Equal(new[] { "pre-node", "pre-account" }, snapshot.PresetJsonById.Keys);
    }

    /// <summary>
    /// 运行准备遇到预设缺失、预设不兼容、引用覆盖未声明的公共参数或调用覆盖未消费时明确报错。
    /// </summary>
    [Fact]
    public async Task Builder_RejectsMissingOrIncompatiblePresetsAndInvalidOverrides()
    {
        using var store = await CreateStoreAsync();
        var builder = new PuloniaTaskBuilder(store);

        var missing = CreateMainPlan();
        FindTask(missing.RootTask, "tsk-leaf-a").PresetId = "pre-missing";
        var missingError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => builder.BuildAsync(missing, new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() }));
        Assert.Equal(LeafAAddress + "：预设 pre-missing 不存在。", missingError.Message);

        var incompatible = CreateMainPlan();
        FindTask(incompatible.RootTask, "tsk-leaf-a").PresetId = "pre-other";
        var incompatibleError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => builder.BuildAsync(incompatible, new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() }));
        Assert.Equal(LeafAAddress + "：预设的类型、资源或参数版本不匹配。", incompatibleError.Message);

        var undeclared = CreateMainPlan();
        FindTask(undeclared.RootTask, "tsk-ref").ParameterOverrides[0].Values["a"] = "not-public";
        var undeclaredError = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => builder.BuildAsync(undeclared, new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() }));
        Assert.Equal(ChildLeafAddress + "：计划引用试图覆盖未声明的公共参数。", undeclaredError.Message);

        var unusedCall = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => builder.BuildAsync(CreateMainPlan(), new PuloniaTaskBuildOptions
            {
                Definitions = CreateDefinitions(),
                ParameterOverrides = new Dictionary<string, JObject> { ["pln-main/tsk-root/tsk-off"] = new() { ["a"] = 1 } }
            }));
        Assert.Equal("pln-main/tsk-root/tsk-off：调用覆盖未指向启用的具体任务。", unusedCall.Message);
    }

    /// <summary>
    /// 未选择账号、没有调用覆盖时，界面预览与运行准备对同一节点得到完全相同的有效参数。
    /// </summary>
    [Fact]
    public async Task Preview_MatchesBuilderWithoutAccountAndCallOverrides()
    {
        using var store = await CreateStoreAsync();
        var plan = CreateMainPlan();
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan,
            new PuloniaTaskBuildOptions { Definitions = CreateDefinitions() });
        var document = new PuloniaTaskPlanDocumentViewModel(plan, CreatePresets(), new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(CreateDefinitions());

        foreach (var (taskId, address) in new[] { ("tsk-leaf-a", LeafAAddress), ("tsk-leaf-res", LeafResAddress) })
        {
            var node = document.EnumerateNodes().Single(item => item.Id == taskId);
            var preview = document.GetOneDragonParameters(node);
            Assert.True(JToken.DeepEquals(Find(snapshot.RootTask, address).Parameters, preview),
                $"{taskId} 预览：{preview}");
        }
    }

    /// <summary>
    /// 预设缺失或不兼容时界面预览不抛异常，继续展示其余层级，并在清单摘要中给出提示。
    /// </summary>
    [Fact]
    public void Preview_ReportsMissingOrIncompatiblePresetWithoutThrowing()
    {
        var plan = CreateMainPlan();
        FindTask(plan.RootTask, "tsk-leaf-a").PresetId = "pre-missing";
        var document = new PuloniaTaskPlanDocumentViewModel(plan, CreatePresets(), new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(CreateDefinitions());
        var node = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-a");

        var preview = document.GetOneDragonParameters(node);

        Assert.Equal("default", preview.Value<string>("b"));
        Assert.Equal("inner", preview.Value<string>("e"));
        Assert.Contains("⚠ 预设缺失", document.GetOneDragonConfigurationSummary(node));

        // 预设类型不兼容时同样跳过预设层并提示，判定规则与运行准备一致。
        node.Model.PresetId = "pre-other";
        Assert.Equal("default", document.GetOneDragonParameters(node).Value<string>("b"));
        Assert.Contains("⚠ 预设不兼容", document.GetOneDragonConfigurationSummary(node));
    }

    /// <summary>
    /// 编辑界面“有效参数与来源”沿用原展示文本：能力默认值、预设名、分组名和节点名。
    /// </summary>
    [Fact]
    public void Preview_DescribesSourcesWithDisplayNames()
    {
        var document = new PuloniaTaskPlanDocumentViewModel(CreateMainPlan(), CreatePresets(), new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(CreateDefinitions());
        var node = document.EnumerateNodes().Single(item => item.Id == "tsk-leaf-res");

        // 有效参数来源改由高级设置窗口按需读取，切换选中节点不再刷新文档上的预览集合
        var sources = document.GetEffectiveParameterSources(node).ToDictionary(item => item.Name, item => item.Source);

        Assert.Equal("能力默认值", sources["a"]);
        Assert.Equal("预设“节点预设”", sources["b"]);
        Assert.Equal("分组“根分组”", sources["c"]);
        Assert.Equal("分组“外层”", sources["d"]);
        Assert.Equal("分组“内层”", sources["e"]);
        Assert.Equal("节点“资源叶子”", sources["h"]);
    }

    /// <summary>
    /// 解析器直接给出预设判定：缺失或不兼容时跳过预设层；引用公共参数项含未声明字段时整项不生效并标记。
    /// </summary>
    [Fact]
    public void Resolver_ReportsPresetStatusAndUndeclaredPublicOverride()
    {
        var definition = CreateDefinitions()[0];
        var task = new PuloniaTask { TaskType = EchoType, PresetId = "pre-x", Parameters = new JObject { ["a"] = "node" } };

        var missing = PuloniaParameterResolver.Resolve(task, definition, "pre-x", null, []);
        Assert.Equal(PuloniaPresetStatus.Missing, missing.PresetStatus);
        Assert.Equal("default", missing.Parameters.Value<string>("b"));

        var other = CreatePresets()[2];
        var incompatible = PuloniaParameterResolver.Resolve(task, definition, other.Id, other, []);
        Assert.Equal(PuloniaPresetStatus.Incompatible, incompatible.PresetStatus);
        Assert.Same(other, incompatible.Preset);
        Assert.Equal("default", incompatible.Parameters.Value<string>("b"));

        var reference = PuloniaParameterResolver.Resolve(task, definition, null, null, [],
        [
            Override(new JObject { ["a"] = "not-public" }), Override(new JObject { ["f"] = "public" }),
            Override(new JObject { ["g"] = "resource-only" }, ResourceKey)
        ]);
        Assert.Equal(PuloniaPresetStatus.None, reference.PresetStatus);
        Assert.True(reference.HasUndeclaredPublicOverride);
        Assert.Equal("node", reference.Parameters.Value<string>("a"));
        Assert.Equal("public", reference.Parameters.Value<string>("f"));
        Assert.Equal(PuloniaParameterLayer.ReferencePublic, reference.Sources["f"].Layer);
        // 资源专用项与节点资源不匹配，不参与解析。
        Assert.Equal("default", reference.Parameters.Value<string>("g"));
    }

    /// <summary>
    /// 账号替换只在所选账号于计划中启用、且为该节点配置了替换时生效；不传账号即跳过该层。
    /// </summary>
    [Fact]
    public void Resolver_AppliesAccountPresetReplacementOnlyForEnabledBinding()
    {
        var plan = CreateMainPlan();
        var leaf = FindTask(plan.RootTask, "tsk-leaf-res");

        Assert.Equal("pre-account", PuloniaParameterResolver.GetEffectivePresetId(leaf, plan.Accounts, "acc-main"));
        Assert.Equal("pre-node", PuloniaParameterResolver.GetEffectivePresetId(leaf, plan.Accounts, null));
        Assert.Equal("pre-node", PuloniaParameterResolver.GetEffectivePresetId(leaf, plan.Accounts, "acc-other"));
        plan.Accounts[0].Enabled = false;
        Assert.Equal("pre-node", PuloniaParameterResolver.GetEffectivePresetId(leaf, plan.Accounts, "acc-main"));
    }

    /// <summary>
    /// 建立保存了预设和被引用计划的独立存储。
    /// </summary>
    private async Task<PuloniaTaskStore> CreateStoreAsync()
    {
        var store = new PuloniaTaskStore(_directory);
        foreach (var preset in CreatePresets())
            await store.SavePresetAsync(preset);
        await store.SavePlanAsync(CreateChildPlan());
        return store;
    }

    /// <summary>
    /// 通用能力与资源专用能力；两者默认值不同，便于区分实际选用的说明。
    /// </summary>
    private static List<PuloniaTaskDefinition> CreateDefinitions() =>
    [
        new()
        {
            TaskType = EchoType,
            DefaultParameters = Defaults("default"),
            PublicParameters = ["f", "g"]
        },
        new()
        {
            TaskType = EchoType,
            ResourceId = ResourceKey,
            DefaultParameters = Defaults("res-default"),
            PublicParameters = ["f", "g"]
        }
    ];

    /// <summary>
    /// 节点自选预设、账号替换预设和类型不兼容的预设。
    /// </summary>
    private static List<PuloniaTaskPreset> CreatePresets() =>
    [
        new() { Id = "pre-node", Name = "节点预设", TaskType = EchoType, Values = new JObject { ["b"] = "preset-node" } },
        new()
        {
            Id = "pre-account", Name = "账号预设", TaskType = EchoType, ResourceId = ResourceKey,
            Values = new JObject { ["b"] = "preset-account", ["c"] = "preset-account" }
        },
        new() { Id = "pre-other", Name = "其他类型预设", TaskType = "sample.other", Values = new JObject { ["b"] = "other" } }
    ];

    /// <summary>
    /// 入口计划：三层分组覆盖、两个叶子、一个计划引用和一个禁用叶子，并为账号替换资源叶子的预设。
    /// </summary>
    private static PuloniaTaskPlan CreateMainPlan()
    {
        var plan = new PuloniaTaskPlan { Id = "pln-main", Name = "参数层级" };
        plan.RootTask.Id = "tsk-root";
        plan.RootTask.ParameterOverrides.Add(Override(new JObject { ["c"] = "root", ["d"] = "root" }));
        var inner = new PuloniaTask
        {
            Id = "tsk-inner", Name = "内层", ParameterOverrides = [Override(new JObject { ["e"] = "inner" })],
            Children =
            [
                new PuloniaTask
                {
                    Id = "tsk-leaf-a", Name = "通用叶子", TaskType = EchoType, PresetId = "pre-node",
                    Parameters = new JObject { ["g"] = "node" }
                },
                new PuloniaTask
                {
                    Id = "tsk-leaf-res", Name = "资源叶子", TaskType = EchoType, Path = ResourceKey, PresetId = "pre-node",
                    Parameters = new JObject { ["h"] = "node" }
                }
            ]
        };
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-outer", Name = "外层",
            // 资源专用覆盖写在前面，仍应晚于同组通用覆盖生效。
            ParameterOverrides =
            [
                Override(new JObject { ["d"] = "outer-res" }, ResourceKey),
                Override(new JObject { ["d"] = "outer", ["e"] = "outer" })
            ],
            Children = [inner]
        });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-ref", Name = "引用", Source = new PuloniaTaskSource { Kind = "plan", PlanId = "pln-child" },
            ParameterOverrides = [Override(new JObject { ["f"] = "ref-public" })]
        });
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-off", Name = "禁用叶子", TaskType = EchoType, IsEnabled = false,
            Parameters = new JObject { ["a"] = "off" }
        });
        plan.Accounts.Add(new PuloniaTaskAccountBinding
        {
            AccountId = "acc-main",
            PresetSelections = new Dictionary<string, string> { ["tsk-leaf-res"] = "pre-account" }
        });
        return plan;
    }

    /// <summary>
    /// 被引用计划：根分组提供通用覆盖，叶子自带参数。
    /// </summary>
    private static PuloniaTaskPlan CreateChildPlan()
    {
        var plan = new PuloniaTaskPlan { Id = "pln-child", Name = "被引用" };
        plan.RootTask.Id = "tsk-child-root";
        plan.RootTask.ParameterOverrides.Add(Override(new JObject { ["e"] = "child-root" }));
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Id = "tsk-child-leaf", Name = "引用叶子", TaskType = EchoType, Parameters = new JObject { ["g"] = "child-node" }
        });
        return plan;
    }

    /// <summary>a—h 八个字段使用同一默认值。</summary>
    private static JObject Defaults(string value)
        => new(new[] { "a", "b", "c", "d", "e", "f", "g", "h" }.Select(name => new JProperty(name, value)));

    /// <summary>建立测试类型的分组公共参数。</summary>
    private static PuloniaTaskParameterOverride Override(JObject values, string? resourceId = null)
        => new() { TaskType = EchoType, ResourceId = resourceId, Values = values };

    /// <summary>按运行地址查找展开节点。</summary>
    private static PuloniaTaskPreparedTask Find(PuloniaTaskPreparedTask task, string address)
        => task.TaskAddress == address
            ? task
            : task.Children.Select(child => TryFind(child, address)).FirstOrDefault(found => found is not null)
              ?? throw new InvalidOperationException("未找到节点 " + address);

    /// <summary>按运行地址查找展开节点，未找到时返回 null。</summary>
    private static PuloniaTaskPreparedTask? TryFind(PuloniaTaskPreparedTask task, string address)
        => task.TaskAddress == address
            ? task
            : task.Children.Select(child => TryFind(child, address)).FirstOrDefault(found => found is not null);

    /// <summary>按节点 ID 查找编辑树节点。</summary>
    private static PuloniaTask FindTask(PuloniaTask task, string id)
        => task.Id == id ? task : task.Children.Select(child => FindTaskOrNull(child, id)).First(found => found is not null)!;

    /// <summary>按节点 ID 查找编辑树节点，未找到时返回 null。</summary>
    private static PuloniaTask? FindTaskOrNull(PuloniaTask task, string id)
        => task.Id == id ? task : task.Children.Select(child => FindTaskOrNull(child, id)).FirstOrDefault(found => found is not null);

    /// <summary>逐字段比较有效参数（含字段顺序）和来源。</summary>
    private static void AssertPrepared(PuloniaTaskPreparedTask task, JObject parameters, Dictionary<string, string> sources)
    {
        Assert.Equal(parameters.ToString(Newtonsoft.Json.Formatting.None), task.Parameters.ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal(sources.OrderBy(item => item.Key, StringComparer.Ordinal),
            task.ParameterSources.OrderBy(item => item.Key, StringComparer.Ordinal));
    }

    /// <summary>删除当前测试创建的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
