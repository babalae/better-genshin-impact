using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     自动进入游戏模式
/// </summary>
public enum AutoEnterGameMode
{
    /// <summary>始终执行：任何情况下都自动进入游戏（原"开启"行为）</summary>
    [Description("始终执行")]
    Always,

    /// <summary>不执行：不自动进入游戏（原"关闭"行为）</summary>
    [Description("不执行")]
    Never,

    /// <summary>仅联动启动时执行：只在 BGI 自己启动原神后自动进入游戏</summary>
    [Description("仅BGI自启原神时执行")]
    OnlyWhenLinkedStart,
}

/// <summary>
///     原神启动配置
/// </summary>
[Serializable]
public partial class GenshinStartConfig : ObservableObject
{
    // /// <summary>
    // ///     自动点击月卡
    // /// </summary>
    // [ObservableProperty]
    // private bool _autoClickBlessingOfTheWelkinMoonEnabled;

    /// <summary>
    ///     自动进入游戏（开门）模式
    /// </summary>
    [ObservableProperty]
    private AutoEnterGameMode _autoEnterGameMode = AutoEnterGameMode.Always;

    /// <summary>
    ///     兼容旧配置：旧版本 AutoEnterGameEnabled(bool) 自动迁移到 AutoEnterGameMode
    /// </summary>
    [JsonProperty("AutoEnterGameEnabled")]
    private bool? AutoEnterGameEnabledLegacy
    {
        set
        {
            if (value != null)
            {
                AutoEnterGameMode = value.Value ? AutoEnterGameMode.Always : AutoEnterGameMode.Never;
            }
        }
    }

    /// <summary>
    ///     原神启动参数
    /// </summary>
    [ObservableProperty]
    private string _genshinStartArgs = "";

    /// <summary>
    ///     原神安装路径
    /// </summary>
    [ObservableProperty]
    private string _installPath = "";

    /// <summary>
    ///     联动启动原神本体
    /// </summary>
    [ObservableProperty]
    private bool _linkedStartEnabled = true;

    /// <summary>
    ///     使用Starward同步记录时间
    /// </summary>
    [ObservableProperty]
    private bool _recordGameTimeEnabled = false;

    [ObservableProperty]
    private bool _startGameWithCmd = false;

    /// <summary>
    ///     启动前自动关闭原神 HDR（删除原神 HDR 对应注册表键）
    /// </summary>
    [ObservableProperty]
    private bool _autoDisableGenshinHdrEnabled = true;
}
