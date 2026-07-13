using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// shell执行配置
/// </summary>
[Serializable]
public partial class ShellConfig : ObservableObject
{
    // 禁用Shell任务
    [ObservableProperty] private bool _disable;

    // 最长等待命令返回的时间,单位秒，<=0不等待,直接返回。
    [ObservableProperty] private int _timeout = 60;

    // 隐藏命令执行窗口
    [ObservableProperty] private bool _noWindow = true;

    // 向log打印命令执行输出
    [ObservableProperty] private bool _output = true;
}
