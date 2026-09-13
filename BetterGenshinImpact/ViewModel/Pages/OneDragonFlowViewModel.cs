using System;
using System.Collections.Generic;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
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

    public static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");

    private readonly ScriptService _scriptService;

    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        new("领取邮件"),
        new("合成树脂"),
        // new ("每日委托"),
        new("自动秘境"),
        new ("自动首领讨伐"),
        new ("自动幽境危战"),
        new ("自动地脉花"),
        new("领取每日奖励"),
        new ("领取尘歌壶奖励"),
        // new ("自动七圣召唤"),
    ];


    [ObservableProperty] private OneDragonTaskItem _selectedTask;

    [ObservableProperty] private OneDragonTaskItem? _conditionEditingTask;

    [ObservableProperty] private bool _shouldShowTaskConditionPopup = false;

    /// <summary>
    /// 条件弹窗当前正在编辑的原始任务引用（非副本）。
    /// 为 null 表示当前未处于条件编辑状态。
    /// </summary>
    private OneDragonTaskItem? _conditionEditingTarget;

    /// <summary>
    /// 服务器 4:00 日界刷新调度器：在到达下一个服务器游戏日时刷新 TaskList 中所有任务的 ShouldRunToday。
    /// </summary>
    private CancellationTokenSource? _dailyRefreshCts;

    private Task? _dailyRefreshTask;

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
            new() { Name = "自动首领讨伐" },
            new() { Name = "自动幽境危战" },
            new() { Name = "自动地脉花" },
            new() { Name = "领取每日奖励" },
            new() {Name = "领取尘歌壶奖励" },
        };

    private readonly string _scriptGroupPath = Global.Absolute(@"User\ScriptGroup");
    private readonly string _basePath = AppDomain.CurrentDomain.BaseDirectory;
    
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
    
    public AllConfig Config { get; set; } = TaskContext.Instance().Config;

    public OneDragonFlowViewModel()
    {
        AutoFightViewModel = new AutoFightViewModel(Config);

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

        // 配置切换/重载会重建 TaskList，条件弹窗的编辑目标已失效，直接丢弃避免误写入新配置
        if (ShouldShowTaskConditionPopup)
        {
            CancelTaskCondition();
        }

        TaskList.Clear();

        // 旧格式兼容：TaskDefinitions 为空时，TaskEnabledList 键为任务名
        bool isOldFormat = SelectedConfig.TaskDefinitions == null || SelectedConfig.TaskDefinitions.Count == 0;

        // 使用 TaskOrder 恢复顺序；若无则回退到 TaskEnabledList 的键顺序
        var orderedKeys = SelectedConfig.TaskOrder?.Count > 0
            ? SelectedConfig.TaskOrder
            : SelectedConfig.TaskEnabledList.Keys.ToList();

        foreach (var key in orderedKeys)
        {
            if (!SelectedConfig.TaskEnabledList.TryGetValue(key, out var enabled))
            {
                continue;
            }

            OneDragonTaskItem taskItem;
            if (isOldFormat)
            {
                taskItem = new OneDragonTaskItem(key) { IsEnabled = enabled };
                taskItem.ApplyCondition(SelectedConfig.GetTaskCondition(key));
            }
            else
            {
                if (!SelectedConfig.TaskDefinitions.TryGetValue(key, out var name))
                {
                    continue;
                }
                taskItem = new OneDragonTaskItem(name, key) { IsEnabled = enabled };
                taskItem.ApplyCondition(SelectedConfig.GetTaskCondition(key));
            }
            taskItem.IsNextTask = key == SelectedConfig.NextTaskId;
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

        if (SelectedConfig.TaskDefinitions == null)
        {
            SelectedConfig.TaskDefinitions = new Dictionary<string, string>();
        }
        SelectedConfig.TaskDefinitions.Clear();
        if (SelectedConfig.TaskEnabledList == null)
        {
            SelectedConfig.TaskEnabledList = new Dictionary<string, bool>();
        }
        SelectedConfig.TaskEnabledList.Clear();
        if (SelectedConfig.TaskOrder == null)
        {
            SelectedConfig.TaskOrder = new List<string>();
        }
        SelectedConfig.TaskOrder.Clear();
        if (SelectedConfig.TaskConditions == null)
        {
            SelectedConfig.TaskConditions = new Dictionary<string, OneDragonTaskCondition>();
        }
        SelectedConfig.TaskConditions.Clear();
        foreach (var task in TaskList)
        {
            SelectedConfig.TaskDefinitions[task.Id] = task.Name;
            SelectedConfig.TaskEnabledList[task.Id] = task.IsEnabled;
            SelectedConfig.TaskOrder.Add(task.Id);
            SelectedConfig.TaskConditions[task.Id] = task.ToCondition();
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

    public void SetSomeSelectedConfig(OneDragonFlowConfig? selected)
    {
        if (SelectedConfig != null)
        {
            TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
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
        // 1) 条件弹窗编辑的是独立副本，不加入 TaskList 订阅，此处按引用防御，避免误触发保存；
        // 2) HasCondition / ShouldRunToday 为派生属性（由星期字段或 4:00 日界刷新触发），
        //    其变化不携带新数据，无需触发持久化。
        if (ReferenceEquals(sender, ConditionEditingTask) ||
            e.PropertyName is nameof(OneDragonTaskItem.HasCondition)
                or nameof(OneDragonTaskItem.ShouldRunToday))
        {
            return;
        }

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
        StartDailyRefreshLoop();

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
            // 异步执行一条龙
            Toast.Information($"命令行一条龙「{SelectedConfig.Name}」。");
            OnOneKeyExecute();
        }
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        // 页面卸载时停止服务器日界刷新循环并释放 CancellationTokenSource，
        // 避免后台任务持有 ViewModel 引用造成泄漏或退出后仍运行。
        await StopDailyRefreshLoopAsync(true);
    }

    /// <summary>
    /// 启动服务器日界（4:00）刷新循环：每次计算距离下一个服务器 4:00 的剩余时间并等待，
    /// 唤醒后刷新所有任务的 ShouldRunToday / HasCondition 通知，然后为下一个日界重新调度。
    /// </summary>
    private void StartDailyRefreshLoop()
    {
        if (_dailyRefreshTask != null && !_dailyRefreshTask.IsCompleted)
        {
            return;
        }

        StopDailyRefreshLoopAsync(false).GetAwaiter().GetResult();
        _dailyRefreshCts = new CancellationTokenSource();
        var token = _dailyRefreshCts.Token;

        _dailyRefreshTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    DateTimeOffset now;
                    try
                    {
                        now = ServerTimeHelper.GetServerTimeNow();
                    }
                    catch
                    {
                        now = DateTimeOffset.Now;
                    }

                    // now.Date 为 Kind=Unspecified 的 DateTime，直接参与 DateTimeOffset 运算会被按本机时区解释；
                    // 服务器时区与本机不一致时 delay 会偏差两个时区的差值，必须用 now.Offset 显式重建。
                    var next4am = new DateTimeOffset(now.Date, now.Offset).AddHours(4);
                    if (now.TimeOfDay >= TimeSpan.FromHours(4))
                    {
                        next4am = next4am.AddDays(1);
                    }

                    var delay = next4am - now;
                    if (delay <= TimeSpan.Zero)
                    {
                        delay = TimeSpan.FromMinutes(1);
                    }

                    await Task.Delay(delay, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        foreach (var t in TaskList)
                        {
                            t.NotifyDateStateChanged();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // 预期取消路径
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "一条龙服务器日界刷新循环异常");
            }
        }, token);
    }

    /// <summary>
    /// 停止服务器日界刷新循环并释放 CancellationTokenSource 与后台任务引用。
    /// 从 View 卸载或应用退出时调用，避免后台线程挂起与 ViewModel 内存泄漏。
    /// </summary>
    public Task StopDailyRefreshLoopAsync(bool waitForCompletion = true)
    {
        var cts = _dailyRefreshCts;
        var task = _dailyRefreshTask;

        try
        {
            if (cts != null)
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
        }
        catch (AggregateException ex)
        {
            // Task.Delay 的 OperationCanceledException 在 Cancel 回调注册时已被抛出时聚合
            foreach (var inner in ex.InnerExceptions)
            {
                _logger.LogDebug(inner, "停止一条龙日界刷新循环时抛出已预期的取消异常");
            }
        }

        Task stopTask;
        if (task == null || task.IsCompleted)
        {
            stopTask = Task.CompletedTask;
        }
        else if (waitForCompletion)
        {
            // 最多等待 1.5 秒。Token 已经 Cancel，正常情况 Delay 会马上抛 OCE，应当很快完成。
            stopTask = Task.WhenAny(task, Task.Delay(1500, CancellationToken.None)).ContinueWith(_ => { }, CancellationToken.None);
        }
        else
        {
            stopTask = Task.CompletedTask;
        }

        // null 化字段，确保下一次 StartDailyRefreshLoop 能重新调度，同时让 GC 能回收旧任务。
        if (ReferenceEquals(_dailyRefreshCts, cts))
        {
            _dailyRefreshCts = null;
        }
        if (ReferenceEquals(_dailyRefreshTask, task))
        {
            _dailyRefreshTask = null;
        }
        cts?.Dispose();
        return stopTask;
    }

    [RelayCommand]
    public async Task OnOneKeyExecute()
    {
        _logger.LogInformation($"启用一条龙配置：{SelectedConfig.Name}");

        // 启动等待之前先进行取消操作的初始化，便于在任务开始前终止任务.
        CancellationContext.Instance.Set();

        var taskListCopy = new List<OneDragonTaskItem>(TaskList);//避免执行过程中修改TaskList

        // 如果设置了 NextTaskId，从指定任务开始执行
        if (!string.IsNullOrEmpty(SelectedConfig.NextTaskId))
        {
            var taskIndex = taskListCopy.FindIndex(t => t.Id == SelectedConfig.NextTaskId);
            if (taskIndex >= 0)
            {
                _logger.LogInformation("一条龙：任务将从 {Name} 开始执行", taskListCopy[taskIndex].Name);
                taskListCopy = taskListCopy.Skip(taskIndex).ToList();
            }
            else
            {
                _logger.LogWarning("一条龙：未找到标记的任务，将从头开始执行");
            }
            SelectedConfig.NextTaskId = string.Empty;
            LoadDisplayTaskListFromConfig();
        }

        foreach (var task in taskListCopy)
        {
            task.InitAction(SelectedConfig);
        }

        int enabledTaskCountall = taskListCopy.Count(t => t.IsEnabled);
        int runnableTodayCount = taskListCopy.Count(t => t.IsEnabled && t.ShouldRunToday);
        int finishOneTaskcount = 1;
        int finishTaskcount = 1;
        _logger.LogInformation($"启用任务总数量: {enabledTaskCountall}，今日可执行数量: {runnableTodayCount}");
        
        ReadScriptGroup();
        foreach (var task in ScriptGroupsdefault)
        {
            ScriptGroups.Remove(task);
        }

        if (SelectedConfig == null || enabledTaskCountall == 0)
        {
            Toast.Warning("请先选择任务");
            _logger.LogInformation("没有配置,退出执行!");
            return;
        }

        if (runnableTodayCount == 0)
        {
            Toast.Information("今日无符合执行条件的任务，已跳过启动一条龙");
            _logger.LogInformation("今日所有启用任务均不满足执行日期条件，跳过启动一条龙");
            return;
        }

        int enabledoneTaskCount = taskListCopy.Count(t => t.IsEnabled);
        _logger.LogInformation($"启用一条龙任务的数量: {enabledoneTaskCount}");

        await ScriptService.StartGameTask();
        if (CancellationContext.Instance.IsCancellationRequested)
        {
            _logger.LogInformation("一条龙在启动阶段被取消");
            return;
        }

        SaveConfig();
        int enabledTaskCount = taskListCopy.Count(t =>
            t.IsEnabled && !ScriptGroupsdefault.Any(d => d.Name == t.Name));
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
                if (!task.ShouldRunToday)
                {
                    _logger.LogInformation($"任务 {task.Name} 不满足执行条件（运行日：{task.GetConditionSummaryText()}），跳过");
                    continue;
                }

                if (ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == task.Name))
                {
                    _logger.LogInformation($"一条龙任务执行: {finishOneTaskcount++}/{runnableTodayCount}");
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

                        if (SelectedConfig.TaskEnabledList[task.Id])
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
                    if (CancellationContext.Instance.IsManualStop is false)
                    {
                        Notify.Event(NotificationEvent.DragonEnd).Success("一条龙和配置组任务结束");
                    }
                    return; // 后续的检查任务也不执行
                }
            }
        }

        // 检查和最终结束的任务
        await new TaskRunner().RunThreadAsync(async () =>
        {
            await new CheckRewardsTask().Start(CancellationContext.Instance.Cts.Token);
            await Task.Delay(500);
            if (CancellationContext.Instance.IsManualStop is false)
            {
                Notify.Event(NotificationEvent.DragonEnd).Success("一条龙和配置组任务结束");
            }
            _logger.LogInformation("一条龙和配置组任务结束");

            // 执行完成后操作
            if (SelectedConfig != null && !string.IsNullOrEmpty(SelectedConfig.CompletionAction))
            {
                switch (SelectedConfig.CompletionAction)
                {
                    case "关闭游戏":
                        SystemControl.CloseGame();
                        break;
                    case "关闭软件":
                        Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
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
        copy.ApplyCondition(taskItem.ToCondition());

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
            TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = SelectedConfig.Name;
            
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
            TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = newName;

            Toast.Success("配置重命名成功");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "重命名配置时失败");
            Toast.Error("重命名配置时失败");
        }
    }

    [RelayCommand]
    private void OpenTaskCondition(OneDragonTaskItem? task)
    {
        task ??= SelectedTask;
        if (task == null)
        {
            Toast.Warning("请先选择一个任务");
            return;
        }
        // 使用独立副本进行弹窗编辑：副本不加入 TaskList 订阅，不触发自动保存，
        // 用户取消即丢弃副本，确认时才把条件写回原任务。
        ConditionEditingTask = task.CreateConditionEditingClone();
        _conditionEditingTarget = task;
        ShouldShowTaskConditionPopup = true;
        if (task != SelectedTask)
        {
            SelectedTask = task;
        }
    }

    [RelayCommand]
    private void CloseTaskCondition()
    {
        try
        {
            if (_conditionEditingTarget != null && ConditionEditingTask != null)
            {
                // 引用校验：目标任务必须仍在当前 TaskList 中（配置未被切换）。
                // 旧格式配置的任务 Id 为任务名，切换配置后按 Id 查找可能命中其它配置的同名任务。
                if (TaskList.Contains(_conditionEditingTarget))
                {
                    _conditionEditingTarget.ApplyCondition(ConditionEditingTask.ToCondition());
                    SaveConfig();
                }
                else
                {
                    Toast.Warning("配置已切换，本次条件修改已丢弃");
                }
            }
        }
        finally
        {
            _conditionEditingTarget = null;
            ConditionEditingTask = null;
            ShouldShowTaskConditionPopup = false;
        }
    }

    [RelayCommand]
    private void ResetTaskCondition()
    {
        ConditionEditingTask?.ApplyCondition(null);
    }

    [RelayCommand]
    private void CancelTaskCondition()
    {
        // 取消编辑：副本改动直接丢弃。
        _conditionEditingTarget = null;
        ConditionEditingTask = null;
        ShouldShowTaskConditionPopup = false;
    }
}
