using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using System.Windows.Input;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 格式必须是 快捷键 与 快捷键Type
/// </summary>
[Serializable]
public partial class HotKeyConfig : ObservableObject
{
    [ObservableProperty] private string _bgiEnabledHotkey = "F11";
    [ObservableProperty] private string _bgiEnabledHotkeyType = HotKeyTypeEnum.GlobalRegister.ToString();

    [ObservableProperty] private string _autoPickEnabledHotkey = "F1";
    [ObservableProperty] private string _autoPickEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoSkipEnabledHotkey = "F2";
    [ObservableProperty] private string _autoSkipEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoFishingEnabledHotkey = "";
    [ObservableProperty] private string _autoFishingEnabledHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _turnAroundHotkey = "F3";
    [ObservableProperty] private string _turnAroundHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _enhanceArtifactHotkey = "F4";
    [ObservableProperty] private string _enhanceArtifactHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _quickBuyHotkey = "";
    [ObservableProperty] private string _quickBuyHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoGeniusInvokationHotkey = "";
    [ObservableProperty] private string _autoGeniusInvokationHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoWoodHotkey = "";
    [ObservableProperty] private string _autoWoodHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoFightHotkey = "";
    [ObservableProperty] private string _autoFightHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();

    [ObservableProperty] private string _autoDomainHotkey = "";
    [ObservableProperty] private string _autoDomainHotkeyType = HotKeyTypeEnum.KeyboardMonitor.ToString();
}