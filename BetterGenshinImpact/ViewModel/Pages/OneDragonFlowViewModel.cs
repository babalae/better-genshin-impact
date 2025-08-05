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
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using System.Windows.Controls;
using ABI.Windows.UI.UIAutomation;
using Wpf.Ui;
using StackPanel = Wpf.Ui.Controls.StackPanel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Service.Interface;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using System.Collections.Specialized;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    public static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");

    private readonly ScriptService _scriptService;

    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        new("领取邮件"),
        new("合成树脂"),
        // new ("每日委托"),
        new("自动秘境"),
        // new ("自动锻造"),
        // new ("自动刷地脉花"),
        new("领取每日奖励"),
        new ("领取尘歌壶奖励"),
        // new ("自动七圣召唤"),
    ];


    [ObservableProperty] private OneDragonTaskItem _selectedTask;

    partial void OnSelectedTaskChanged(OneDragonTaskItem value)
    {
        if (value != null)
        {
            InputScriptGroupName = value.Name;
        }
    }

    // 其他属性和方法...
    [ObservableProperty] private string _inputScriptGroupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _playTaskList = new ObservableCollection<OneDragonTaskItem>();

    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = new ObservableCollection<ScriptGroup>();

    [ObservableProperty] private ObservableCollection<ScriptGroup> _scriptGroupsdefault =
        new ObservableCollection<ScriptGroup>()
        {
            new() { Name = "领取邮件" },
            new() { Name = "合成树脂" },
            new() { Name = "自动秘境" },
            new() { Name = "领取每日奖励" },
            new() {Name = "领取尘歌壶奖励" },
        };

    private readonly string _scriptGroupPath = Global.Absolute(@"User\ScriptGroup");
    private readonly string _basePath = AppDomain.CurrentDomain.BaseDirectory;
    
    private void ReadScriptGroup()
    {
        try
        {
            if (!Directory.Exists(_scriptGroupPath))
            {
                Directory.CreateDirectory(_scriptGroupPath);
            }

            ScriptGroups.Clear();
            foreach (var group in _scriptGroupsdefault)
            {
                ScriptGroups.Add(group);
            }

            var files = Directory.GetFiles(_scriptGroupPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var group = ScriptGroup.FromJson(json);

                    var nst = TaskContext.Instance().Config.NextScheduledTask.Find(item => item.Item1 == group.Name);
                    foreach (var item in group.Projects)
                    {
                        item.NextFlag = false;
                        if (nst != default)
                        {
                            if (nst.Item2 == item.Index && nst.Item3 == item.FolderName && nst.Item4 == item.Name)
                            {
                                item.NextFlag = true;
                            }
                        }
                    }

                    ScriptGroups.Add(group);
                }
                catch (Exception e)
                {
                    _logger.LogInformation(e, "读取配置组配置时失败");
                }
            }

            ScriptGroups = new ObservableCollection<ScriptGroup>(ScriptGroups.OrderBy(g => g.Index));
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "读取配置组配置时失败");
        }
    }

    private async void AddNewTaskGroup()
    {
        ReadScriptGroup();
        var selectedGroupNamePick = await OnStartMultiScriptGroupAsync();
        if (selectedGroupNamePick == null)
        {
            return;
        }
        int pickTaskCount = selectedGroupNamePick.Split(',').Count();
        foreach (var selectedGroupName in selectedGroupNamePick.Split(','))
        {
            var taskItem = new OneDragonTaskItem(selectedGroupName)
            {
                IsEnabled = true
            };
            if (TaskList.All(task => task.Name != taskItem.Name))
            {
                var names = selectedGroupName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .ToList();
                bool containsAnyDefaultGroup =
                    names.Any(name => ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == name));
                if (containsAnyDefaultGroup)
                {
                    int lastDefaultGroupIndex = -1;
                    for (int i = TaskList.Count - 1; i >= 0; i--)
                    {
                        if (ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == TaskList[i].Name))
                        {
                            lastDefaultGroupIndex = i;
                            break;
                        }
                    }
                    if (lastDefaultGroupIndex >= 0)
                    {
                        TaskList.Insert(lastDefaultGroupIndex + 1, taskItem);
                    }
                    else
                    {
                        TaskList.Insert(0, taskItem);
                    }
                    if (pickTaskCount == 1)
                    {
                        Toast.Success("一条龙任务添加成功");
                    }
                }
                else
                {
                    TaskList.Add(taskItem);
                    if (pickTaskCount == 1)
                    {
                        Toast.Success("配置组添加成功");
                    }
                }
            }
            else
            {
                if (pickTaskCount == 1)
                {
                    Toast.Warning("任务或配置组已存在");
                }
            } 
        }
        if (pickTaskCount > 1)
        {
                Toast.Success(pickTaskCount + " 个任务添加成功");  
        }
    }

    public async Task<string?> OnStartMultiScriptGroupAsync()
    {
        var stackPanel = new StackPanel();
        var checkBoxes = new Dictionary<ScriptGroup, CheckBox>();
        CheckBox selectedCheckBox = null; // 用于保存当前选中的 CheckBox
        foreach (var scriptGroup in ScriptGroups)
        {
            if (TaskList.Any(taskName => scriptGroup.Name == taskName.Name))
            {
                continue; // 只有当文件名完全相同时才跳过显示
            }
            var checkBox = new CheckBox
            {
                Content = scriptGroup.Name,
                Tag = scriptGroup,
                IsChecked = false // 初始状态下都未选中
            };
            checkBoxes[scriptGroup] = checkBox;
            stackPanel.Children.Add(checkBox);
        }
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
        Title = "选择增加的配置组（可多选）",
        Content = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        },
        CloseButtonText = "关闭",
        PrimaryButtonText = "确认",
        Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.Width , // 确保弹窗根据内容自动调整大小
        MaxHeight = 600,
        };
        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            List<string> selectedItems = new List<string>(); // 用于存储所有选中的项
            foreach (var checkBox in checkBoxes.Values)
            {
                if (checkBox.IsChecked == true)
                {
                    // 确保 Tag 是 ScriptGroup 类型，并返回其 Name 属性
                    var scriptGroup = checkBox.Tag as ScriptGroup;
                    if (scriptGroup != null)
                    { 
                        selectedItems.Add(scriptGroup.Name);
                    }
                    else
                    {
                        Toast.Error("配置组加载失败");
                    }
                }
            }
            return string.Join(",", selectedItems); // 返回所有选中的项
        }
        return null;
    }
    
    [ObservableProperty] private ObservableCollection<OneDragonFlowConfig> _configList = [];
    /// <summary>
    /// 当前生效配置
    /// </summary>
    [ObservableProperty] private OneDragonFlowConfig? _selectedConfig;

    [ObservableProperty] private List<string> _craftingBenchCountry = ["枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty] private List<string> _adventurersGuildCountry = ["枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty] private List<string> _domainNameList = ["", ..MapLazyAssets.Instance.DomainNameList];

    [ObservableProperty] private List<string> _completionActionList = ["无", "关闭游戏", "关闭游戏和软件", "关机"];

    [ObservableProperty] private List<string> _sundayEverySelectedValueList = ["","1", "2", "3"];
    
    [ObservableProperty] private List<string> _sundaySelectedValueList = ["","1", "2", "3"];

    [ObservableProperty] private List<string> _secretTreasureObjectList = ["布匹","须臾树脂","大英雄的经验","流浪者的经验","精锻用魔矿","摩拉","祝圣精华","祝圣油膏"];
    
    [ObservableProperty] private List<string> _sereniteaPotTpTypes = ["地图传送", "尘歌壶道具"];
    
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

        TaskList.CollectionChanged += (sender, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (OneDragonTaskItem newItem in e.NewItems)
                {
                    newItem.PropertyChanged += TaskPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (OneDragonTaskItem oldItem in e.OldItems)
                {
                    oldItem.PropertyChanged -= TaskPropertyChanged;
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                SaveConfig();
            }
        };
    }

    public override void OnNavigatedTo()
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
        LoadDisplayTaskListFromConfig(); // 加载 DisplayTaskList 从配置文件
        SetSomeSelectedConfig(SelectedConfig);
    }

    // 新增方法：从配置文件加载 DisplayTaskList

    public void LoadDisplayTaskListFromConfig()
    {
        if (SelectedConfig == null || SelectedConfig.TaskEnabledList == null)
        {
            return;
        }

        TaskList.Clear();
        foreach (var kvp in SelectedConfig.TaskEnabledList)
        {
            var taskItem = new OneDragonTaskItem(kvp.Key)
            {
                IsEnabled = kvp.Value
            };
            TaskList.Add(taskItem);
            // _logger.LogInformation($"加载配置: {kvp.Key} {kvp.Value}");
        }
    }

    [RelayCommand]
    private void DeleteConfigDisplayTaskListFromConfig()
    {
        if (SelectedConfig == null || SelectedTask == null ||
            SelectedConfig.TaskEnabledList == null) //|| SelectedConfig.TaskEnabledList == null 
        {
            Toast.Warning("请先选择配置组和任务");
            return;
        }

        TaskList.Clear();
        foreach (var kvp in SelectedConfig.TaskEnabledList)
        {
            var taskItem = new OneDragonTaskItem(kvp.Key)
            {
                IsEnabled = kvp.Value
            };
            if (taskItem.Name != InputScriptGroupName)
            {
                TaskList.Add(taskItem);
                taskItem = null;
                Toast.Information("已经删除");
            }
        }
    }

    [RelayCommand]
    private void OnConfigDropDownChanged()
    {
        SetSomeSelectedConfig(SelectedConfig);
        SelectedTask = null;
    }

    public void SaveConfig()
    {
        if (SelectedConfig == null)
        {
            return;
        }

        SelectedConfig.TaskEnabledList.Clear();
        foreach (var task in TaskList)
        {
            SelectedConfig.TaskEnabledList[task.Name] = task.IsEnabled;
        }

        WriteConfig(SelectedConfig);
    }
    
    [RelayCommand]
    private void AddTaskGroup()
    {
        AddNewTaskGroup();
        SaveConfig();
        SelectedTask = null;
    }

    [RelayCommand]
    private void SaveActionConfig()
    {
        SaveConfig();
        Toast.Information("排序已保存");
    }

    public void SetSomeSelectedConfig(OneDragonFlowConfig? selected)
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

            LoadDisplayTaskListFromConfig();
        }
    }

    private async void TaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        await Task.Delay(100); //等会加载完再保存
        SaveConfig();
    }

    private void ConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveConfig();
        WriteConfig(SelectedConfig);
    }

    public void WriteConfig(OneDragonFlowConfig? config)
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
    
    private bool _autoRun = true;
    
    [RelayCommand]
    private void OnLoaded()
    {
        // 组件首次加载时运行一次。
        if (!_autoRun)
        {
            return;
        }
        _autoRun = false;
        //
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].Contains("startOneDragon"))
        {
            // 通过命令行参数启动一条龙。
            if (args.Length > 2)
            {
                // 从命令行参数中提取一条龙配置名称。
                _logger.LogInformation($"参数指定的一条龙配置：{args[2]}");
                var argsOneDragonConfig = ConfigList.FirstOrDefault(x => x.Name == args[2], null);
                if (argsOneDragonConfig != null)
                {
                    // 设定配置，配置下拉框会选定。
                    SelectedConfig = argsOneDragonConfig;
                    // 调用选定更新函数。
                    OnConfigDropDownChanged();
                }
                else
                {
                    _logger.LogWarning("未找到，请检查。");
                }
            }
            // 异步执行一条龙
            Toast.Information($"命令行一条龙「{SelectedConfig.Name}」。");
            OnOneKeyExecute();
        }
    }

    [RelayCommand]
    public async Task OnOneKeyExecute()
    {
        _logger.LogInformation($"启用一条龙配置：{SelectedConfig.Name}");
        var taskListCopy = new List<OneDragonTaskItem>(TaskList);//避免执行过程中修改TaskList
        foreach (var task in taskListCopy)
        {
            task.InitAction(SelectedConfig);
        }

        int finishOneTaskcount = 1;
        int finishTaskcount = 1;
        int enabledTaskCountall = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation($"启用任务总数量: {enabledTaskCountall}");

        await ScriptService.StartGameTask();

        ReadScriptGroup();
        foreach (var task in ScriptGroupsdefault)
        {
            ScriptGroups.Remove(task);
        }

        foreach (var scriptGroup in ScriptGroups)
        {
            SelectedConfig.TaskEnabledList.Remove(scriptGroup.Name);
        }

        if (SelectedConfig == null || taskListCopy.Count(t => t.IsEnabled) == 0)
        {
            Toast.Warning("请先选择任务");
            _logger.LogInformation("没有配置,退出执行!");
            return;
        }

        int enabledoneTaskCount = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation($"启用一条龙任务的数量: {enabledoneTaskCount}");

        await ScriptService.StartGameTask();
        SaveConfig();
        int enabledTaskCount = SelectedConfig.TaskEnabledList.Count(t =>
            t.Value && ScriptGroupsdefault.All(defaultTask => defaultTask.Name != t.Key));
        _logger.LogInformation($"启用配置组任务的数量: {enabledTaskCount}");

        if (enabledoneTaskCount <= 0)
        {
            _logger.LogInformation("没有一条龙任务!");
        }

        Notify.Event(NotificationEvent.DragonStart).Success("一条龙启动");
        foreach (var task in taskListCopy)
        {
            if (task is { IsEnabled: true, Action: not null })
            {
                if (ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == task.Name))
                {
                    _logger.LogInformation($"一条龙任务执行: {finishOneTaskcount++}/{enabledoneTaskCount}");
                    await new TaskRunner().RunThreadAsync(async () =>
                    {
                        await task.Action();
                        await Task.Delay(1000);
                    });
                }
                else
                {
                    try
                    {
                        if (enabledTaskCount <= 0)
                        {
                            _logger.LogInformation("没有配置组任务,退出执行!");
                            return;
                        }

                        Notify.Event(NotificationEvent.DragonStart).Success("配置组任务启动");

                        if (SelectedConfig.TaskEnabledList[task.Name])
                        {
                            _logger.LogInformation($"配置组任务执行: {finishTaskcount++}/{enabledTaskCount}");
                            await Task.Delay(500);
                            string filePath = Path.Combine(_basePath, _scriptGroupPath, $"{task.Name}.json");
                            var group = ScriptGroup.FromJson(await File.ReadAllTextAsync(filePath));
                            IScriptService? scriptService = App.GetService<IScriptService>();
                            await scriptService!.RunMulti(ScriptControlViewModel.GetNextProjects(group), group.Name);
                            await Task.Delay(1000);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogDebug(e, "执行配置组任务时失败");
                        Toast.Error("执行配置组任务时失败");
                    }
                }
                // 如果任务已经被取消，中断所有任务
                if (CancellationContext.Instance.Cts.IsCancellationRequested)
                {
                    _logger.LogInformation("任务被取消，退出执行");
                    Notify.Event(NotificationEvent.DragonEnd).Success("一条龙和配置组任务结束");
                    return; // 后续的检查任务也不执行
                }
            }
        }

        // 检查和最终结束的任务
        await new TaskRunner().RunThreadAsync(async () =>
        {
            await new CheckRewardsTask().Start(CancellationContext.Instance.Cts.Token);
            await Task.Delay(500);
            Notify.Event(NotificationEvent.DragonEnd).Success("一条龙和配置组任务结束");
            _logger.LogInformation("一条龙和配置组任务结束");

            // 执行完成后操作
            if (SelectedConfig != null && !string.IsNullOrEmpty(SelectedConfig.CompletionAction))
            {
                switch (SelectedConfig.CompletionAction)
                {
                    case "关闭游戏":
                        SystemControl.CloseGame();
                        break;
                    case "关闭游戏和软件":
                        SystemControl.CloseGame();
                        Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
                        break;
                    case "关机":
                        SystemControl.CloseGame();
                        SystemControl.Shutdown();
                        break;
                }
            }
        });
    }

    [RelayCommand]
    private void DeleteTaskGroup()
    {
        DeleteConfigDisplayTaskListFromConfig();
        SaveConfig();
        InputScriptGroupName = null;
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

        SaveConfig();
    }
}