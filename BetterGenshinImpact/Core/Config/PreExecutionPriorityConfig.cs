using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;


[Serializable]
public partial class PreExecutionPriorityConfig: ObservableObject
{
    
    // 配置是否启用
    [ObservableProperty]
    private bool _enabled = false;
    /// <summary>
    /// 需要优先检查执行的配置组名称，多个用逗号分隔。
    /// </summary>
    [ObservableProperty]
    private string _groupNames  = "";

    /// <summary>
    /// 最大重试次数，如果优先的任务执行失败了，重试几次。
    /// </summary>
    [ObservableProperty]
    private int _maxRetryCount = 1;
}
