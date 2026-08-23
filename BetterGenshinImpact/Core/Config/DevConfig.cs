using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
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

    // Recognition 模板制作工具最近使用的配置文件
    [ObservableProperty]
    private string _recognitionJsonPath = "";

    /// <summary>
    /// Recognition 模板制作工具最近使用的配置文件历史，按最近使用顺序保存，最多 10 条。
    /// </summary>
    [ObservableProperty]
    private List<string> _recognitionJsonPathHistory = [];

    // Recognition 模板制作工具最近使用的 Assets 根目录
    [ObservableProperty]
    private string _recognitionAssetsRootPath = "";

    /// <summary>
    /// Recognition 模板制作工具最近使用的输出文件夹历史，按最近使用顺序保存，最多 10 条。
    /// </summary>
    [ObservableProperty]
    private List<string> _recognitionAssetsRootPathHistory = [];
}
