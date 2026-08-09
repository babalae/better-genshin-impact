using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.System;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class ScriptGroupConfigViewModel : ObservableObject, IViewModel
{
    [ObservableProperty]
    private AutoFightViewModel _autoFightViewModel;

    [ObservableProperty]
    private ScriptGroupConfig _scriptGroupConfig;

    [ObservableProperty]
    private PathingPartyConfig _pathingConfig;

    [ObservableProperty]
    private ShellConfig _shellConfig;

    [ObservableProperty]
    private bool _enableShellConfig;
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _onlyPickEliteDropsSource  = new()
    {
        new KeyValuePair<string, string>("Closed", "关闭功能"),
        new KeyValuePair<string, string>("AllowAutoPickupForNonElite", "非精英允许自动拾取"),
        new KeyValuePair<string, string>("DisableAutoPickupForNonElite", "非精英关闭自动拾取")
    };    
    //跳过策略
    //GroupPhysicalPathSkipPolicy:  配置组且物理路径相同跳过
    //PhysicalPathSkipPolicy:  物理路径相同跳过        
    //SameNameSkipPolicy:   同类型同名跳过
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _skipPolicySource  = new()
    {
        new KeyValuePair<string, string>("GroupPhysicalPathSkipPolicy", "配置组且物理路径相同跳过"),
        new KeyValuePair<string, string>("PhysicalPathSkipPolicy", "物理路径相同跳过"),
        new KeyValuePair<string, string>("SameNameSkipPolicy", "同类型同名跳过")
    };     
    
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _referencePointSource  = new()
    {
        new KeyValuePair<string, string>("StartTime", "开始时间"),
        new KeyValuePair<string, string>("EndTime", "结束时间")
    };

    [ObservableProperty]
    private ObservableCollection<KeyValuePair<RecoverTiming, string>> _recoverTimingSource = new()
    {
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.AnyWaypoint, "任何路径点"),
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.OnlyTeleport, "只在传送点"),
        new KeyValuePair<RecoverTiming, string>(RecoverTiming.Never, "不回复"),
    };
    public ScriptGroupConfigViewModel(AllConfig config, ScriptGroupConfig scriptGroupConfig)
    {
        ScriptGroupConfig = scriptGroupConfig;
        PathingConfig = scriptGroupConfig.PathingConfig;
        PathingConfig.PropertyChanged += OnPathingConfigPropertyChanged;
        AutoFightViewModel = new AutoFightViewModel(config);
        ShellConfig = scriptGroupConfig.ShellConfig;
        EnableShellConfig = scriptGroupConfig.EnableShellConfig;
    }

    // 赶路角色为 自动 或 玛薇卡 时显示跳飞相关配置
    public bool IsHurryOnMwkOrAuto => PathingConfig.HurryOnAvatar is "自动" or "玛薇卡";

    // 赶路角色为 自动/玛薇卡/闲云 时显示跳飞间隔配置
    public bool IsHurryOnMwkOrAutoOrXianyun => PathingConfig.HurryOnAvatar is "自动" or "玛薇卡" or "闲云";

    // 跳飞启用距离：仅当角色为 自动/玛薇卡 且启用跳飞时显示
    public bool IsJumpFlyDistanceVisible => IsHurryOnMwkOrAuto && PathingConfig.MwkJumpFlyEnabled;

    // 跳飞前额外冲刺次数：仅当角色为 自动/玛薇卡 且启用跳飞时显示
    public bool IsJumpFlySprintCountVisible => IsHurryOnMwkOrAuto && PathingConfig.MwkJumpFlyEnabled;

    // 玛薇卡在车上禁用冲刺：仅当角色为 自动/玛薇卡 时显示
    public bool IsDisableSprintVisible => IsHurryOnMwkOrAuto;

    // 切人步行：仅当角色为 自动 或实际应用了该选项的角色（玛薇卡/希诺宁）时显示
    public bool IsSwitchToWalkVisible => PathingConfig.HurryOnAvatar is "自动" or "玛薇卡" or "希诺宁";

    // 接近停车距离：仅当角色为 自动 或存在接近停车逻辑的角色（闲云/法尔伽无此逻辑）时显示
    public bool IsApproachStopDistanceVisible => PathingConfig.HurryOnAvatar is "自动"
        or "玛薇卡" or "希诺宁" or "桑多涅" or "恰斯卡" or "伊法" or "流浪者" or "夜兰";

    private void OnPathingConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PathingPartyConfig.HurryOnAvatar) or nameof(PathingPartyConfig.MwkJumpFlyEnabled))
        {
            OnPropertyChanged(nameof(IsHurryOnMwkOrAuto));
            OnPropertyChanged(nameof(IsHurryOnMwkOrAutoOrXianyun));
            OnPropertyChanged(nameof(IsJumpFlyDistanceVisible));
            OnPropertyChanged(nameof(IsJumpFlySprintCountVisible));
            OnPropertyChanged(nameof(IsDisableSprintVisible));
            OnPropertyChanged(nameof(IsSwitchToWalkVisible));
            OnPropertyChanged(nameof(IsApproachStopDistanceVisible));
        }
    }

    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        AutoFightViewModel.OnOpenLocalScriptRepo();
    }
    [RelayCommand]
    public void OnGetExecutionOrder()
    {
        var index = _pathingConfig.TaskCycleConfig.GetExecutionOrder();
        if (index == -1)
        {
            Toast.Error("计算失败，请检查参数！");
        }
        else
        {
            Toast.Success("当前执行序号为："+index);
        }
    }

    [RelayCommand]
    public void OnOpenFightFolder()
    {
        AutoFightViewModel.OnOpenFightFolder();
    }
    
    [RelayCommand]
    private void OnAutoFightEnabledChecked()
    {
        PathingConfig.Enabled = true;
    }

    [RelayCommand]
    private async Task OnGoToAutoEatUrlAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://www.bettergi.com/dev/js/dispatcher.html#autoeat-自动吃食物"));
    }
}