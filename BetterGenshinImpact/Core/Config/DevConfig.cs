using CommunityToolkit.Mvvm.ComponentModel;
using System;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 脚本配置
/// </summary>
[Serializable]
public partial class DevConfig : ObservableObject
{
    // 录制地图名称
    [ObservableProperty]
    private string _recordMapName = MapTypes.Teyvat.ToString();

    // 实时追踪地图窗口是否置顶
    [ObservableProperty]
    private bool _mapViewerTopmost;

    // 路线录制默认作者
    [ObservableProperty]
    private string _recordDefaultAuthorName = string.Empty;

    // 路线录制默认作者链接
    [ObservableProperty]
    private string _recordDefaultAuthorLinks = string.Empty;

    // 路线录制常用作者
    [ObservableProperty]
    private string _recordCommonAuthorsJson = string.Empty;

    // 路线录制动作菜单中的其他动作
    [ObservableProperty]
    private string _recordRareActionCodes = string.Empty;
}
