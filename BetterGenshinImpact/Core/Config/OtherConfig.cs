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

    /// <summary>
    /// 独立任务运行期间是否临时开启 Windows 勿扰，任务结束后恢复原通知设置。
    /// </summary>
    [ObservableProperty]
    private bool _suppressWindowsNotificationsDuringSoloTask;

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
    [ObservableProperty]
    private Miyoushe _miyousheConfig = new();
    // OCR 模型改为可插拔能力，选择保存在 PluginConfig.Selections；旧的 ocrConfig 字段不再读取，所有用户使用 PP-OCRv6
    

    public partial class Miyoushe : ObservableObject
    {

        //cookie
        [ObservableProperty]
        private string _cookie = "";
        
        //与调度器日志处相互同步cookie
        [ObservableProperty]
        private bool _logSyncCookie = true;
        
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
