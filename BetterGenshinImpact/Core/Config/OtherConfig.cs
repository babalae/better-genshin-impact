using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.GameTask.Common.Job;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;


[Serializable]
public partial class OtherConfig : ObservableObject
{
    //调度器任务和部分独立任务，失去焦点，自动激活游戏窗口
    [ObservableProperty]
    private bool _restoreFocusOnLostEnabled = false;
    //窗口类名优先检测：原版仅靠进程名+MainWindowHandle 查找游戏窗口，该句柄为 0 或指向错误窗口时会找不到。开启后优先按窗口类名枚举检测，未命中再按进程枚举取客户区最大的可见窗口，仍不命中才回退原版方式。即时生效（每次查找窗口时读取），默认关闭，关闭时行为与旧版完全一致
    [ObservableProperty]
    private bool _windowClassDetectPreferred = false;
    //自动领取派遣任务城市
    [ObservableProperty]
    private string _autoFetchDispatchAdventurersGuildCountry = "无";
    //服务器时区偏移量
    [ObservableProperty]
    private TimeSpan _serverTimeZoneOffset = TimeSpan.FromHours(8);
    //物品图标识别模型
    [ObservableProperty]
    private ItemIconRecognitionMode _itemIconRecognitionMode = ItemIconRecognitionMode.Item;
    //锄地规划
    [ObservableProperty]
    private FarmingPlan _farmingPlanConfig = new();
    
    [ObservableProperty]
    private Miyoushe _miyousheConfig = new();
    //OCR配置
    [ObservableProperty]
    private Ocr _ocrConfig = new();
    

    public partial class Miyoushe : ObservableObject
    {

        //cookie
        [ObservableProperty]
        private string _cookie = "";
        
        //与调度器日志处相互同步cookie
        [ObservableProperty]
        private bool _logSyncCookie = true;
        
    }
    public partial class MiyousheDataSupport : ObservableObject
    {
        [ObservableProperty]
        private bool _enabled = false;
        
        //日精英上限
        [ObservableProperty]
        private int _dailyEliteCap = 400;
        
        //日小怪上限
        [ObservableProperty]
        private int _dailyMobCap = 2000;
    }
    public partial class FarmingPlan : ObservableObject
    {


        [ObservableProperty]
        private MiyousheDataSupport _miyousheDataConfig = new();

        [ObservableProperty]
        private bool _enabled = false;
        
        //日精英上限
        [ObservableProperty]
        private int _dailyEliteCap = 400;
        
        //日小怪上限
        [ObservableProperty]
        private int _dailyMobCap = 2000;
        
    }
    
    public partial class Ocr : ObservableObject
    {
        /// <summary>
        ///     PaddleOCR模型配置
        /// </summary>
        [ObservableProperty]
        private PaddleOcrModelConfig _paddleOcrModelConfig = PaddleOcrModelConfig.V4Auto;
    }
    
    //public partial class OtherConfig : ObservableObject
    
    /// <summary>
    /// 游戏语言名称
    /// </summary>
    [ObservableProperty]
    private string _gameCultureInfoName = "zh-Hans";

    /// <summary>
    /// BGI界面语言名称
    /// </summary>
    [ObservableProperty]
    private string _uiCultureInfoName = "zh-Hans";

}
