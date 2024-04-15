using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class MacroConfig : ObservableObject
{
    /// <summary>
    ///     高延迟下强化的额外等待时间
    ///     https://github.com/babalae/better-genshin-impact/issues/9
    /// </summary>
    [ObservableProperty]
    private int _enhanceWaitDelay;

    /// <summary>
    ///     F连发时间间隔
    /// </summary>
    [ObservableProperty]
    private int _fFireInterval = 100;

    /// <summary>
    ///     长按F变F连发
    /// </summary>
    [ObservableProperty]
    private bool _fPressHoldToContinuationEnabled;

    /// <summary>
    ///     转圈圈时间间隔
    /// </summary>
    [ObservableProperty]
    private int _runaroundInterval;

    /// <summary>
    ///     转圈圈鼠标右移长度
    /// </summary>
    [ObservableProperty]
    private int _runaroundMouseXInterval = 500;

    /// <summary>
    ///     空格连发时间间隔
    /// </summary>
    [ObservableProperty]
    private int _spaceFireInterval = 100;

    /// <summary>
    ///     长按空格变空格连发
    /// </summary>
    [ObservableProperty]
    private bool _spacePressHoldToContinuationEnabled;

    /// <summary>
    ///     一键战斗宏启用状态
    /// </summary>
    [ObservableProperty]
    private bool _combatMacroEnabled;

    /// <summary>
    ///     一键战斗宏快捷键模式
    /// </summary>
    [ObservableProperty]
    private string _combatMacroHotkeyMode = OneKeyFightTask.HoldOnMode;

    /// <summary>
    ///     一键战斗宏优先级
    /// </summary>
    [ObservableProperty]
    private int _combatMacroPriority;
}
