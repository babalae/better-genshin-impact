using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Resin;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ViewModelTests.ResinTests;

/// <summary>宿主草稿、通用字段、可选 DI 与全局配置同步合同；不创建应用或窗口。</summary>
public sealed class ResinHostIntegrationTests
{
    /// <summary>缺失、null、格式错误及有效快照打开不提交，实际编辑只写 resin_usage 并通知宿主。</summary>
    [Theory]
    [InlineData("auto_domain")]
    [InlineData("auto_stygian_onslaught")]
    [InlineData("auto_boss")]
    [InlineData("auto_ley_line_outcrop")]
    public void Dedicated_UsesSnapshotFallbackWithoutWritingOnOpen(string taskId)
    {
        var capability = ResinUsageViewModelSupport.GetTask(taskId)!.Value.Capability;
        foreach (var token in new JToken?[] { null, JValue.CreateNull(), new JValue("格式错误"), new JObject { ["run_count"] = "错误" }, JObject.Parse(SoloTaskJson.Serialize(capability.Defaults)) })
        {
            var original = new JObject();
            if (token is not null) original["resin_usage"] = token.DeepClone();
            var originalCopy = original.DeepClone();
            var vm = CreateDedicated(taskId, original);
            var resin = Entry(vm);
            Assert.Equal(capability.Defaults, resin.Value);
            Assert.Empty(Changes(vm));
            Assert.Contains("resin_usage", vm.EditedParameterNames);
            Assert.False(resin.CanEdit);
            var edited = 0;
            vm.Edited += (_, _) => edited++;
            var next = resin.Value;
            next.Mode = ResinUsageMode.RunCount;
            next.RunCount = 8;
            resin.Reset(next);
            var changes = Changes(vm);
            Assert.Single(changes);
            Assert.Equal(next, SoloTaskJson.Deserialize<ResinUsage>(changes["resin_usage"]!.ToString()));
            Assert.True(edited > 0);
            Assert.True(JToken.DeepEquals(originalCopy, original));
            resin.Reset(capability.Defaults);
            Assert.Empty(Changes(vm));
        }
    }

    /// <summary>ActivatorUtilities 使用标记重载，容器有无弹窗服务都可以创建五个专属 VM。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Factory_ResolvesOptionalDialogsWithoutStartingApp(bool register)
    {
        var registry = new SoloTaskRegistry([typeof(SoloTaskRegistry).Assembly]);
        var collection = new ServiceCollection();
        if (register) collection.AddSingleton<IResinSettingsDialogs>(new DialogStub());
        using var services = collection.BuildServiceProvider();
        foreach (var id in new[] { "auto_domain", "auto_stygian_onslaught", "auto_boss", "auto_ley_line_outcrop", "craft_condensed_resin" })
        {
            var vm = Assert.IsAssignableFrom<SoloTaskSettingsViewModel>(SoloTaskSettingsFactory.Create(registry, id, new(), services));
            if (vm is CraftCondensedResinSettingsViewModel craft)
                Assert.Equal(register, craft.EditResinCommand.CanExecute(null));
            else Assert.Equal(register, Entry(vm).EditCommand.CanExecute(null));
            Assert.Empty(Changes(vm));
        }
    }

    /// <summary>保留旧 -1 快照不产生修改，弹窗取消不写值，确定只更新保留量。</summary>
    [Fact]
    public void Craft_OnlyWritesConfirmedRetentionAndPreservesLegacySnapshot()
    {
        var dialogs = new DialogStub();
        var vm = new CraftCondensedResinSettingsViewModel(new() { ["min_resin_to_keep"] = -1 }, dialogs);
        Assert.Empty(Changes(vm));
        Assert.Equal("按默认数量合成", vm.ResinSummary);
        vm.EditResinCommand.Execute(null);
        Assert.Empty(Changes(vm));
        dialogs.CraftResult = 80;
        var summaryChanged = false;
        vm.PropertyChanged += (_, e) => summaryChanged |= e.PropertyName == nameof(vm.ResinSummary);
        vm.EditResinCommand.Execute(null);
        var changes = Changes(vm);
        Assert.Single(changes);
        Assert.Equal(80, changes.Value<int>("min_resin_to_keep"));
        Assert.Equal("合成后保留 80 原粹树脂", vm.ResinSummary);
        Assert.True(summaryChanged);
    }

    /// <summary>两种通用字段保留原始表示且无修改不覆盖；所有其他形态都与树脂形态互斥。</summary>
    [Theory]
    [InlineData("solo.auto_domain")]
    [InlineData("solo.auto_stygian_onslaught")]
    [InlineData("solo.auto_boss")]
    [InlineData("solo.auto_ley_line_outcrop")]
    public void GenericFields_KeepNestedContractAndOptionalOriginals(string taskType)
    {
        var capability = ResinUsageViewModelSupport.GetTask(taskType)!.Value.Capability;
        var rawSchema = PuloniaSoloTaskSchema.Create(typeof(ResinUsage));
        var dialogs = new DialogStub();
        foreach (var original in new JToken?[] { null, JValue.CreateNull(), new JValue("坏值"), JObject.Parse(SoloTaskJson.Serialize(capability.Defaults)) })
        {
            var schema = PuloniaOneDragonSettingChoices.Decorate("resin_usage", rawSchema, original, taskType);
            Assert.Equal(taskType, schema.Value<string>("x-resin-usage"));
            Assert.Null(rawSchema["x-resin-usage"]);
            Assert.False(schema.Value<bool>("additionalProperties"));
            var field = new PuloniaOneDragonParameterFieldViewModel("resin_usage", schema, original, dialogs: dialogs);
            Assert.True(field.IsResinUsage);
            Assert.False(field.IsAdvancedJson || field.IsObject || field.IsBoolean || field.IsChoice || field.IsCombatStrategy || field.IsText || field.IsNumber);
            Assert.Empty(field.Children);
            Assert.False(field.HasChanges());
            Assert.True(JToken.DeepEquals(original, field.BuildValue()));
            Assert.True(field.Resin!.CanEdit);
            var next = capability.Defaults;
            next.Mode = ResinUsageMode.RunCount;
            next.RunCount = 8;
            field.Resin.Reset(next);
            Assert.True(field.HasChanges());
            Assert.Equal(next, SoloTaskJson.Deserialize<ResinUsage>(Assert.IsType<JObject>(field.BuildValue()).ToString()));
            var scalar = new PuloniaTaskParameterFieldViewModel("resin_usage", schema, original, original is not null, false, dialogs);
            Assert.True(scalar.IsResinUsageEditor);
            Assert.False(scalar.IsJsonEditor || scalar.IsTextEditor || scalar.IsChoiceEditor || scalar.IsBooleanEditor || scalar.IsCombatStrategyEditor);
            Assert.False(scalar.ShouldPersist(scalar.BuildValue()));
            scalar.Resin!.Reset(next);
            Assert.True(scalar.ShouldPersist(scalar.BuildValue()));
            Assert.Equal(next, SoloTaskJson.Deserialize<ResinUsage>(scalar.BuildValue().ToString()));
        }
    }

    /// <summary>通用工厂能把弹窗服务转发给 resin_usage 卡片。</summary>
    [Fact]
    public void GenericFactory_ForwardsDialogs()
    {
        var properties = new JObject
        {
            ["resin_usage"] = PuloniaSoloTaskSchema.Create(typeof(ResinUsage))
        };
        var definition = new PuloniaTaskDefinition
        {
            TaskType = "solo.auto_domain", DisplayName = "秘境", ParameterSchema = new() { ["properties"] = properties }
        };
        var fields = PuloniaTaskSettingsEditorViewModel.CreateFields(definition, new(), new DialogStub());
        Assert.True(fields.Single(field => field.Name == "resin_usage").Resin!.CanEdit);
    }

    /// <summary>入口与配置只做整体替换，两个入口相互刷新，一次变化只产生一次配置通知，不会递归。</summary>
    [Fact]
    public void ConfigBinding_SynchronizesTwoEntriesWithoutLoopOrSharedMutation()
    {
        var config = new AutoDomainConfig();
        ResinUsageEntryViewModel Bind() => ResinUsageConfigBinding.Create("自动秘境", ResinCapabilities.Domain, config,
            config.EnsureResinUsage, value => config.ResinUsage = value, null);
        var first = Bind();
        var second = Bind();
        var initial = config.ResinUsage!;
        var notifications = 0;
        config.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(config.ResinUsage)) notifications++; };
        var next = first.Value;
        next.Mode = ResinUsageMode.RunCount;
        next.RunCount = 8;
        first.Reset(next);
        Assert.Equal(1, notifications);
        Assert.NotSame(initial, config.ResinUsage);
        Assert.Equal(ResinUsageMode.Exhaust, initial.Mode);
        Assert.Equal(first.Value, second.Value);
        var external = next.Clone();
        external.RunCount = 9;
        config.ResinUsage = external;
        Assert.Equal(2, notifications);
        Assert.Equal(external, first.Value);
        Assert.Equal(external, second.Value);
        external.Allowed.Original = false;
        Assert.True(first.Value.Allowed.Original);
        Assert.True(second.Value.Allowed.Original);
        first.Reset(first.Value);
        Assert.Equal(2, notifications);
    }

    /// <summary>使用保留的原构造，覆盖 Activator.CreateInstance 所依赖的签名。</summary>
    private static SoloTaskSettingsViewModel CreateDedicated(string id, JObject original) => id switch
    {
        "auto_domain" => new AutoDomainSettingsViewModel(original),
        "auto_stygian_onslaught" => new AutoStygianOnslaughtSettingsViewModel(original),
        "auto_boss" => new AutoBossSettingsViewModel(original, null),
        _ => new AutoLeyLineOutcropSettingsViewModel(original)
    };

    /// <summary>取得各专属 VM 的同一入口合同。</summary>
    private static ResinUsageEntryViewModel Entry(SoloTaskSettingsViewModel settings) => settings switch
    {
        AutoDomainSettingsViewModel domain => domain.Resin,
        AutoStygianOnslaughtSettingsViewModel stygian => stygian.Resin,
        AutoBossSettingsViewModel boss => boss.Resin,
        AutoLeyLineOutcropSettingsViewModel leyLine => leyLine.Resin,
        _ => throw new ArgumentOutOfRangeException(nameof(settings))
    };

    /// <summary>收集页面相对快照的变化。</summary>
    private static JObject Changes(SoloTaskSettingsViewModel settings)
    {
        var changes = new JObject();
        settings.CollectChanges(changes);
        return changes;
    }

    /// <summary>不创建真实窗口的宿主弹窗替身。</summary>
    private sealed class DialogStub : IResinSettingsDialogs
    {
        /// <summary>合成弹窗的预设返回值。</summary>
        public int? CraftResult { get; set; }
        /// <inheritdoc />
        public ResinUsage? EditResinUsage(string taskName, ResinUsageCapability capability, ResinUsage current) => null;
        /// <inheritdoc />
        public int? EditCraftResin(int minResinToKeep) => CraftResult;
    }
}
