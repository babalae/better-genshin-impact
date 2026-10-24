using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 两个页面共用编辑会话后的回归：触发配置直接编辑会话文档、运行账号常驻提示、高级设置按需计算。
/// 不启动应用、游戏或 WPF 窗口。
/// </summary>
public sealed class PuloniaPlanEditingSessionTests : IDisposable
{
    /// <summary>
    /// 测试独占目录，避免接触程序真实计划。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 两个页面共享同一会话文档；一条龙触发配置不复制页面，保存经会话写入同一文档，释放后不再响应文档变化。
    /// </summary>
    [Fact]
    public async Task TriggerSettings_EditOneDragonDocumentThroughSharedSession()
    {
        using var store = new PuloniaTaskStore(_directory);
        await store.SavePlanAsync(CreateOneDragonPlan("一条龙"));
        // 已有普通计划时加载不会补建待自动保存的默认计划。
        await store.SavePlanAsync(new PuloniaTaskPlan { Name = "普通计划" });
        await using var service = CreateService(store);
        var session = CreateSession(store, service);
        var planPage = new PuloniaTaskPlanViewModel(session, new PuloniaTaskHistoryViewModel(service, store));
        var dragonPage = new PuloniaOneDragonViewModel(session, service, new PuloniaTaskResourceCatalog(), store, EmptyRegistry());
        await planPage.InitializeCommand.ExecuteAsync(null);
        var document = Assert.Single(dragonPage.Configurations);
        Assert.Same(document, Assert.Single(session.OneDragonDocuments));

        var triggers = new PuloniaPlanTriggersViewModel(session, null) { Document = document };
        triggers.AddHotkeyTriggerCommand.Execute(null);
        var editor = triggers.TriggerEditor!;
        // 新建快捷键与按钮“新增快捷键触发”一致：键鼠监听 F8；一条龙按配置绑定的账号运行，不提供账号下拉框。
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor, editor.HotkeyType);
        Assert.Equal("F8", editor.Hotkey.ToString());
        Assert.False(editor.CanSelectAccount);
        Assert.Equal("运行账号", editor.AccountSelectionTitle);

        await triggers.SaveTriggerCommand.ExecuteAsync(null);

        var saved = Assert.Single((await store.ListPlansAsync()).Single(plan => plan.Purpose == PuloniaTaskPlanPurpose.OneDragon).Triggers);
        Assert.Equal(PuloniaTaskTriggerKind.Hotkey, saved.Kind);
        Assert.False(saved.Enabled);
        Assert.Same(document.Plan.Triggers[0], document.Plan.Triggers.Single());
        Assert.Single(triggers.Triggers);
        Assert.NotEqual("", triggers.Message);

        // 释放后不再跟随文档刷新，避免关闭的窗口残留订阅。
        triggers.Dispose();
        document.ApplyMutation(() => document.Plan.Triggers.Clear(), document.SelectedNode);
        Assert.Single(triggers.Triggers);
    }

    /// <summary>
    /// 绑定非默认账号时始终提示运行账号，与“显示账号切换步骤”无关；默认账号不提示。
    /// </summary>
    [Fact]
    public async Task RunAccount_IsShownWheneverConfigurationSwitchesAccount()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var session = CreateSession(store, service);
        var page = new PuloniaOneDragonViewModel(session, service, new PuloniaTaskResourceCatalog(), store, EmptyRegistry());
        var bound = CreateDocument(CreateOneDragonPlan("绑定账号"));
        bound.Plan.Accounts = [new PuloniaTaskAccountBinding { AccountId = "acc_0123456789abcdef" }];
        var standard = CreateDocument(CreateOneDragonPlan("默认账号"));
        session.OneDragonDocuments.Add(bound);
        session.OneDragonDocuments.Add(standard);

        page.SelectedConfiguration = bound;
        Assert.False(page.ShowAccountPreparation);
        Assert.True(page.HasRunAccount);
        Assert.Equal("运行账号：账号资料已删除，请重新选择", page.RunAccountText);
        page.GenshinAccounts.Add(new PuloniaGenshinAccountProfile { Id = "acc_0123456789abcdef", Name = "小号" });
        Assert.Equal("运行账号：小号", page.RunAccountText);

        page.SelectedConfiguration = standard;
        Assert.False(page.HasRunAccount);
    }

    /// <summary>
    /// 高级设置只在打开的窗口中计算：切换选中节点不影响它，应用参数经文档提交并可撤销，撤销后窗口按模型重读。
    /// </summary>
    [Fact]
    public void AdvancedSettings_AreComputedForTheOpenedNodeOnly()
    {
        var plan = CreateOneDragonPlan("高级设置");
        var document = CreateDocument(plan);
        var node = document.RootNode.Children[0];
        using var advanced = new PuloniaTaskNodeAdvancedSettingsViewModel(document, node, dialogs: null);
        Assert.Equal("{}", advanced.ParameterJson);
        Assert.Equal("不使用共享预设", advanced.PresetOptions[0].DisplayName);

        document.SelectedNode = document.RootNode;
        Assert.Same(node, advanced.Node);
        Assert.True(advanced.CanSelectPreset);

        advanced.ParameterJson = "{\"values\":[7]}";
        advanced.ApplyParametersCommand.Execute(null);
        Assert.Equal(7, node.Model.Parameters["values"]![0]!.Value<int>());
        Assert.True(document.IsDirty);
        Assert.Contains("已应用", advanced.Message);
        Assert.Contains(advanced.EffectiveParameters, item => item.Name == "values");

        document.UndoCommand.Execute(null);
        Assert.Equal("{}", advanced.ParameterJson);
        Assert.False(document.IsDirty);
    }

    /// <summary>建立具有已保存检查点和真实能力定义的纯计算一条龙文档。</summary>
    private static PuloniaTaskPlanDocumentViewModel CreateDocument(PuloniaTaskPlan plan)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()).Definitions);
        return document;
    }

    /// <summary>建立只含一个纯计算任务的一条龙计划。</summary>
    private static PuloniaTaskPlan CreateOneDragonPlan(string name) => new()
    {
        Name = name, Purpose = PuloniaTaskPlanPurpose.OneDragon,
        RootTask = new PuloniaTask
        {
            Name = name,
            Children = [new PuloniaTask { Name = "计算", TaskType = "csharp",
                Parameters = new JObject() }]
        }
    };

    /// <summary>建立两个页面共用的编辑会话。</summary>
    private static PuloniaPlanEditingSession CreateSession(PuloniaTaskStore store, PuloniaTaskService service)
        => new(store, new PuloniaTaskClipboardService(), service, new PuloniaTaskResourceCatalog());

    /// <summary>建立不扫描任何程序集的空注册表。</summary>
    private static SoloTaskRegistry EmptyRegistry() => new(Array.Empty<System.Reflection.Assembly>());

    /// <summary>建立不启动游戏会话的纯任务服务。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store) => new(store,
        new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>仅清理当前测试创建的唯一临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
