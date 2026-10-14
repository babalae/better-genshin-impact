using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoCook;

[Serializable]
public partial class AutoCookConfig : ObservableObject
{
    [ObservableProperty]
    private int _checkIntervalMs = 10;

    [ObservableProperty]
    private bool _stopTaskWhenRecoverButtonDetected = true;

    // 游戏窗口不在前台时继续烹饪，按键与点击改走后台通道，且不抢占游戏窗口焦点
    [ObservableProperty]
    private bool _runBackgroundEnabled;
}
