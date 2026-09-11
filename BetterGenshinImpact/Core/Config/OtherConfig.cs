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
    //窗口类名优先检测：原版仅靠进程名+MainWindowHandle 查找游戏窗口，该句柄为 0 或指向错误窗口时会找不到。开启后优先按窗口类名枚举检测，未命中再按进程枚举取客户区最大的可见窗口，仍不命中才回退原版方式。即时生效（每次查找窗口时读取），默认关闭，关闭时行为与旧版完全一致
    [ObservableProperty]
    private bool _windowClassDetectPreferred = false;

    //游戏异常弹窗自动恢复 / 断网自动暂停（实时触发页「异常弹窗处理」「断网自动暂停」两张卡片）
    //单独成一个配置段，是为了能像其它功能段（AutoEatConfig/SkillCdConfig…）一样
    //在 AllConfig.InitEvent() 里被订阅：改动即时落盘 + 触发触发器刷新
    [ObservableProperty]
    private ExceptionRecovery _exceptionRecoveryConfig = new();

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
    

    /// <summary>
    /// 游戏异常弹窗自动恢复（L1）与断网自动暂停（L2）的配置段。
    /// 独立成段是为了纳入 AllConfig.InitEvent() 的订阅（改动即时保存 + 刷新触发器配置），
    /// 与 AutoEatConfig/SkillCdConfig 等既有功能段保持一致。
    /// </summary>
    public partial class ExceptionRecovery : ObservableObject
    {
        //===== L1 异常弹窗处理（更新通知 / 连接中断等）=====
        //异常弹窗自动处理开关（默认关闭，由用户在实时触发页自行开启）
        [ObservableProperty]
        private bool _popupRecoveryEnabled = false;
        //弹窗探测间隔（秒）：2~60，默认 30（沿用同类实现的 30 秒节奏：弹窗会一直等人点，无需秒级响应）
        [ObservableProperty]
        private int _popupRecoveryProbeIntervalSeconds = 30;
        //单次恢复的点按轮数上限
        [ObservableProperty]
        private int _popupRecoveryClickRounds = 6;
        //单次恢复的超时时间（分钟）：须覆盖自动重登流程（ExitAndReloginJob 最坏约 6 分钟）
        [ObservableProperty]
        private int _popupRecoveryTimeoutMinutes = 10;
        //连续失败多少次后熔断（0 = 不熔断）
        [ObservableProperty]
        private int _popupRecoveryMaxAttempts = 3;
        //熔断冷却时间（分钟）
        [ObservableProperty]
        private int _popupRecoveryCooldownMinutes = 10;
        //恢复时允许自动重新登录（默认关闭）
        [ObservableProperty]
        private bool _popupRecoveryAutoReloginEnabled = false;
        //追加的弹窗关键词（逗号/分号/竖线分隔，用于国际服等文案不同的客户端）
        [ObservableProperty]
        private string _popupRecoveryExtraKeywords = "";

        //===== L2 断网自动暂停 =====
        //网络检测失败时自动暂停脚本开关
        [ObservableProperty]
        private bool _networkDetectionEnabled = false;
        //Ping 地址（无需 http 前缀）
        [ObservableProperty]
        private string _networkDetectionUrl = "www.baidu.com";
        //备用 Ping 地址（主地址不可达时复核；界面未暴露，可改 JSON）
        [ObservableProperty]
        private string _networkDetectionSecondaryUrl = "www.qq.com";
        //检测间隔（秒）
        [ObservableProperty]
        private int _networkDetectionInterval = 30;
        //连续失败阈值（0 = 不因网络暂停脚本）
        [ObservableProperty]
        private int _networkDetectionFailureThreshold = 3;
        //挂起上限（分钟，按来源分别计时，超时后强制恢复脚本执行）
        [ObservableProperty]
        private int _networkSuspendMaxMinutes = 30;
    }

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
