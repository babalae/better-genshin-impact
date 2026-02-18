using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;


[Serializable]
public partial class OtherConfig : ObservableObject
{
    //调度器任务和部分独立任务，失去焦点，自动激活游戏窗口
    [ObservableProperty]
    private bool _restoreFocusOnLostEnabled = false;
    //自动领取派遣任务城市
    [ObservableProperty]
    private string _autoFetchDispatchAdventurersGuildCountry = "无";
    //服务器时区偏移量
    [ObservableProperty]
    private TimeSpan _serverTimeZoneOffset = TimeSpan.FromHours(8);
    [ObservableProperty]
    private AutoRestart _autoRestartConfig = new();
    //锄地规划
    [ObservableProperty]
    private FarmingPlan _farmingPlanConfig = new();
    
    [ObservableProperty]
    private Miyoushe _miyousheConfig = new();
    //OCR配置
    [ObservableProperty]
    private Ocr _ocrConfig = new();
    

    public partial class AutoRestart : ObservableObject
    {
        [ObservableProperty]
        private bool _enabled = false;
        
        //调度器任务连续异常退出几次任务自动重启
        [ObservableProperty]
        private int _failureCount = 5;
        
        //是否同时重启游戏，需开启首页启动配置：同时启动原神、自动进入游戏，此配置才会生效
        [ObservableProperty]
        private bool _restartGameTogether = false;
        
        //锄地脚本，如果打架次数不一致，则判定任务失败。
        [ObservableProperty]
        private bool _isFightFailureExceptional = false;
        
        //任何追踪任务，未走完全路径结束，视为失败。
        [ObservableProperty]
        private bool _isPathingFailureExceptional = false;
        
    }
    
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

        /// <summary>
        ///     允许OCR结果中出现连续重复字符（关闭CTC重复字符折叠）
        /// </summary>
        [ObservableProperty]
        private bool _allowDuplicateChar;

        /// <summary>
        ///     切换队伍时使用 OcrMatch 模糊匹配代替正则表达式匹配
        /// </summary>
        [ObservableProperty]
        private bool _useOcrMatchForPartySwitch = true;

        /// <summary>
        ///     OcrMatch 模糊匹配的默认阈值 (0~1)，分数 ≥ 阈值视为匹配成功
        /// </summary>
        [ObservableProperty]
        private double _ocrMatchDefaultThreshold = 0.8;

        partial void OnOcrMatchDefaultThresholdChanged(double value)
        {
            if (value is <= 0 or > 1)
            {
                OcrMatchDefaultThreshold = Math.Clamp(value, 0.01, 1);
            }
        }

        /// <summary>
        ///     PaddleOCR 识别置信度阈值 (0~1)，低于此阈值的字符将被过滤
        /// </summary>
        [ObservableProperty]
        private double _paddleOcrThreshold = 0.5;

        partial void OnPaddleOcrThresholdChanged(double value)
        {
            if (value is < 0 or >= 1)
            {
                PaddleOcrThreshold = Math.Clamp(value, 0, 0.99);
            }
        }
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
