using CommunityToolkit.Mvvm.ComponentModel;
using System;
using BetterGenshinImpact.GameTask.AutoSkip.Model;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 自动跳过剧情配置
/// </summary>
[Serializable]
public partial class AutoSkipConfig : ObservableObject
{
    /// <summary>
    /// 触发器是否启用
    /// 启用后：
    /// 1. 快速跳过对话
    /// 2. 自动点击一个识别到的选项
    /// 3. 黑屏过长自动点击跳过
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// 快速跳过对话
    /// </summary>
    [ObservableProperty]
    private bool _quicklySkipConversationsEnabled = true;

    // public int ChatOptionTextWidth { get; set; } = 280;
    //
    // public int ExpeditionOptionTextWidth { get; set; } = 130;

    /// <summary>
    /// 选择选项前的延迟（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _afterChooseOptionSleepDelay = 0;

    /// <summary>
    /// 自动领取每日委托奖励
    /// </summary>
    [ObservableProperty]
    private bool _autoGetDailyRewardsEnabled = true;

    /// <summary>
    /// 自动重新派遣
    /// </summary>
    [ObservableProperty]
    private bool _autoReExploreEnabled = true;

    /// <summary>
    /// 自动重新派遣使用角色配置，逗号分割
    /// </summary>
    [Obsolete]
    [ObservableProperty]
    private string _autoReExploreCharacter = "";

    /// <summary>
    /// 优先选择第一个选项
    /// 优先选择最后一个选项
    /// 不选择选项
    /// </summary>
    [ObservableProperty]
    private string _clickChatOption = "优先选择第一个选项";
    
    /// <summary>  
    /// 自定义优先选项文本，每行一个或用分号分隔  
    /// </summary>  
    [ObservableProperty]  
    private string _customPriorityOptions = "";

    /// <summary>
    /// 自动邀约启用
    /// </summary>
    [ObservableProperty]
    private bool _autoHangoutEventEnabled = false;

    /// <summary>
    /// 自动邀约分支选择
    /// </summary>
    [ObservableProperty]
    private string _autoHangoutEndChoose = string.Empty;

    /// <summary>
    /// 自动邀约选择选项前的延迟（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _autoHangoutChooseOptionSleepDelay = 0;

    /// <summary>
    /// 自动邀约自动点击跳过按钮
    /// </summary>
    [ObservableProperty]
    private bool _autoHangoutPressSkipEnabled = true;

    public bool IsClickFirstChatOption()
    {
        return ClickChatOption == "优先选择第一个选项";
    }

    public bool IsClickRandomChatOption()
    {
        return ClickChatOption == "随机选择选项";
    }
    public bool IsClickCustomPriorityOption()  
    {  
        return ClickChatOption == "自定义优先选项";  
    }
    public bool IsClickNoneChatOption()
    {
        return ClickChatOption == "不选择选项";
    }

    /// <summary>
    /// 后台运行
    /// </summary>
    [ObservableProperty]
    private bool _runBackgroundEnabled = false;

    /// <summary>
    /// 提交物品
    /// </summary>
    [ObservableProperty]
    private bool _submitGoodsEnabled = true;

    /// <summary>
    /// 关闭弹出层
    /// </summary>
    [ObservableProperty]
    private bool _closePopupPagedEnabled = true;

    // /// <summary>
    // /// 使用交互键
    // /// 使用鼠标
    // /// </summary>
    // [ObservableProperty]
    // private string _selectChatOptionType = SelectChatOptionTypes.UseMouse;
}