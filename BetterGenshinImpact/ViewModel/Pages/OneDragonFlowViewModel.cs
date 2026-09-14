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
using System.Windows.Threading;
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
            }
            else
            {
                if (!SelectedConfig.TaskDefinitions.TryGetValue(key, out var name))
                {
                    continue;
                }
                taskItem = new OneDragonTaskItem(name, key) { IsEnabled = enabled };
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

    /// <summary>true 时表示正在应用磁盘重读结果，忽略由绑定回写引起的本命令重入。</summary>
    private bool _isApplyingConfigReload;

    /// <summary>最近一次成功应用到当前所选预设的原始 JSON（按名称记忆），内容未变化时跳过重复替换。</summary>
    private (string Name, string Json)? _lastAppliedConfigReload;

    [RelayCommand]
    private void OnConfigDropDownChanged()
    {
        if (SelectedConfig == null || _isApplyingConfigReload)
        {
            return;
        }

        // 切换预设时从磁盘重读对应文件，让运行中外部编辑的预设文件生效，
        // 也避免后续自动保存把内存中的旧值覆盖回文件（#3235）。
        var configName = SelectedConfig.Name;

        // 只做一次读取：取到磁盘内容（含反序列化与 Name 校验），读取/解析失败则沿用内存实例。
        var diskConfig = ReadOneDragonConfigFromDisk(configName, out var json);

        // 内容与最近一次应用的一致（例如绑定异步回写再次触发的本命令）：无需重复替换。
        if (diskConfig != null && _lastAppliedConfigReload is { } lastApplied
            && lastApplied.Name == configName && lastApplied.Json == json)
        {
            SetSomeSelectedConfig(SelectedConfig);
            SelectedTask = null;
            return;
        }

        if (diskConfig != null)
        {
            _isApplyingConfigReload = true;
            try
            {
                var index = ConfigList.IndexOf(SelectedConfig);
                if (index >= 0)
                {
                    ConfigList[index] = diskConfig;
                }

                // SelectedConfig 是预设下拉框的 TwoWay 绑定源：赋入新实例后绑定引擎会异步回写
                // ComboBox.SelectedItem 并再次触发本命令。守卫打开期间同步泵完 DataBind 优先级的
                // 回写，把这次重入拦掉，避免实例被反复替换（死循环）。
                _lastAppliedConfigReload = (configName, json);
                SelectedConfig = diskConfig;
                Application.Current?.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            }
            finally
            {
                _isApplyingConfigReload = false;
            }
        }

        // 收尾以本次读取结果为准（读取失败则保持原实例），不依赖绑定回写回显来完成状态收敛。
        SetSomeSelectedConfig(diskConfig ?? SelectedConfig);
        SelectedTask = null;
    }

    /// <summary>
    /// 按配置名读取并反序列化 <see cref="OneDragonFlowConfigFolder" /> 下的文件为全新实例；
    /// 文件缺失、解析失败或名称与来源文件不一致时返回 null（沿用内存中的实例，活对象不会被部分修改）。
    /// 成功时通过 <paramref name="json" /> 返回本次实际应用的原始内容，供后续幂等比较使用（单次读取）。
    /// </summary>
    private OneDragonFlowConfig? ReadOneDragonConfigFromDisk(string configName, out string json)
    {
        json = string.Empty;
        try
        {
            var filePath = Path.Combine(OneDragonFlowConfigFolder, $"{configName}.json");
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("重读一条龙配置失败，文件不存在: {ConfigName}", configName);
                return null;
            }

            json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<OneDragonFlowConfig>(json);
            // 仅接受与来源文件名一致的非空 Name：否则后续保存会用对象里的 Name 生成路径，
            // 导致原预设文件未被更新或写入错误的 .json 文件。
            if (config == null || string.IsNullOrEmpty(config.Name) || config.Name != configName)
            {
                _logger.LogDebug("忽略重读的一条龙配置，名称与来源文件不一致: {ConfigName}", configName);
                return null;
            }

            // 防御外部文件把任务集合显式写成 null 的情况，避免后续 SaveConfig 对空集合调用 Clear() 崩溃。
            config.TaskEnabledList ??= [];
            config.TaskOrder ??= [];
            config.TaskDefinitions ??= [];

            return config;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "重读一条龙配置失败: {ConfigName}", configName);
            return null;
        }
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

    public void SetSomeSelectedConfig(OneDragonFlowConfig? selected)
    {
        if (selected == null)
        {
            return;
        }

        // 以入参为准收口：绑定的异步回写可能让 SelectedConfig 短暂为 null 或滞后（自审），
        // 先确保属性指向本次要应用的实例，后续刷新才具备确定性。
        if (!ReferenceEquals(SelectedConfig, selected))
        {
            SelectedConfig = selected;
        }

        TaskContext.Instance().Config.SelectedOneDragonFlowConfigName = selected.Name;
        foreach (var task in TaskList)
        {
            if (selected.TaskEnabledList.TryGetValue(task.Id, out var value))
            {
                task.IsEnabled = value;
            }
        }

        LoadDisplayTaskListFromConfig();
    }

    private async void TaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SelectedConfig == null)
        {
            return;
        }

        // 捕获本次变更所属的预设实例：防抖期间若用户已切换预设，
        // 不再用当前 TaskList/SelectedConfig 覆盖新预设（自审）。
        var config = SelectedConfig;
        await Task.Delay(100); //等会加载完再保存
        if (ReferenceEquals(SelectedConfig, config))
        {
            SaveConfig();
        }
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

        int finishOneTaskcount = 1;
        int finishTaskcount = 1;
        int enabledTaskCountall = taskListCopy.Count(t => t.IsEnabled);
        _logger.LogInformation($"启用任务总数量: {enabledTaskCountall}");
        
        ReadScriptGroup();
        foreach (var task in ScriptGroupsdefault)
        {
            ScriptGroups.Remove(task);
        }

        if (SelectedConfig == null || taskListCopy.Count(t => t.IsEnabled) == 0)
        {
            Toast.Warning("请先选择任务");
            _logger.LogInformation("没有配置,退出执行!");
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
}
