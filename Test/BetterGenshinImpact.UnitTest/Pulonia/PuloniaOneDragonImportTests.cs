using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>验证新一条龙的快照参数和旧数据转换，不创建窗口或启动游戏。</summary>
public sealed class PuloniaOneDragonImportTests : IDisposable
{
    /// <summary>本测试独占的临时来源目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 旧一条龙的顺序、重复名称和关闭状态保留；一条龙自有字段按新参数名写入独立任务节点。
    /// 战斗类任务的参数基类依赖运行中的应用，预设冻结只能在应用内验证。
    /// </summary>
    [Theory]
    [InlineData(40, 40)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    public async Task DragonImport_PreservesOrderAndWritesSoloParameters(int retainedResin, int expectedRetainedResin)
    {
        var dragon = new OneDragonFlowConfig { Name = "测试龙", CompletionAction = "关机", NextTaskId = "second" };
        dragon.TaskOrder = ["first", "second", "third", "fourth", "fifth"];
        dragon.TaskDefinitions = new()
        {
            ["first"] = "领取邮件", ["second"] = "合成树脂", ["third"] = "领取邮件",
            ["fourth"] = "领取每日奖励", ["fifth"] = "领取尘歌壶奖励"
        };
        dragon.TaskEnabledList = new() { ["first"] = true, ["second"] = false, ["third"] = true, ["fourth"] = true, ["fifth"] = true };
        dragon.CraftingBenchCountry = "稻妻"; dragon.MinResinToKeep = retainedResin;
        dragon.AdventurersGuildCountry = "璃月"; dragon.DailyRewardPartyName = "好感队";
        dragon.SereniteaPotTpType = "尘歌壶道具"; dragon.SecretTreasureObjects = ["每天重复", "精锻用魔矿"];
        var json = PuloniaTaskJson.Write(dragon);
        var file = await WriteAsync("dragon.json", json);
        var result = await Importer().PrepareAsync([file], new AllConfig());
        var plan = Assert.Single(result.Plans);
        var nodes = plan.RootTask.Children;
        Assert.Equal(new[] { "领取邮件", "合成树脂", "领取邮件", "领取每日奖励", "领取尘歌壶奖励" }, nodes.Select(n => n.Name));
        Assert.Equal(new[]
        {
            PuloniaSoloTaskExecutor.ToTaskType(ClaimMailRewardsTask.TaskId), PuloniaSoloTaskExecutor.ToTaskType(GoToCraftingBenchTask.TaskId),
            PuloniaSoloTaskExecutor.ToTaskType(ClaimMailRewardsTask.TaskId), PuloniaSoloTaskExecutor.ToTaskType(ClaimDailyRewardsTask.TaskId),
            PuloniaSoloTaskExecutor.ToTaskType(GoToSereniteaPotTask.TaskId)
        }, nodes.Select(n => n.TaskType));
        Assert.False(nodes[1].IsEnabled);
        Assert.Equal(5, nodes.Select(n => n.Id).Distinct().Count());
        Assert.Empty(nodes[0].Parameters);
        Assert.Equal("稻妻", nodes[1].Parameters.Value<string>("country"));
        Assert.Equal(expectedRetainedResin, nodes[1].Parameters.Value<int>("min_resin_to_keep"));
        Assert.Equal("璃月", nodes[3].Parameters.Value<string>("country"));
        Assert.Equal("好感队", nodes[3].Parameters.Value<string>("party_name"));
        Assert.Equal("尘歌壶道具", nodes[4].Parameters.Value<string>("teleport_type"));
        Assert.Equal(new[] { "每天重复", "精锻用魔矿" }, nodes[4].Parameters["secret_treasure_objects"]!.Values<string>());
        // 一条龙自有字段直接写入节点任务参数
        Assert.Contains(result.Differences, d => d.Contains("关机"));
        Assert.Contains(result.Differences, d => d.Contains("断点"));
        Assert.Contains(result.Differences, d => d.Contains("保留原粹树脂数"));
        Assert.Equal(json, await File.ReadAllTextAsync(file));
    }

    /// <summary>配置组的周期不能误当成主动触发，重复次数与 JS 布尔设置保持原始类型。</summary>
    [Fact]
    public async Task GroupImport_ReportsScheduleAndKeepsTypedSettings()
    {
        var json = new JObject
        {
            ["name"] = "旧配置组", ["projects"] = new JArray(
                new JObject { ["index"] = 2, ["name"] = "缺失脚本", ["folderName"] = Guid.NewGuid().ToString("N"), ["type"] = "Javascript", ["status"] = "Enabled", ["schedule"] = "Daily", ["runNum"] = 2,
                    ["jsScriptSettingsObject"] = new JObject { ["flag"] = false, ["items"] = new JArray("甲", "乙") } },
                new JObject { ["index"] = 1, ["name"] = "echo import", ["type"] = "Shell", ["status"] = "Disabled", ["runNum"] = 1 })
        }.ToString();
        var result = await Importer().PrepareAsync([await WriteAsync("group.json", json)], new AllConfig());
        var plan = Assert.Single(result.Plans);
        Assert.Equal("shell", plan.RootTask.Children[0].TaskType);
        Assert.False(plan.RootTask.Children[0].IsEnabled);
        var repeat = plan.RootTask.Children[1];
        Assert.Equal(2, repeat.RepeatCount);
        var node = Assert.Single(repeat.Children);
        Assert.False(node.IsEnabled);
        Assert.Equal(JTokenType.Boolean, node.Parameters["settings"]!["flag"]!.Type);
        Assert.Equal(JTokenType.Array, node.Parameters["settings"]!["items"]!.Type);
        Assert.Empty(plan.Triggers);
        Assert.Contains(result.Differences, d => d.Contains("Daily"));
    }

    /// <summary>d-v3 未注册反射方法保留原始参数并禁用，不变成任意反射执行入口。</summary>
    [Fact]
    public async Task GearImport_PreservesUnsupportedReflectionAsDisabledData()
    {
        var source = new JObject { ["name"] = "d-v3 计划", ["root_task"] = new JObject
        {
            ["name"] = "根", ["task_type"] = "group", ["is_enabled"] = true, ["children"] = new JArray(new JObject
            {
                ["name"] = "旧反射", ["task_type"] = "csharpreflection", ["parameters"] = "{\"MethodPath\":\"Legacy.Run\",\"Args\":[false,0]}"
            })
        } };
        var result = await Importer().PrepareAsync([await WriteAsync("gear.json", source.ToString())], new AllConfig());
        var node = Assert.Single(Assert.Single(result.Plans).RootTask.Children);
        Assert.False(node.IsEnabled);
        Assert.Equal("legacy.unsupported", node.TaskType);
        Assert.Equal(source["root_task"]!["children"]![0]!.ToString(), node.Parameters["legacy_data"]!.ToString());
        Assert.Contains(result.Differences, d => d.Contains("反射"));
    }

    /// <summary>旧配置组设置导入为分组共享设置，只保留与默认值不同的项，运行准备可以直接使用。</summary>
    [Fact]
    public async Task GroupImport_ConvertsGroupConfigToSharedSettings()
    {
        var json = new JObject
        {
            ["name"] = "带设置的配置组",
            ["config"] = new JObject { ["pathingConfig"] = new JObject { ["partyName"] = "采集队", ["autoPickEnabled"] = false } },
            ["projects"] = new JArray(new JObject { ["index"] = 1, ["name"] = "echo import", ["type"] = "Shell", ["status"] = "Enabled", ["runNum"] = 1 })
        }.ToString();
        var result = await Importer().PrepareAsync([await WriteAsync("group-settings.json", json)], new AllConfig());
        var plan = Assert.Single(result.Plans);
        Assert.Equal("采集队", plan.RootTask.Settings.Value<string>("party.party_name"));
        Assert.False(plan.RootTask.Settings.Value<bool>("pickup.auto_pick"));
        Assert.Equal(2, plan.RootTask.Settings.Count);
        PuloniaTaskValidator.ValidatePlan(plan);
    }

    /// <summary>
    /// 使用资源类执行器的真实能力定义与测试用独立任务定义转换，资源失败路径也经过正式指纹服务。
    /// 独立任务没有外部资源，导入时不读取它们的定义。
    /// </summary>
    private static PuloniaTaskLegacyImporter Importer() => new(
        [.. PuloniaSoloTestDefinitions.OneDragonCandidates(),
            .. new PuloniaJavaScriptTaskExecutor().Definitions, .. new PuloniaPathingTaskExecutor(new AllConfig()).Definitions,
            .. new PuloniaKeyMouseTaskExecutor().Definitions, .. new PuloniaShellTaskExecutor().Definitions], new PuloniaTaskResourceCatalog());

    /// <summary>只写本测试的独占源文件。</summary>
    private async Task<string> WriteAsync(string name, string json)
    {
        Directory.CreateDirectory(_directory); var file = Path.Combine(_directory, name);
        await File.WriteAllTextAsync(file, json); return file;
    }

    /// <summary>验证临时目录的绝对位置后清理测试资源。</summary>
    public void Dispose()
    {
        var target = Path.GetFullPath(_directory);
        if (Path.GetDirectoryName(target) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
            && Path.GetFileName(target).StartsWith("bgi-", StringComparison.Ordinal) && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }
}
