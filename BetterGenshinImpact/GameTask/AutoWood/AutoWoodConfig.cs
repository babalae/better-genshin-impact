using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木配置
/// </summary>
[Serializable]
public partial class AutoWoodConfig : ObservableObject
{
    /// <summary>
    /// 使用小道具后的额外延迟（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _afterZSleepDelay = 0;

    /// <summary>
    /// 木材数量OCR是否启用
    /// </summary>
    [ObservableProperty]
    private bool _woodCountOcrEnabled = false;

    /// <summary>
    /// 使用进出千星奇域刷新CD
    /// </summary>
    [ObservableProperty]
    private bool _useWonderlandRefresh = true;

    // /// <summary>
    // /// 按下两次ESC键，原因见：
    // /// https://github.com/babalae/better-genshin-impact/issues/235
    // /// </summary>
    // [ObservableProperty]
    // private bool _pressTwoEscEnabled = false;
}
