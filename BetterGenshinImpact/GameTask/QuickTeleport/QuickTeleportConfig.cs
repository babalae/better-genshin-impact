using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.QuickTeleport;

/// <summary>
/// 快速传送配置
/// </summary>
[Serializable]
public partial class QuickTeleportConfig : ObservableObject
{
    /// <summary>
    /// 快速传送是否启用
    /// </summary>
    [ObservableProperty] private bool _enabled = false;

    /// <summary>
    /// 点击候选列表传送点的间隔时间(ms)
    /// </summary>
    [ObservableProperty] private int _teleportListClickDelay = 200;

    /// <summary>
    /// 等待右侧传送弹出界面的时间(ms)
    /// 0.24 版本后，这个值可以设置为 0，因为识图时间变久了。0.24 版本前，建议设置为 100
    /// </summary>
    [ObservableProperty] private int _waitTeleportPanelDelay = 10;
}