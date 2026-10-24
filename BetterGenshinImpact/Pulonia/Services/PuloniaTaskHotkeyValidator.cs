using System;
using System.Windows.Input;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 热键触发器的键位规则，属于热键入口侧：触发器宿主注册前和编辑器保存前调用。
/// 从 <see cref="PuloniaTaskSchedule"/> 移出，使调度计算与计划校验不依赖 WPF 输入类型。
/// </summary>
public static class PuloniaTaskHotkeyValidator
{
    /// <summary>校验热键模式和键值组合。</summary>
    /// <param name="trigger">热键触发器配置。</param>
    /// <exception cref="FormatException">模式未定义或键位不被所选模式支持。</exception>
    public static void Validate(PuloniaTaskTrigger trigger)
    {
        var hotkey = HotKey.FromString(trigger.Hotkey);
        if (!Enum.IsDefined(trigger.HotkeyType) || hotkey.IsEmpty || !Enum.IsDefined(hotkey.Key)
            || !Enum.IsDefined(hotkey.MouseButton)
            || (hotkey.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) != 0)
            throw new FormatException("请选择有效的热键类型并设置快捷键。");
        // 两种模式沿用现有热键系统的边界：监听只接受单键 / 侧键，全局注册接受组合键 / 功能键。
        if (trigger.HotkeyType == HotKeyTypeEnum.KeyboardMonitor)
        {
            if (hotkey.Modifiers != ModifierKeys.None
                || hotkey.MouseButton is not (MouseButton.Left or MouseButton.XButton1 or MouseButton.XButton2)
                || hotkey.MouseButton != MouseButton.Left && hotkey.Key != Key.None)
                throw new FormatException("键鼠监听只支持键盘单键或鼠标侧键，不支持组合键。");
            if (hotkey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2) return;
        }
        else if (hotkey.MouseButton != MouseButton.Left || hotkey.Key == Key.F12
            || hotkey.Modifiers == ModifierKeys.None && hotkey.Key is not (>= Key.F1 and <= Key.F24))
            throw new FormatException("全局热键支持组合键或功能键；普通键需搭配 Ctrl / Alt / Win，F12 为系统保留。");
        if (hotkey.Key is Key.None or Key.System or Key.Clear or Key.OemClear or Key.Apps or Key.ImeProcessed or Key.DeadCharProcessed
                or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new FormatException("请设置有效的键盘键，不可只使用修饰键。");
    }
}
