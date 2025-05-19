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
        var index = _pathingConfig.TaskCycleConfig.GetExecutionOrder(DateTime.Now);
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
}