﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Interface;
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
        new KeyValuePair<string, string>("Closed", App.GetService<ILocalizationService>().GetString("config.eliteDrops.closed")),
        new KeyValuePair<string, string>("AllowAutoPickupForNonElite", App.GetService<ILocalizationService>().GetString("config.eliteDrops.allowNonElite")),
        new KeyValuePair<string, string>("DisableAutoPickupForNonElite", App.GetService<ILocalizationService>().GetString("config.eliteDrops.disableNonElite"))
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
        var localizationService = App.GetService<ILocalizationService>();
        var index = _pathingConfig.TaskCycleConfig.GetExecutionOrder(DateTime.Now);
        if (index == -1)
        {
            Toast.Error(localizationService.GetString("toast.calculationFailed"));
        }
        else
        {
            Toast.Success(localizationService.GetString("toast.currentExecutionIndex", index));
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