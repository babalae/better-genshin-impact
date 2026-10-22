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
using BetterGenshinImpact.GameTask.OneDragon;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Service.Interface;
using System.Collections.Specialized;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    public static readonly string OneDragonFlowConfigFolder = OneDragonFlowConfigStore.Folder;

    /// <summary>
    /// 一条龙启动按钮的展示状态，与独立任务页按钮共用 <see cref="BetterGenshinImpact.View.Controls.SoloTaskButton"/>
    /// </summary>
    public SoloTaskButtonState OneDragonSoloTaskState { get; } = new(OneDragonTask.TaskId);

    /// <summary>
    /// 一条龙按钮的状态刷新与启动、停止、多实例管理流程
    /// </summary>
    private readonly SoloTaskButtonController _soloTaskButtonController;

    /// <summary>
    /// 上一次刷新时是否有实例正在运行一条龙，用于在运行结束时重新读取首领讨伐累计次数
    /// </summary>
    private bool _wasOneDragonRunning;

    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList =
        new(OneDragonTaskItem.BuiltInTaskNames.Select(name => new OneDragonTaskItem(name)));


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
        new(OneDragonTaskItem.BuiltInTaskNames.Select(name => new ScriptGroup { Name = name }));

    private readonly string _scriptGroupPath = Global.Absolute(@"User\ScriptGroup");
    
    public void ReadScriptGroup()
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

                    var nst = Config.NextScheduledTask.Find(item => item.Item1 == group.Name);
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
        // 这个方法现在由XAML中的Popup处理，保留为空或者可以删除
        // 实际逻辑已经移到ProcessSelectedGroups方法中
    }

    public void ProcessSelectedGroups(List<string> selectedGroupNames)
    {
        if (selectedGroupNames == null || !selectedGroupNames.Any())
        {
            return;
        }

        int pickTaskCount = selectedGroupNames.Count;
        
        foreach (var selectedGroupName in selectedGroupNames)
        {
            var taskItem = new OneDragonTaskItem(selectedGroupName)
            {
                IsEnabled = true
            };
            taskItem.Id = GenerateUniqueTaskId();
            
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
        if (pickTaskCount > 1)
        {
            Toast.Success(pickTaskCount + " 个任务添加成功");  
        }
    }

    // 原来的OnStartMultiScriptGroupAsync方法已被移除，功能已迁移到XAML Popup中
    
    [ObservableProperty] private ObservableCollection<OneDragonFlowConfig> _configList = [];
    /// <summary>
    /// 当前生效配置
    /// </summary>
    [ObservableProperty] private OneDragonFlowConfig? _selectedConfig;

    [ObservableProperty] private List<string> _craftingBenchCountry = ["枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty] private List<string> _adventurersGuildCountry = ["挪德卡莱", "枫丹", "稻妻", "璃月", "蒙德"];

    [ObservableProperty] private List<string> _domainNameList = ["", ..MapLazyAssets.Get().DomainNameList];

    [ObservableProperty] private List<string> _completionActionList = ["无", "关闭游戏", "关闭软件", "关闭游戏和软件", "关机"];

    [ObservableProperty] private List<string> _sundayEverySelectedValueList = ["","1", "2", "3"];
    
    [ObservableProperty] private List<string> _sundaySelectedValueList = ["","1", "2", "3"];

    [ObservableProperty] private List<string> _secretTreasureObjectList = ["布匹","须臾树脂","大英雄的经验","流浪者的经验","精锻用魔矿","摩拉","祝圣精华","祝圣油膏"];
    
    [ObservableProperty] private List<string> _sereniteaPotTpTypes = ["地图传送", "尘歌壶道具"];

    [ObservableProperty] private AutoFightViewModel? _autoFightViewModel;
    
    public AllConfig Config { get; set; }

    /// <summary>
    /// 创建一条龙页面 ViewModel
    /// </summary>
    /// <param name="config">配置根：由 DI 容器注入单例</param>
    /// <param name="instanceService">多实例连接与独立任务操作服务</param>
    /// <param name="soloTaskRegistry">独立任务注册表</param>
    /// <param name="configService">配置服务，启动前把配置落盘供目标实例读取</param>
    public OneDragonFlowViewModel(AllConfig config, InstanceService instanceService,
        SoloTaskRegistry soloTaskRegistry, IConfigService configService)
    {
        Config = config;
        AutoFightViewModel = new AutoFightViewModel(Config);

        // 启动按钮复用独立任务的按钮行为：单实例直接启停，多实例打开实例管理窗口
        _soloTaskButtonController = new SoloTaskButtonController(instanceService, soloTaskRegistry, configService,
            [OneDragonSoloTaskState]);
        _soloTaskButtonController.Refreshed += OnSoloTaskButtonRefreshed;
        OneDragonFlowConfigStore.AutoBossCompletedRunCountSaved += OnAutoBossCompletedRunCountSaved;

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
                if (config.Name == Config.SelectedOneDragonFlowConfigName)
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
        foreach (var taskItem in OneDragonTaskItem.CreateListFromConfig(SelectedConfig))
        {
            TaskList.Add(taskItem);
        }
    }

    [RelayCommand]
    private void DeleteConfigDisplayTaskListFromConfig()
    {
        if (SelectedConfig == null || SelectedTask == null)
        {
            Toast.Warning("请先选择配置组和任务");
            return;
        }

        var itemToDelete = TaskList.FirstOrDefault(t => t.Id == SelectedTask.Id);
        if (itemToDelete != null)
        {
            TaskList.Remove(itemToDelete);
            Toast.Information("已经删除");
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

        SelectedConfig.TaskDefinitions.Clear();
        SelectedConfig.TaskEnabledList.Clear();
        SelectedConfig.TaskOrder.Clear();
        foreach (var task in TaskList)
        {
            SelectedConfig.TaskDefinitions[task.Id] = task.Name;
            SelectedConfig.TaskEnabledList[task.Id] = task.IsEnabled;
            SelectedConfig.TaskOrder.Add(task.Id);
        }

        WriteConfig(SelectedConfig);
    }
    
    [RelayCommand]
    private void AddTaskGroup()
    {
        // 触发弹窗显示的事件，让View层处理
        // 我们可以通过一个属性来通知View显示弹窗
        ShouldShowAddTaskGroupPopup = true;
    }
    
    [ObservableProperty]
    private bool _shouldShowAddTaskGroupPopup = false;

    [RelayCommand]
    private void SaveActionConfig()
    {
        SaveConfig();
        Toast.Information("排序已保存");
    }

    [RelayCommand]
    private void OnStrategyDropDownOpened(string type)
    {
        AutoFightViewModel?.OnStrategyDropDownOpened(type);
    }

    [RelayCommand]
    private void ResetAutoBossCompletedRunCount()
    {
        if (SelectedConfig == null)
        {
            return;
        }

        SelectedConfig.AutoBossCompletedRunCount = 0;
        Toast.Information("首领讨伐累计成功领奖次数已清空");
    }

    public void SetSomeSelectedConfig(OneDragonFlowConfig? selected)
    {
        if (SelectedConfig != null)
        {
            Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
            foreach (var task in TaskList)
            {
                if (SelectedConfig.TaskEnabledList.TryGetValue(task.Id, out var value))
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
        if (sender is not OneDragonFlowConfig config)
        {
            return;
        }

        // 首领讨伐的累计次数回调来自任务线程，保存配置需要访问 WPF 任务列表，统一调度回 UI 线程。
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ConfigPropertyChanged(sender, e));
            return;
        }

        if (ReferenceEquals(config, SelectedConfig))
        {
            SaveConfig();
            return;
        }

        WriteConfig(config);
    }

    public void WriteConfig(OneDragonFlowConfig? config)
    {
        if (config == null)
        {
            return;
        }

        try
        {
            // 原子写入：其他实例中运行的一条龙可能正在读取这个文件
            OneDragonFlowConfigStore.Write(config);
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
        var cmdOptions = CommandLineOptions.Instance;
        if (cmdOptions.Action == CommandLineAction.StartOneDragon)
        {
            // 通过命令行参数启动一条龙。
            if (cmdOptions.OneDragonConfigName != null)
            {
                // 从命令行参数中提取一条龙配置名称。
                _logger.LogInformation($"参数指定的一条龙配置：{cmdOptions.OneDragonConfigName}");
                var argsOneDragonConfig = ConfigList.FirstOrDefault(x =>
                    string.Equals(x.Name, cmdOptions.OneDragonConfigName, StringComparison.Ordinal));
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
            // 异步执行一条龙：命令行只针对当前实例，不打开实例管理窗口
            Toast.Information($"命令行一条龙「{SelectedConfig?.Name}」。");
            _ = StartOnCurrentInstanceAsync();
        }
    }

    /// <summary>
    /// 一条龙启动按钮与快捷键的统一入口：单实例时直接启动或停止，多实例时打开实例管理窗口
    /// </summary>
    /// <param name="taskId">任务标识，固定为一条龙</param>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task ManageSoloTask(string taskId)
    {
        return _soloTaskButtonController.ManageAsync(taskId, BuildOneDragonDefinitionAsync);
    }

    /// <summary>
    /// 按当前选中的一条龙配置生成任务参数。生成前把界面上的修改写入配置文件，并取走"从此执行"标记（只生效一次）
    /// </summary>
    /// <returns>任务参数；没有配置或没有启用的任务时提示并返回 null</returns>
    public OneDragonParam? CreateOneDragonParam()
    {
        // 快捷键可能在一条龙页面从未打开过时触发，此时先加载配置列表
        if (SelectedConfig == null)
        {
            InitConfigList();
        }

        if (SelectedConfig == null || !TaskList.Any(t => t.IsEnabled))
        {
            Toast.Warning("请先选择任务");
            return null;
        }

        // 目标实例从文件读取一条龙配置
        SaveConfig();

        var startTaskId = SelectedConfig.NextTaskId;
        if (!string.IsNullOrEmpty(startTaskId))
        {
            SelectedConfig.NextTaskId = string.Empty;
            LoadDisplayTaskListFromConfig();
            SaveConfig();
        }

        return new OneDragonParam
        {
            ConfigName = SelectedConfig.Name,
            StartTaskId = string.IsNullOrEmpty(startTaskId) ? null : startTaskId
        };
    }

    /// <summary>
    /// 按当前选中的一条龙配置生成启动定义
    /// </summary>
    /// <returns>启动定义；校验失败时返回 null</returns>
    private Task<SoloTaskStartDefinition?> BuildOneDragonDefinitionAsync()
    {
        var param = CreateOneDragonParam();
        return Task.FromResult(param is null
            ? null
            : _soloTaskButtonController.CreateDefinition(OneDragonTask.TaskId, param));
    }

    /// <summary>
    /// 在当前实例启动一条龙（命令行入口）
    /// </summary>
    private async Task StartOnCurrentInstanceAsync()
    {
        try
        {
            var definition = await BuildOneDragonDefinitionAsync();
            if (definition is null)
            {
                return;
            }

            var response = await _soloTaskButtonController.StartOnCurrentInstanceAsync(definition);
            if (!response.Accepted)
            {
                Toast.Warning(response.ErrorMessage ?? "当前实例拒绝启动一条龙。");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "命令行启动一条龙失败");
            Toast.Error($"命令行启动一条龙失败：{e.Message}");
        }
    }

    /// <summary>
    /// 一条龙在任一实例运行结束后，从文件重新读取首领讨伐累计次数：子实例中的累计结果不会自动同步到本进程
    /// </summary>
    private void OnSoloTaskButtonRefreshed(object? sender, EventArgs e)
    {
        var isRunning = OneDragonSoloTaskState.ActiveRunCount > 0;
        if (_wasOneDragonRunning && !isRunning)
        {
            ReloadAutoBossCompletedRunCounts();
        }

        _wasOneDragonRunning = isRunning;
    }

    /// <summary>
    /// 用文件中的首领讨伐累计次数更新已加载的配置
    /// </summary>
    private void ReloadAutoBossCompletedRunCounts()
    {
        foreach (var config in ConfigList)
        {
            if (OneDragonFlowConfigStore.TryReadAutoBossCompletedRunCount(config.Name, out var count)
                && config.AutoBossCompletedRunCount != count)
            {
                config.AutoBossCompletedRunCount = count;
            }
        }
    }

    /// <summary>
    /// 本进程中的一条龙写入首领讨伐累计次数后，同步到界面上的配置对象
    /// </summary>
    /// <param name="configName">配置名</param>
    /// <param name="count">新的累计次数</param>
    private void OnAutoBossCompletedRunCountSaved(string configName, int count)
    {
        UIDispatcherHelper.BeginInvoke(() =>
        {
            var config = ConfigList.FirstOrDefault(x => x.Name == configName);
            if (config != null && config.AutoBossCompletedRunCount != count)
            {
                config.AutoBossCompletedRunCount = count;
            }
        });
    }

    /// <summary>
    /// 生成与 TaskList 中现有 ID 不重复的唯一 ID。
    /// </summary>
    private string GenerateUniqueTaskId()
    {
        var existingIds = new HashSet<string>(TaskList.Select(t => t.Id));
        string newId;
        do
        {
            newId = Guid.NewGuid().ToString();
        } while (existingIds.Contains(newId));
        return newId;
    }

    [RelayCommand]
    private void CopyTask(OneDragonTaskItem? taskItem)
    {
        if (taskItem == null) return;

        var copy = new OneDragonTaskItem(taskItem.Name) { IsEnabled = taskItem.IsEnabled };
        copy.Id = GenerateUniqueTaskId();

        var index = TaskList.IndexOf(taskItem);
        if (index >= 0)
        {
            TaskList.Insert(index + 1, copy);
        }
        else
        {
            TaskList.Add(copy);
        }

        SaveConfig();
        Toast.Success($"已复制任务: {taskItem.Name}");
    }

    [RelayCommand]
    private void DeleteTask(OneDragonTaskItem? taskItem)
    {
        if (taskItem == null) return;

        TaskList.Remove(taskItem);
        SaveConfig();
        Toast.Success($"已删除任务: {taskItem.Name}");
    }

    [RelayCommand]
    private void SetTaskAsNext(OneDragonTaskItem? taskItem)
    {
        if (taskItem == null) return;
        if (SelectedConfig == null)
        {
            Toast.Warning("请先选择一条龙配置单");
            return;
        }
        if (!taskItem.IsEnabled)
        {
            Toast.Warning($"当前任务 <{taskItem.Name}> 已禁用，请先启用后再从此开始执行");
            return;
        }

        SelectedConfig.NextTaskId = taskItem.Id;
        foreach (var task in TaskList)
        {
            task.IsNextTask = task.Id == taskItem.Id;
        }
        Toast.Success($"设置从 <{taskItem.Name}> 开始执行任务列表");
        SaveConfig();
    }

    [RelayCommand]
    private void DeleteTaskGroup()
    {
        DeleteConfigDisplayTaskListFromConfig();
        SaveConfig();
        InputScriptGroupName = null;
    }

    [RelayCommand]
    private void NextTaskGroup()
    {
        if (SelectedConfig == null)
        {
            Toast.Warning("请先选择一条龙配置单");
            return;
        }

        var currentTask = SelectedTask;
        if (currentTask == null)
        {
            Toast.Warning("请先选择要从此开始执行的任务");
            return;
        }
        if (!currentTask.IsEnabled)
        {
            Toast.Warning($"当前任务 <{currentTask.Name}> 已禁用，请先启用后再从此开始执行");
            return;
        }

        SelectedConfig.NextTaskId = currentTask.Id;
        foreach (var task in TaskList)
        {
            task.IsNextTask = task.Id == currentTask.Id;
        }
        Toast.Success($"设置从 <{currentTask.Name}> 开始执行任务列表");
        SaveConfig();
    }

    [RelayCommand]
    private void ClearNextTaskGroup()
    {
        if (SelectedConfig == null)
        {
            Toast.Warning("请先选择一条龙配置单");
            return;
        }

        SelectedConfig.NextTaskId = string.Empty;
        foreach (var task in TaskList)
        {
            task.IsNextTask = false;
        }
        Toast.Success("清除从此执行标记完成");
        SaveConfig();
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

    [RelayCommand]
    private async Task DeleteConfig()
    {
        if (SelectedConfig == null)
        {
            Toast.Warning("请先选择要删除的配置");
            return;
        }

        var displayName = SelectedConfig.Name.Length > 14 
            ? $"{SelectedConfig.Name[..4]}...{SelectedConfig.Name[^4..]}" 
            : SelectedConfig.Name;
        var result = await ThemedMessageBox.ShowAsync(
            $"确定要删除配置「{displayName}」吗？", 
            "删除配置", 
            System.Windows.MessageBoxButton.YesNo, 
            ThemedMessageBox.MessageBoxIcon.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            // 删除对应的JSON文件
            var configFile = Path.Combine(OneDragonFlowConfigFolder, $"{SelectedConfig.Name}.json");
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }

            // 从列表中移除
            ConfigList.Remove(SelectedConfig);

            // 如果列表为空，创建默认配置
            if (ConfigList.Count == 0)
            {
                var defaultConfig = new OneDragonFlowConfig
                {
                    Name = "默认配置"
                };
                ConfigList.Add(defaultConfig);
                SelectedConfig = defaultConfig;
                WriteConfig(defaultConfig);
            }
            else
            {
                // 如果还有其他配置，选中第一个
                SelectedConfig = ConfigList[0];
            }

            // 更新全局配置名称
            Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
            
            // 刷新任务列表
            LoadDisplayTaskListFromConfig();
            SelectedTask = null!;
            InputScriptGroupName = string.Empty;
            
            // 保存配置
            SaveConfig();

            Toast.Success("配置删除成功");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "删除配置时失败");
            Toast.Error("删除配置时失败");
        }
    }

    [RelayCommand]
    private void RenameConfig()
    {
        if (SelectedConfig == null)
        {
            Toast.Warning("请先选择要重命名的配置");
            return;
        }

        var newName = PromptDialog.Prompt("请输入新的配置名称", "重命名配置", SelectedConfig.Name);
        if (string.IsNullOrEmpty(newName))
        {
            return;
        }

        if (newName == SelectedConfig.Name)
        {
            return;
        }

        if (ConfigList.Any(x => x.Name == newName))
        {
            Toast.Warning($"配置名称「{newName}」已存在，请使用其他名称");
            return;
        }

        try
        {
            // 保存旧名称
            var oldName = SelectedConfig.Name;
            
            // 更新配置名称
            SelectedConfig.Name = newName;

            // 先写入新文件
            WriteConfig(SelectedConfig);

            // 写入成功后再删除旧文件
            var oldConfigFile = Path.Combine(OneDragonFlowConfigFolder, $"{oldName}.json");
            if (File.Exists(oldConfigFile))
            {
                File.Delete(oldConfigFile);
            }

            // 更新全局配置名称
            Config.SelectedOneDragonFlowConfigName = newName;

            Toast.Success("配置重命名成功");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "重命名配置时失败");
            Toast.Error("重命名配置时失败");
        }
    }
}
