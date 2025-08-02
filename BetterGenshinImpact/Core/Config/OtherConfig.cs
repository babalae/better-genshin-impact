using System;
using System.DirectoryServices.ActiveDirectory;
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
    [ObservableProperty]
    private AutoRestart _autoRestartConfig = new();
    //锄地规划
    [ObservableProperty]
    private FarmingPlan _farmingPlanConfig = new();
    
    [ObservableProperty]
    private Miyoushe _miyousheConfig = new();

    // 自定义角色配置
    [ObservableProperty]
    private CustomAvatarConfig _customAvatarConfigOut = new CustomAvatarConfig();
    
    public partial class CustomAvatarConfig : ObservableObject
    {
        //自定义角色开关
        public  bool CustomAvatarEnabled { set; get; } = false;
        
        // 自定义角色1名称,初始化用于举例
        public string CustomAvatar1Name { get; set; } = "申鹤";
        public string CustomAvatar1Name2 { get; set; } = "甘雨";
        public string CustomAvatar1Name3 { get; set; } = "芭芭拉";
    
        // 自定义角色1假装名称
        public string CustomAvatar1DisplayName { get; set; } = "琴";
    
        // 自定义角色2名称
        public string CustomAvatar2Name { get; set; } = "凯亚";
        public string CustomAvatar2Name2 { get; set; } = string.Empty;
        public string CustomAvatar2Name3 { get; set; } = string.Empty;
    
        // 自定义角色2假装名称
        public string CustomAvatar2DisplayName { get; set; } = "夜兰";
    
        // 自定义置信度1
        public double CustomAvatar1Confidence { get; set; } = 0.7;
    
        // 自定义置信度2
        public double CustomAvatar2Confidence { get; set; } = 0.7;
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