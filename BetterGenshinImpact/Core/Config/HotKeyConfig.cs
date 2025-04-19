using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     格式必须是 快捷键 与 快捷键Type
/// </summary>
[Serializable]
public partial class HotKeyConfig : ObservableObject
{
    [ObservableProperty]
    private string _autoTrackHotkey = "";

    [ObservableProperty]
    private string _autoTrackHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoDomainHotkey = "";

    [ObservableProperty]
    private string _autoDomainHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoFightHotkey = "";

    [ObservableProperty]
    private string _autoFightHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoFishingEnabledHotkey = "";

    [ObservableProperty]
    private string _autoFishingEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoGeniusInvokationHotkey = "";

    [ObservableProperty]
    private string _autoGeniusInvokationHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoPickEnabledHotkey = "";

    [ObservableProperty]
    private string _autoPickEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoSkipEnabledHotkey = "";

    [ObservableProperty]
    private string _autoSkipEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoSkipHangoutEnabledHotkey = "";

    [ObservableProperty]
    private string _autoSkipHangoutEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _autoWoodHotkey = "";

    [ObservableProperty]
    private string _autoWoodHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _bgiEnabledHotkey = "F11";

    [ObservableProperty]
    private string _bgiEnabledHotkeyType = HotKeyTypeEnum.GlobalRegister.ToString();

    [ObservableProperty]
    private string _enhanceArtifactHotkey = "";

    [ObservableProperty]
    private string _enhanceArtifactHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _quickBuyHotkey = "";

    [ObservableProperty]
    private string _quickBuyHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _quickSereniteaPotHotkey = "";

    [ObservableProperty]
    private string _quickSereniteaPotHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _quickTeleportEnabledHotkey = "";

    [ObservableProperty]
    private string _quickTeleportEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 快捷传送触发
    [ObservableProperty]
    private string _quickTeleportTickHotkey = "";

    [ObservableProperty]
    private string _quickTeleportTickHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 截图
    [ObservableProperty]
    private string _takeScreenshotHotkey = "";

    [ObservableProperty]
    private string _takeScreenshotHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty]
    private string _turnAroundHotkey = "";

    [ObservableProperty]
    private string _turnAroundHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 点击确认按钮
    [ObservableProperty]
    private string _clickGenshinConfirmButtonHotkey = "";

    [ObservableProperty]
    private string _clickGenshinConfirmButtonHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 点击取消按钮
    [ObservableProperty]
    private string _clickGenshinCancelButtonHotkey = "";

    [ObservableProperty]
    private string _clickGenshinCancelButtonHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 一键战斗宏
    [ObservableProperty]
    private string _oneKeyFightHotkey = "";

    [ObservableProperty]
    private string _oneKeyFightHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 地图路线录制开始/停止
    [ObservableProperty]
    private string _mapPosRecordHotkey = "";

    [ObservableProperty]
    private string _mapPosRecordHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 活动音游开始/停止
    [ObservableProperty]
    private string _autoMusicGameHotkey = "";

    [ObservableProperty]
    private string _autoMusicGameHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
    
    // 活动音游开始/停止
    [ObservableProperty]
    private string _autoFishingGameHotkey = "";

    [ObservableProperty]
    private string _autoFishingGameHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 自动寻路
    [ObservableProperty]
    private string _autoTrackPathHotkey = "";

    [ObservableProperty]
    private string _autoTrackPathHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 测试
    [ObservableProperty]
    private string _test1Hotkey = "";

    [ObservableProperty]
    private string _test1HotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 测试2
    [ObservableProperty]
    private string _test2Hotkey = "";

    [ObservableProperty]
    private string _test2HotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 路径记录开始
    [ObservableProperty]
    private string _recBigMapPosHotkey = "";

    [ObservableProperty]
    private string _recBigMapPosHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 路径记录开始
    [ObservableProperty]
    private string _pathRecorderHotkey = "";

    [ObservableProperty]
    private string _pathRecorderHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 添加路径记录点
    [ObservableProperty]
    private string _addWaypointHotkey = "";

    [ObservableProperty]
    private string _addWaypointHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 路径执行
    [ObservableProperty]
    private string _executePathHotkey = "";

    [ObservableProperty]
    private string _executePathHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 日志与状态窗口展示
    [ObservableProperty]
    private string _logBoxDisplayHotkey = "";

    [ObservableProperty]
    private string _logBoxDisplayHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    // 键鼠录制/停止
    [ObservableProperty]
    private string _keyMouseMacroRecordHotkey = "";

    [ObservableProperty]
    private string _keyMouseMacroRecordHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
    // 暂停
    [ObservableProperty]
    private string _suspendHotkey = "";

    [ObservableProperty]
    private string _suspendHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
    // 停止任意独立任务
    [ObservableProperty]
    private string _cancelTaskHotkey = "";

    [ObservableProperty]
    private string _cancelTaskHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
    
    // 停止任意独立任务
    [ObservableProperty]
    private string _onedragonHotkey = "";

    [ObservableProperty]
    private string _onedragonHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
}
