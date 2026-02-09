using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.System;
using Wpf.Ui.Violeta.Controls;
using BetterGenshinImpact.Helpers;

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
        new KeyValuePair<string, string>("Closed", Lang.S["Gen_12469_1f5c46"]),
        new KeyValuePair<string, string>("AllowAutoPickupForNonElite", Lang.S["Gen_12468_bfddb6"]),
        new KeyValuePair<string, string>("DisableAutoPickupForNonElite", Lang.S["Gen_12467_f6afb0"])
    };    
    //跳过策略
    //GroupPhysicalPathSkipPolicy:  配置组且物理路径相同跳过
    //PhysicalPathSkipPolicy:  物理路径相同跳过        
    //SameNameSkipPolicy:   同类型同名跳过
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _skipPolicySource  = new()
    {
        new KeyValuePair<string, string>("GroupPhysicalPathSkipPolicy", Lang.S["Gen_12466_f32b83"]),
        new KeyValuePair<string, string>("PhysicalPathSkipPolicy", Lang.S["Gen_12465_5e15c1"]),
        new KeyValuePair<string, string>("SameNameSkipPolicy", Lang.S["Gen_12464_700ada"])
    };     
    
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<string, string>> _referencePointSource  = new()
    {
        new KeyValuePair<string, string>("StartTime", Lang.S["GameTask_11797_592c59"]),
        new KeyValuePair<string, string>("EndTime", Lang.S["GameTask_11796_f78277"])
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
        var index = _pathingConfig.TaskCycleConfig.GetExecutionOrder();
        if (index == -1)
        {
            Toast.Error(Lang.S["Gen_1067_729062"]);
        }
        else
        {
            Toast.Success(Lang.S["Gen_1068_48e692"]+index);
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
        await Launcher.LaunchUriAsync(new Uri(Lang.S["Gen_12463_fc1746"]));
    }
}