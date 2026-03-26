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
}
