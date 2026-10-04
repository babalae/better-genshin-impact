using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>验证新一条龙的快照参数和旧数据转换，不创建窗口或启动游戏。</summary>
public sealed class PuloniaOneDragonImportTests : IDisposable
{
    /// <summary>本测试独占的临时来源目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-import-" + Guid.NewGuid().ToString("N"));

    /// <summary>新增战斗定义必须通过运行构建的完整 Schema 与默认值校验。</summary>
    [Fact]
    public void CombatDefinitions_ValidateAndKeepExplicitConfiguration()
    {
        foreach (var definition in new PuloniaCombatTaskExecutor().Definitions)
        {
            PuloniaTaskValidator.ValidateParameterSchema(definition.ParameterSchema, definition.TaskType);
            PuloniaTaskValidator.ValidateParameters(definition.DefaultParameters, definition.ParameterSchema, definition.TaskType);
        }
        var source = new AutoBossConfig { BossName = "测试首领", TeamName = "指定队伍", RunCount = 3, UseFragileResin = false };
        var parameters = PuloniaCombatTaskSettings.ToParameters(source);
        source.TeamName = "后来修改";
        var restored = PuloniaCombatTaskSettings.FromParameters<AutoBossConfig>(parameters);
        Assert.Equal("指定队伍", restored.TeamName);
        Assert.Equal(3, restored.RunCount);
        Assert.False(restored.UseFragileResin);
    }

    /// <summary>旧一条龙的顺序、重复名称和关闭状态保留，隐式功能设置成为独立预设。</summary>
    [Fact]
    public async Task DragonImport_PreservesOrderAndFreezesHiddenSettings()
    {
        var dragon = new OneDragonFlowConfig { Name = "测试龙", CompletionAction = "关机", NextTaskId = "second" };
        dragon.TaskOrder = ["first", "second", "third"];
        dragon.TaskDefinitions = new() { ["first"] = "领取邮件", ["second"] = "自动首领讨伐", ["third"] = "领取邮件" };
        dragon.TaskEnabledList = new() { ["first"] = true, ["second"] = false, ["third"] = true };
        dragon.AutoBossName = "测试首领"; dragon.AutoBossTeamName = "指定队伍";
        var json = PuloniaTaskJson.Write(dragon);
        var file = await WriteAsync("dragon.json", json);
        var result = await Importer().PrepareAsync([file], new AllConfig());
        var plan = Assert.Single(result.Plans);
        Assert.Equal(new[] { "领取邮件", "自动首领讨伐", "领取邮件" }, plan.RootTask.Children.Select(n => n.Name));
        Assert.False(plan.RootTask.Children[1].IsEnabled);
        Assert.Equal(3, plan.RootTask.Children.Select(n => n.Id).Distinct().Count());
        var bossValues = Assert.Single(result.Presets).Values;
        Assert.Equal("指定队伍", bossValues.Value<string>("team_name"));
        Assert.Equal(JTokenType.Object, bossValues["fight_config"]!.Type);
        Assert.Contains(result.Differences, d => d.Contains("关机"));
        Assert.Contains(result.Differences, d => d.Contains("断点"));
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

    /// <summary>数字枚举在表单确认后仍是 JSON 整数，避免准备阶段拒绝配置。</summary>
    [Fact]
    public void IntegerChoice_RemainsIntegerAfterEditing()
    {
        var field = new PuloniaTaskParameterFieldViewModel("mode", new JObject { ["type"] = "integer", ["enum"] = new JArray(0, 1) }, new JValue(0), false, true)
        { SelectedChoice = "1" };
        Assert.Equal(JTokenType.Integer, field.BuildValue().Type);
        Assert.Equal(1, field.BuildValue().Value<int>());
    }

    /// <summary>可选的空队伍能够保留继承，表单确认只写真正变化的次数。</summary>
    [Fact]
    public void ParameterEditor_KeepsOptionalDefaultsAndWritesOnlyChangedFields()
    {
        var task = new PuloniaTask { Name = "首领", TaskType = "builtin.auto_boss" };
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
            { Name = "参数表单", RootTask = new PuloniaTask { Children = [task] } }, [], new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(new PuloniaCombatTaskExecutor().Definitions);
        document.SelectedNode = Assert.Single(document.RootNode.Children);
        var node = document.SelectedNode!;
        Assert.Equal("", document.GetOneDragonParameters(node).Value<string>("team_name"));
        document.ApplyOneDragonParameters(node, new JObject());
        Assert.Empty(task.Parameters);
        Assert.False(document.IsDirty);
        document.ApplyOneDragonParameters(node, new JObject { ["run_count"] = 3 });
        Assert.Single(task.Parameters);
        Assert.Equal(3, task.Parameters.Value<int>("run_count"));
        Assert.True(document.IsDirty);
    }

    /// <summary>参数版本不兼容的预设不能混入卡片与配置预览。</summary>
    [Fact]
    public void ParameterPreview_RejectsPresetWithDifferentSchemaVersion()
    {
        var preset = new PuloniaTaskPreset { Name = "旧格式", TaskType = "builtin.auto_boss", SchemaVersion = 2,
            Values = new JObject { ["team_name"] = "不兼容队伍" } };
        var task = new PuloniaTask { Name = "首领", TaskType = "builtin.auto_boss", PresetId = preset.Id };
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
            { Name = "预设格式", RootTask = new PuloniaTask { Children = [task] } }, [preset], new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(new PuloniaCombatTaskExecutor().Definitions);
        document.SelectedNode = Assert.Single(document.RootNode.Children);
        Assert.Contains("参数版本", document.EditorMessage);
        Assert.Equal("", document.GetOneDragonParameters(document.SelectedNode!).Value<string>("team_name"));
    }

    /// <summary>使用真实能力定义转换，资源失败路径也经过正式指纹服务。</summary>
    private static PuloniaTaskLegacyImporter Importer() => new(
        [.. new PuloniaBuiltinTaskExecutor().Definitions, .. new PuloniaCombatTaskExecutor().Definitions,
            .. new PuloniaJavaScriptTaskExecutor().Definitions, .. new PuloniaPathingTaskExecutor().Definitions,
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
            && Path.GetFileName(target).StartsWith("bgi-pulonia-import-", StringComparison.Ordinal) && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }
}
