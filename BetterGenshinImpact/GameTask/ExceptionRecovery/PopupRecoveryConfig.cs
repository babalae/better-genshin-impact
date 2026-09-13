using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 异常弹窗自动处理配置
/// </summary>
[Serializable]
public partial class PopupRecoveryConfig : ObservableObject
{
    /// <summary>
    /// 是否启用异常弹窗自动处理
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;

    /// <summary>
    /// 检测间隔（秒）
    /// </summary>
    [ObservableProperty]
    private int _checkIntervalSeconds = 5;

    /// <summary>
    /// 单次处理上限（秒）
    /// </summary>
    [ObservableProperty]
    private int _maxRecoverySeconds = 30;

    /// <summary>
    /// 追加的弹窗关键词（逗号分隔，用于其它服/其它文案）
    /// </summary>
    [ObservableProperty]
    private string _extraKeywords = "";
}
