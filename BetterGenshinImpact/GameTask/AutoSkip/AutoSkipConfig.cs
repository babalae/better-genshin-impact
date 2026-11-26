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

    /// <summary>
    /// 剧情简介，由大语言模型对当前章节对话内容总结而来
    /// </summary>
    [ObservableProperty]
    private bool _synopsisEnabled = true;

    /// <summary>
    /// 剧情简介风格，根据不同的提示词由大语言模型产生
    /// Serious：正经风格
    /// Funny：搞笑风格
    /// </summary>
    [ObservableProperty]
    private string _synopsisStyle = nameof(SynopsisStyleType.Serious);
    
    /// <summary>
    /// 剧情简介字体大小
    /// </summary>
    [ObservableProperty]
    private int _synopsisFontSize = 26;
    
    partial void OnSynopsisFontSizeChanged(int value)
    {
        if (value < 18) SynopsisFontSize = 18;
        else if (value > 42) SynopsisFontSize = 42;
    }
    
    /// <summary>
    /// 剧情简介每行最大字符数量
    /// </summary>
    [ObservableProperty]
    private int _synopsisMaxCharsPerLine = 40;
    
    partial void OnSynopsisMaxCharsPerLineChanged(int value)
    {
        if (value < 10) SynopsisMaxCharsPerLine = 10;
        else if (value > 200) SynopsisMaxCharsPerLine = 200;
    }
    
    /// <summary>
    /// 剧情简介文本框左上角X坐标位置
    /// </summary>
    [ObservableProperty]
    private int _synopsisLeftTopX = 420;
    
    partial void OnSynopsisLeftTopXChanged(int value)
    {
        if (value < 0) SynopsisLeftTopX = 0;
        else if (value > 1920) SynopsisLeftTopX = 1920;
    }
    
    /// <summary>
    /// 剧情简介文本框左上角Y坐标位置
    /// </summary>
    [ObservableProperty]
    private int _synopsisLeftTopY = 40;
    
    partial void OnSynopsisLeftTopYChanged(int value)
    {
        if (value < 0) SynopsisLeftTopY = 0;
        else if (value > 1080) SynopsisLeftTopY = 1080;
    }
    
    /// <summary>
    /// 剧情简介文本颜色
    /// </summary>
    [ObservableProperty]
    private string _synopsisTextColor = "#33E8D6";
    
    partial void OnSynopsisTextColorChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SynopsisTextColor = "#33E8D6";
        }
    }
    
    /// <summary>
    /// 剧情简介背景颜色
    /// </summary>
    [ObservableProperty]
    private string _synopsisBackgroundColor = "#00000055";
    
    partial void OnSynopsisBackgroundColorChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SynopsisBackgroundColor = "#00000055";
        }
    }

    /// <summary>
    /// 剧情简介持续展示时间
    /// </summary>
    [ObservableProperty]
    private int _synopsisDisplayDurationTimeSecond = 60;
    
    partial void OnSynopsisDisplayDurationTimeSecondChanged(int value)
    {
        if (value < 5) SynopsisDisplayDurationTimeSecond = 5;
        else if (value > 120) SynopsisDisplayDurationTimeSecond = 120;
    }
    
    /// <summary>
    /// 如果已经不在处于对话模式，展示将提前多少秒结束
    /// </summary>
    [ObservableProperty]
    private int _synopsisDisplaySpeedUpSecond = 50;
    
    partial void OnSynopsisDisplaySpeedUpSecondChanged(int value)
    {
        if (value < 0) SynopsisDisplaySpeedUpSecond = 0;
        else if (value > 120) SynopsisDisplaySpeedUpSecond = 120;
    }

    /// <summary>
    /// 性能选项
    /// 【低】每隔0.8s识别一次，最大尝试次数 10次（快速跳过对话模式） / 20次（手动剧情模式）
    /// 【中】每隔0.5s识别一次，最大尝试次数 15次（快速跳过对话模式） / 40次（手动剧情模式）
    /// 【高】每隔0.2s识别一次，最大尝试次数 30次（快速跳过对话模式） / 60次（手动剧情模式）
    /// </summary>
    [ObservableProperty]
    private string _synopsisPerformance = "中";
    
    // public int ChatOptionTextWidth { get; set; } = 280;
    //
    // public int ExpeditionOptionTextWidth { get; set; } = 130;

    /// <summary>
    /// 选择选项前的延迟（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _afterChooseOptionSleepDelay = 0;

    /// <summary>
    /// 选择剧情选项前，通过游戏进程音频的人声检测自动等待语音结束
    /// </summary>
    [ObservableProperty]
    private bool _autoWaitDialogueOptionVoiceEnabled = false;

    /// <summary>
    /// 人声检测等待语音结束的最大等待时间（秒）
    /// </summary>
    [ObservableProperty]
    private int _dialogueOptionVoiceMaxWaitSeconds = 30;

    /// <summary>
    /// 持续检测游戏进程音频并在设置页显示 Silero VAD 诊断数据
    /// </summary>
    [ObservableProperty]
    private bool _dialogueOptionVoiceVadDiagnosticEnabled = false;

    /// <summary>
    /// 点击对话框前的延迟（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _beforeClickConfirmDelay = 0;
    
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
    /// 自定义优先选项启用
    /// </summary>
    [ObservableProperty]
    private bool _customPriorityOptionsEnabled = false;

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
    /// 自动邀约分支角色
    /// </summary>
    [ObservableProperty]
    private string _autoHangoutCharacterChoose = string.Empty;

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
    /// 后台剧情结束后切回游戏前台
    /// </summary>
    [ObservableProperty]
    private bool _bringGameToFrontAfterBackgroundDialogEnabled = false;

    /// <summary>
    /// 提交物品
    /// </summary>
    [ObservableProperty]
    private bool _submitGoodsEnabled = true;

    /// <summary>
    /// 游戏失焦时显示画中画
    /// </summary>
    [ObservableProperty]
    private bool _pictureInPictureEnabled = false;
    
    /// <summary>
    /// 画中画的源图像类型
    /// TriggerDispatcher：来自于截图器50ms一次
    /// CaptureLoop：主动获取（60帧）
    /// </summary>
    [ObservableProperty]
    private string _pictureInPictureSourceType = nameof(PictureSourceType.CaptureLoop);

    /// <summary>
    /// 关闭弹出层
    /// </summary>
    [ObservableProperty]
    private bool _closePopupPagedEnabled = true;

    /// <summary>
    /// JS调用时跳过内置默认点击选项
    /// </summary>
    [ObservableProperty]
    private bool _skipBuiltInClickOptions = false;

    // /// <summary>
    // /// 使用交互键
    // /// 使用鼠标
    // /// </summary>
    // [ObservableProperty]
    // private string _selectChatOptionType = SelectChatOptionTypes.UseMouse;
}


public enum PictureSourceType
{
    TriggerDispatcher,
    CaptureLoop
}

public enum SynopsisStyleType
{
    Serious,
    Funny
}
