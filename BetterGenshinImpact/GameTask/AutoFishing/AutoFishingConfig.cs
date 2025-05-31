using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoFishing;

/// <summary>
/// 自动钓鱼配置
/// </summary>
[Serializable]
public partial class AutoFishingConfig : ObservableObject
{
    /// <summary>
    /// 触发器是否启用
    /// 启用后：
    /// 1. 自动判断是否进入钓鱼状态
    /// 2. 自动提杆
    /// 3. 自动拉条
    /// </summary>
    [ObservableProperty] private bool _enabled = false;

    ///// <summary>
    ///// 鱼儿上钩文字识别区域
    ///// 暂时无用
    ///// </summary>
    //[ObservableProperty] private Rect _fishHookedRecognitionArea = Rect.Empty;

    /// <summary>
    /// 自动抛竿是否启用
    /// </summary>
    [ObservableProperty] private bool _autoThrowRodEnabled = false;

    /// <summary>
    /// 自动抛竿未上钩超时时间(秒)
    /// </summary>
    [ObservableProperty] private int _autoThrowRodTimeOut = 15;
    
    /// <summary>
    /// 整个任务超时时间
    /// </summary>
    [ObservableProperty]
    private int _wholeProcessTimeoutSeconds = 300;

    /// <summary>
    /// 昼夜策略
    /// 钓全天的鱼、还是只钓白天或夜晚的鱼
    /// </summary>
    [ObservableProperty]
    private FishingTimePolicy _fishingTimePolicy = FishingTimePolicy.All;

    /// <summary>
    /// torch库文件地址
    /// </summary>
    [ObservableProperty]
    private string _torchDllFullPath = @"C:\torch\lib\torch_cpu.dll";
}