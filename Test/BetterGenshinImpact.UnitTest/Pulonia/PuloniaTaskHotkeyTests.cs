using System.Reflection;
using System.Windows;
using System.Windows.Input;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;
using FormsKeys = System.Windows.Forms.Keys;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>两种快捷键、配置序列化和冲突注册回归；不创建窗口或安装真实系统钩子。</summary>
public sealed class PuloniaTaskHotkeyTests
{
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
    {
        var trigger = new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey, HotkeyType = type, Hotkey = hotkey };
        PuloniaTaskSchedule.Validate(trigger);
        PuloniaTaskHotkeyValidator.Validate(trigger);
    }

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
        => Assert.Throws<FormatException>(() => PuloniaTaskHotkeyValidator.Validate(new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey, HotkeyType = type, Hotkey = hotkey }));

    /// <summary>快捷键类型作为普通配置字段持久化，展示文本始终使用实际快捷键。</summary>
    [Fact]
    public void Serialization_PersistsHotkeyTypeAndDerivedDisplayText()
    {
        var trigger = new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey, Hotkey = "F8", HotkeyType = HotKeyTypeEnum.KeyboardMonitor };
        Assert.Equal("F8", trigger.DisplayText);
        var json = PuloniaTaskJson.Write(trigger);
        Assert.NotNull(JObject.Parse(json)["hotkey_type"]);
        var restored = PuloniaTaskJson.Read<PuloniaTaskTrigger>(json);
        Assert.Equal(HotKeyTypeEnum.KeyboardMonitor, restored.HotkeyType);
        Assert.Equal("F8", restored.DisplayText);
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
}
