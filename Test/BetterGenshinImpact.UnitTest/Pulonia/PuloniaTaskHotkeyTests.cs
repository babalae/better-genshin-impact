using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using FormsKeys = System.Windows.Forms.Keys;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>两种快捷键、配置兼容和卡片开关回归；不创建窗口或安装真实系统钩子。</summary>
public sealed class PuloniaTaskHotkeyTests : IDisposable
{
    /// <summary>只用于本测试的独占本地存储目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-hotkeys-" + Guid.NewGuid().ToString("N"));

    /// <summary>两种模式分别支持组合 / 功能键和单键 / 侧键。</summary>
    [Theory]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Ctrl + Alt + F8")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Ctrl + A")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "F8")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Shift + F8")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Shift + A")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Shift + Space")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "F8")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "A")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "Space")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "F12")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "XButton1")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "XButton2")]
    public void Validation_AcceptsSupportedInputs(HotKeyTypeEnum type, string hotkey)
        => PuloniaTaskSchedule.Validate(new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey, HotkeyType = type, Hotkey = hotkey });

    /// <summary>拒绝模式不兼容、修饰键单独使用及无效枚举键，不能保存后静默失效。</summary>
    [Theory]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "A")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "XButton1")]
    [InlineData(HotKeyTypeEnum.GlobalRegister, "Ctrl + F12")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "Ctrl + F8")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "LeftCtrl")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "System")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "256")]
    [InlineData(HotKeyTypeEnum.KeyboardMonitor, "< None >")]
    [InlineData((HotKeyTypeEnum)100, "F8")]
    public void Validation_RejectsUnsupportedInputs(HotKeyTypeEnum type, string hotkey)
        => Assert.Throws<FormatException>(() => PuloniaTaskSchedule.Validate(new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey, HotkeyType = type, Hotkey = hotkey }));

    /// <summary>旧配置仍是全局模式且内容签名不变，新监听模式参与本地保存和排队复核签名。</summary>
    [Fact]
    public void Serialization_PreservesLegacyGlobalSignatureAndPersistsMonitorType()
    {
        var trigger = new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey, Hotkey = "F8" };
        var json = PuloniaTaskJson.Write(trigger);
        Assert.Null(JObject.Parse(json)["hotkey_type"]);
        var restored = PuloniaTaskJson.Read<PuloniaTaskTrigger>(json);
        Assert.Equal(HotKeyTypeEnum.GlobalRegister, restored.HotkeyType);
        var originalSignature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(originalSignature, PuloniaTaskSchedule.Signature(restored));

        trigger.HotkeyType = HotKeyTypeEnum.KeyboardMonitor;
        restored = PuloniaTaskJson.Read<PuloniaTaskTrigger>(PuloniaTaskJson.Write(trigger));
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor, restored.HotkeyType);
        Assert.NotEqual(originalSignature, PuloniaTaskSchedule.Signature(restored));
    }

    /// <summary>类型切换复用既有输入名称，并清空旧键值而不是把组合键误转成监听单键。</summary>
    [Fact]
    public void Editor_SwitchesExistingHotkeyTypesAndClearsOldInput()
    {
        var editor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey }, new PuloniaTaskPlan());
        Assert.Equal(HotKeyTypeEnum.GlobalRegister.ToChineseName(), editor.HotkeyTypeName);
        editor.SwitchHotkeyTypeCommand.Execute(null);
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor.ToChineseName(), editor.HotkeyTypeName);
        Assert.True(editor.Hotkey.IsEmpty);
        Assert.False(editor.IsValid);
        editor.Hotkey = HotKey.FromString("XButton1");
        Assert.True(editor.IsValid);
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor, editor.CreateTrigger().HotkeyType);
        editor.SwitchHotkeyTypeCommand.Execute(null);
        Assert.True(editor.Hotkey.IsEmpty);
        Assert.False(editor.IsValid);
    }

    /// <summary>卡片开关只持久化启用状态，即使右侧草稿无效也不能丢失草稿或将其保存。</summary>
    [Fact]
    public async Task CardToggle_SavesOnlyEnabledAndPreservesInvalidDraft()
    {
        using var store = new PuloniaTaskStore(_directory);
        var trigger = new PuloniaTaskTrigger { Name = "每日任务" };
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Triggers = [trigger] });
        await using var service = CreateService(store);
        var page = new PuloniaTaskPlanViewModel(store, new PuloniaTaskClipboardService(), service,
            new PuloniaTaskResourceCatalog(), new PuloniaTaskHistoryViewModel(service, store));
        await page.InitializeCommand.ExecuteAsync(null);
        var editor = page.TriggerEditor!;
        editor.Name = "未保存的名称";
        editor.Hour = "99";
        Assert.False(editor.IsValid);

        await page.ToggleTriggerEnabledCommand.ExecuteAsync(Assert.Single(page.Triggers));
        var saved = Assert.Single((await store.ListPlansAsync()).Single(value => value.Id == plan.Id).Triggers);
        Assert.True(saved.Enabled);
        Assert.Equal("每日任务", saved.Name);
        Assert.Equal("0 4 * * *", saved.Cron);
        Assert.Same(editor, page.TriggerEditor);
        Assert.Equal("未保存的名称", editor.Name);
        Assert.Equal("99", editor.Hour);
        Assert.True(editor.Enabled);

        await page.ToggleTriggerEnabledCommand.ExecuteAsync(Assert.Single(page.Triggers));
        Assert.False(Assert.Single((await store.ListPlansAsync()).Single(value => value.Id == plan.Id).Triggers).Enabled);
        Assert.Same(editor, page.TriggerEditor);
        Assert.False(editor.Enabled);
    }

    /// <summary>正在新增的独立草稿不会因切换已有卡片的开关而自动选中、替换或提交。</summary>
    [Fact]
    public async Task CardToggle_PreservesNewDraftWithoutSubmittingIt()
    {
        using var store = new PuloniaTaskStore(_directory);
        await store.SavePlanAsync(new PuloniaTaskPlan { Triggers = [new PuloniaTaskTrigger()] });
        await using var service = CreateService(store);
        var page = new PuloniaTaskPlanViewModel(store, new PuloniaTaskClipboardService(), service,
            new PuloniaTaskResourceCatalog(), new PuloniaTaskHistoryViewModel(service, store));
        await page.InitializeCommand.ExecuteAsync(null);
        page.AddHotkeyTriggerCommand.Execute(null);
        var editor = page.TriggerEditor!;
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor, editor.HotkeyType);
        editor.Name = "新草稿";
        await page.ToggleTriggerEnabledCommand.ExecuteAsync(Assert.Single(page.Triggers));
        Assert.Null(page.SelectedTrigger);
        Assert.Same(editor, page.TriggerEditor);
        Assert.Equal("新草稿", editor.Name);
        Assert.Single((await store.ListPlansAsync()).Single().Triggers);
    }

    /// <summary>监听单键与同主键的全局组合键也冲突，而两种不同修饰组合可独立注册。</summary>
    [Fact]
    public void Conflict_UsesActualMonitorSemantics()
    {
        Assert.True(HotKeySettingModel.AreConflicting(HotKey.FromString("F8"), HotKeyTypeEnum.KeyboardMonitor,
            HotKey.FromString("Ctrl + F8"), HotKeyTypeEnum.GlobalRegister));
        Assert.False(HotKeySettingModel.AreConflicting(HotKey.FromString("Alt + F8"), HotKeyTypeEnum.GlobalRegister,
            HotKey.FromString("Ctrl + F8"), HotKeyTypeEnum.GlobalRegister));
        Assert.False(HotKeySettingModel.AreConflicting(HotKey.None, HotKeyTypeEnum.KeyboardMonitor,
            HotKey.None, HotKeyTypeEnum.KeyboardMonitor));
    }

    /// <summary>复用模型注册监听只登记共享表；冲突失败不创建 NativeWindow，也不移除原监听。</summary>
    [Fact]
    public void SharedModel_RejectsCrossTypeConflictWithoutStealingExistingMonitor()
    {
        var first = new HotKeySettingModel("原功能", "test/first", "F23", HotKeyTypeEnum.KeyboardMonitor.ToString(), null);
        var second = new HotKeySettingModel("计划", "test/second", "Ctrl + F23", HotKeyTypeEnum.GlobalRegister.ToString(), null);
        try
        {
            first.RegisterHotKey();
            Assert.False(first.HotKey.IsEmpty);
            second.RegisterHotKey();
            Assert.True(second.HotKey.IsEmpty);
            Assert.NotNull(second.RegistrationError);
            Assert.Null(second.GlobalRegisterHook);
            Assert.Same(first.KeyboardMonitorHook, KeyboardHook.AllKeyboardHooks[FormsKeys.F23]);
            second.UnRegisterHotKey();
            Assert.Same(first.KeyboardMonitorHook, KeyboardHook.AllKeyboardHooks[FormsKeys.F23]);
        }
        finally { second.UnRegisterHotKey(); first.UnRegisterHotKey(); }
    }

    /// <summary>底层监听重复键注册失败后的 Dispose 不能清掉真正拥有该键的旧对象。</summary>
    [Fact]
    public void FailedMonitorRegistration_CleanupKeepsOriginalOwners()
    {
        var keyboard = new KeyboardHook();
        var duplicateKeyboard = new KeyboardHook();
        var mouse = new MouseHook();
        var duplicateMouse = new MouseHook();
        try
        {
            keyboard.RegisterHotKey(FormsKeys.F24);
            mouse.RegisterHotKey(FormsMouseButtons.XButton2);
            Assert.Throws<ArgumentException>(() => duplicateKeyboard.RegisterHotKey(FormsKeys.F24));
            Assert.Throws<ArgumentException>(() => duplicateMouse.RegisterHotKey(FormsMouseButtons.XButton2));
            duplicateKeyboard.Dispose(); duplicateMouse.Dispose();
            Assert.Same(keyboard, KeyboardHook.AllKeyboardHooks[FormsKeys.F24]);
            Assert.Same(mouse, MouseHook.AllMouseHooks[FormsMouseButtons.XButton2]);
        }
        finally { duplicateKeyboard.Dispose(); duplicateMouse.Dispose(); keyboard.Dispose(); mouse.Dispose(); }
    }

    /// <summary>主宿主关闭时也释放监听模式，并继续支持 Application.Current 为空和重复清理。</summary>
    [Fact]
    public void HostShutdown_ReleasesSharedMonitorWithoutApplication()
    {
        Assert.Null(Application.Current);
        using var host = new PuloniaTaskTriggerHost(null!, null!, null!, NullLogger<PuloniaTaskTriggerHost>.Instance, TimeProvider.System);
        var model = new HotKeySettingModel("计划", "test/shutdown", "F22", HotKeyTypeEnum.KeyboardMonitor.ToString(), null);
        try
        {
            model.RegisterHotKey();
            var hotkeys = (List<HotKeySettingModel>)typeof(PuloniaTaskTriggerHost)
                .GetField("_hotkeys", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            hotkeys.Add(model);
            var release = (Action)typeof(PuloniaTaskTriggerHost).GetMethod("ReleaseHotkeys", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate(typeof(Action), host);
            release(); release();
            Assert.False(KeyboardHook.AllKeyboardHooks.ContainsKey(FormsKeys.F22));
            Assert.Null(model.KeyboardMonitorHook);
        }
        finally { model.UnRegisterHotKey(); }
    }

    /// <summary>使用纯 C# 执行能力建立服务，不启动游戏、截图器或热键宿主。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store)
        => new(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>只删除本测试独占且验证过路径的临时目录。</summary>
    public void Dispose()
    {
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith("bgi-pulonia-hotkeys-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试目录越界。");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
