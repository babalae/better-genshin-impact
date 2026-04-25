using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.RevivePrompt;

/// <summary>
/// 复苏提示配置
/// </summary>
[Serializable]
public partial class RevivePromptConfig : ObservableObject
{
    /// <summary>
    /// 是否启用自动点击复苏提示
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;
}
