using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 脚本配置
/// </summary>
[Serializable]
public partial class ScriptConfig : ObservableObject
{
    // 自动更新脚本仓库周期（天）
    [ObservableProperty]
    private int _autoUpdateScriptRepoPeriod = 1;

    // 上次更新脚本仓库时间
    [ObservableProperty]
    private DateTime _lastUpdateScriptRepoTime = DateTime.MinValue;

    // 脚本仓库按钮红点是否展示
    [ObservableProperty]
    private bool _scriptRepoHintDotVisible = false;

    // 已订阅的脚本路径列表
    [ObservableProperty]
    private List<string> _subscribedScriptPaths = [];
    
    // 选择的更新渠道URL
    [ObservableProperty] private string _selectedRepoUrl = "";
}
