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
}
