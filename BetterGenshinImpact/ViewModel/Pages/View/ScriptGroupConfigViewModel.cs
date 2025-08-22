using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    public ScriptGroupConfigViewModel(AllConfig config, ScriptGroupConfig scriptGroupConfig)
    {
        ScriptGroupConfig = scriptGroupConfig;
        PathingConfig = scriptGroupConfig.PathingConfig;
        AutoFightViewModel = new AutoFightViewModel(config);
        ShellConfig = scriptGroupConfig.ShellConfig;
        EnableShellConfig = scriptGroupConfig.EnableShellConfig;
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
        var cfg = _pathingConfig.TaskCycleConfig;
        int todayOrder = cfg.GetExecutionOrder(DateTime.Now);

        if (todayOrder == -1)
        {
            Toast.Error("计算失败，请检查参数！");
            return;
        }

        int diff = (cfg.Index - todayOrder + cfg.Cycle) % cfg.Cycle;
        int distance = diff == 0 ? 1 : diff + 1;
        int daysUntil = diff == 0 ? cfg.Cycle : diff;

        // 显示友好的执行顺序信息
        string message;
        if (distance == 1)
        {
            message = $"当前应执行序号为：{todayOrder} 的任务，" +
                      $"当前任务周期内序号：{distance}，" +
                      $"目标序号：{cfg.Index} —— 今天即可执行！";
        }
        else
        {
            message = $"当前应执行序号为：{todayOrder} 的任务，" +
                      $"当前任务周期内序号：{distance}，" +
                      $"目标序号：{cfg.Index}，距今还有 {daysUntil} 天";
        }

        Toast.Success(message);
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
}