using System.Collections.Generic;
using System.Windows.Documents;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using System.IO;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.Model;
using System.Threading;
using MessageBox = System.Windows.MessageBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class TaskSettingsPageViewModel : ObservableObject, INavigationAware
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;
    private readonly TaskTriggerDispatcher _taskDispatcher;

    private CancellationTokenSource? _cts;


    [ObservableProperty] private string[] _strategyList;
    [ObservableProperty] private string _switchAutoGeniusInvokationButtonText;


    public TaskSettingsPageViewModel(IConfigService configService, INavigationService navigationService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        Config = configService.Get();
        _navigationService = navigationService;
        _taskDispatcher = taskTriggerDispatcher;

        _strategyList = LoadCustomScript();
        _switchAutoGeniusInvokationButtonText = "启动";
    }

    private string[] LoadCustomScript()
    {
        var files = Directory.GetFiles(Global.Absolute(@"Config\AutoGeniusInvokation"), "*.*",
            SearchOption.AllDirectories);

        var strategyList = new string[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].EndsWith(".txt"))
            {
                var fileName = Path.GetFileNameWithoutExtension(files[i]);
                strategyList[i] = fileName;
            }
        }

        return strategyList;
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public void OnSwitchAutoGeniusInvokation()
    {
        try
        {
            if (SwitchAutoGeniusInvokationButtonText == "启动")
            {
                if (string.IsNullOrEmpty(Config.AutoGeniusInvokationConfig.StrategyName))
                {
                    MessageBox.Show("请先选择策略");
                    return;
                }

                var path = Global.Absolute(@"Config\AutoGeniusInvokation\" + Config.AutoGeniusInvokationConfig.StrategyName + ".txt");

                if (!File.Exists(path))
                {
                    MessageBox.Show("策略文件不存在");
                    return;
                }

                var content = File.ReadAllText(path);
                _cts = new CancellationTokenSource();
                var param = new GeniusInvokationTaskParam(_cts, _taskDispatcher, content);
                _taskDispatcher.StartIndependentTask(IndependentTaskEnum.AutoGeniusInvokation, param);
                SwitchAutoGeniusInvokationButtonText = "停止";
            }
            else
            {
                _cts?.Cancel();
                SwitchAutoGeniusInvokationButtonText = "启动";
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message);
        }   
    }
}