using System;
using System.Collections.Generic;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    public static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        new("领取邮件"),
        new("合成树脂"),
        // new ("每日委托"),
        new("自动秘境"),
        // new ("自动锻造"),
        // new ("自动刷地脉花"),
        new("领取每日奖励"),
        // new ("领取尘歌壶奖励"),
        // new ("自动七圣召唤"),
    ];

    [ObservableProperty]
    private OneDragonTaskItem? _selectedTask;

    [ObservableProperty]
    private ObservableCollection<OneDragonFlowConfig> _configList = [];

    /// <summary>
    /// 当前生效配置
    /// </summary>
    [ObservableProperty]
    private OneDragonFlowConfig? _selectedConfig;

    [ObservableProperty]
    private List<string> _craftingBenchCountry = ["枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty]
    private List<string> _adventurersGuildCountry = ["枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty]
    private List<string> _domainNameList = ["", ..MapLazyAssets.Instance.DomainNameList];
    
    public AllConfig Config { get; set; } = TaskContext.Instance().Config;

    public OneDragonFlowViewModel()
    {
        ConfigList.CollectionChanged += (sender, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (OneDragonFlowConfig newItem in e.NewItems)
                {
                    newItem.PropertyChanged += ConfigPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (OneDragonFlowConfig oldItem in e.OldItems)
                {
                    oldItem.PropertyChanged -= ConfigPropertyChanged;
                }
            }
        };
        InitConfigList();
    }

    public void OnNavigatedTo()
    {
        InitConfigList();
    }

    private void InitConfigList()
    {
        Directory.CreateDirectory(OneDragonFlowConfigFolder);
        // 读取文件夹内所有json配置，按创建时间正序
        var configFiles = Directory.GetFiles(OneDragonFlowConfigFolder, "*.json");
        var configs = new List<OneDragonFlowConfig>();

        OneDragonFlowConfig? selected = null;
        foreach (var configFile in configFiles)
        {
            var json = File.ReadAllText(configFile);
            var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            if (config != null)
            {
                configs.Add(config);
                if (config.Name == TaskContext.Instance().Config.SelectedOneDragonFlowConfigName)
                {
                    selected = config;
                }
            }
        }

        if (selected == null)
        {
            if (configs.Count > 0)
            {
                selected = configs[0];
            }
            else
            {
                selected = new OneDragonFlowConfig
                {
                    Name = "默认配置"
                };
                configs.Add(selected);
            }
        }

        ConfigList.Clear();
        foreach (var config in configs)
        {
            ConfigList.Add(config);
        }

        SelectedConfig = selected;
        SetSomeSelectedConfig(SelectedConfig);
    }

    [RelayCommand]
    private void OnConfigDropDownChanged()
    {
        SetSomeSelectedConfig(SelectedConfig);
    }

    private void SetSomeSelectedConfig(OneDragonFlowConfig? selected)
    {
        if (SelectedConfig != null)
        {
            TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
            foreach (var task in TaskList)
            {
                if (SelectedConfig.TaskEnabledList.TryGetValue(task.Name, out var value))
                {
                    task.IsEnabled = value;
                }
            }
        }
    }

    private void ConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        WriteConfig(SelectedConfig);
    }

    private void WriteConfig(OneDragonFlowConfig? config)
    {
        if (config == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(OneDragonFlowConfigFolder);
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var filePath = Path.Combine(OneDragonFlowConfigFolder, $"{config.Name}.json");
            File.WriteAllText(filePath, json);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "保存配置时失败");
            Toast.Error("保存配置时失败");
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task OnOneKeyExecute()
    {
        // 根据配置初始化任务
        foreach (var task in TaskList)
        {
            task.InitAction(SelectedConfig);
        }

        // 没启动的时候先启动
        await ScriptService.StartGameTask();

        await new TaskRunner(DispatcherTimerOperationEnum.UseSelfCaptureImage)
            .RunThreadAsync(async () =>
            {
                foreach (var task in TaskList)
                {
                    if (task is { IsEnabled: true, Action: not null })
                    {
                        await task.Action();
                        await Task.Delay(1000);
                    }
                }
            });
    }

    [RelayCommand]
    private void OnAddTask()
    {
        Toast.Information("正在开发中...");
    }

    [RelayCommand]
    private void OnAddConfig()
    {
        // 添加配置
        var str = PromptDialog.Prompt("请输入一条龙配置名称", "新增一条龙配置");
        if (!string.IsNullOrEmpty(str))
        {
            // 检查是否已存在
            if (ConfigList.Any(x => x.Name == str))
            {
                Toast.Warning($"一条龙配置 {str} 已经存在，请勿重复添加");
            }
            else
            {
                var nc = new OneDragonFlowConfig { Name = str };
                ConfigList.Insert(0, nc);
                SelectedConfig = nc;
            }
        }
    }
}