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
using BetterGenshinImpact.Service;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using System.Collections.Specialized;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class OneDragonFlowViewModel : ViewModel
{
    private readonly ILogger<OneDragonFlowViewModel> _logger = App.GetLogger<OneDragonFlowViewModel>();

    public static readonly string OneDragonFlowConfigFolder = Global.Absolute(@"User\OneDragon");

    private readonly ScriptService _scriptService;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList = [];


    [ObservableProperty] private OneDragonTaskItem _selectedTask;

    partial void OnSelectedTaskChanged(OneDragonTaskItem value)
    {
        if (value != null)
        {
            InputScriptGroupName = value.Name;
        }
    }

    [ObservableProperty] private string _inputScriptGroupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _playTaskList = new ObservableCollection<OneDragonTaskItem>();

    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = new ObservableCollection<ScriptGroup>();

    [ObservableProperty] private ObservableCollection<ScriptGroup> _scriptGroupsdefault = [];

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
                    _logger.LogInformation(e, _localizationService.GetString("oneDragon.readScriptGroupFailed"));
                }
            }

            ScriptGroups = new ObservableCollection<ScriptGroup>(ScriptGroups.OrderBy(g => g.Index));
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, _localizationService.GetString("oneDragon.readScriptGroupFailed"));
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
                        var localizationService = App.GetService<ILocalizationService>();
                        Toast.Success(localizationService.GetString("toast.dragonTaskAddSuccess"));
                    }
                }
                else
                {
                    TaskList.Add(taskItem);
                    if (pickTaskCount == 1)
                    {
                        var localizationService = App.GetService<ILocalizationService>();
                        Toast.Success(localizationService.GetString("toast.configGroupAddSuccess"));
                    }
                }
            }
            else
            {
                if (pickTaskCount == 1)
                {
                    var localizationService = App.GetService<ILocalizationService>();
                    Toast.Warning(localizationService.GetString("toast.taskAlreadyExists"));
                }
            } 
        }
        if (pickTaskCount > 1)
        {
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Success(localizationService.GetString("toast.tasksAddSuccess", pickTaskCount));  
        }
    }

    public async Task<string?> OnStartMultiScriptGroupAsync()
    {
        var localizationService = App.GetService<ILocalizationService>();
        
        var stackPanel = new StackPanel();
        var checkBoxes = new Dictionary<ScriptGroup, CheckBox>();
        CheckBox selectedCheckBox = null; // Used to store the currently selected CheckBox
        foreach (var scriptGroup in ScriptGroups)
        {
            if (TaskList.Any(taskName => scriptGroup.Name == taskName.Name))
            {
                continue; // Skip if task already exists in the list
            }
            var checkBox = new CheckBox
            {
                Content = scriptGroup.Name,
                Tag = scriptGroup,
                IsChecked = false // Initial state is unchecked
            };
            checkBoxes[scriptGroup] = checkBox;
            stackPanel.Children.Add(checkBox);
        }
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
        Title = _localizationService.GetString("oneDragon.selectTasksToAdd"),
        Content = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        },
        CloseButtonText = localizationService.GetString("common.close"),
        PrimaryButtonText = localizationService.GetString("common.ok"),
        Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.Width , // Ensure window automatically adjusts to content size
        MaxHeight = 600,
        };
        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            List<string> selectedItems = new List<string>(); // Store all selected items
            foreach (var checkBox in checkBoxes.Values)
            {
                if (checkBox.IsChecked == true)
                {
                    // Ensure Tag is ScriptGroup type, then get its Name property
                    var scriptGroup = checkBox.Tag as ScriptGroup;
                    if (scriptGroup != null)
                    { 
                        selectedItems.Add(scriptGroup.Name);
                    }
                    else
                    {
                        Toast.Error(localizationService.GetString("toast.configGroupLoadFailed"));
                    }
                }
            }
            return string.Join(",", selectedItems); // Return all selected items
        }
        return null;
    }

    public async Task<string?> OnPotBuyItemAsync()
    {
        var localizationService = App.GetService<ILocalizationService>();
        
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var checkBoxes = new Dictionary<string, CheckBox>(); 
        CheckBox selectedCheckBox = null;
        
        if (SelectedConfig.SecretTreasureObjects == null || SelectedConfig.SecretTreasureObjects.Count == 0)
        {
            Toast.Warning(localizationService.GetString("toast.noSecretTreasureConfig"));
            SelectedConfig.SecretTreasureObjects.Add(_localizationService.GetString("oneDragon.weekdays.everyday"));
        }
        var infoTextBlock = new TextBlock
        {
            Text = _localizationService.GetString("oneDragon.notAffectMoney"),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };

        stackPanel.Children.Add(infoTextBlock);
        // Add day selection dropdown
        var dayComboBox = new ComboBox
        {
            ItemsSource = new List<string> { 
                _localizationService.GetString("oneDragon.weekdays.monday"),
                _localizationService.GetString("oneDragon.weekdays.tuesday"),
                _localizationService.GetString("oneDragon.weekdays.wednesday"),
                _localizationService.GetString("oneDragon.weekdays.thursday"),
                _localizationService.GetString("oneDragon.weekdays.friday"),
                _localizationService.GetString("oneDragon.weekdays.saturday"),
                _localizationService.GetString("oneDragon.weekdays.sunday"),
                _localizationService.GetString("oneDragon.weekdays.everyday")
            },
            SelectedItem = SelectedConfig.SecretTreasureObjects.First(),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        stackPanel.Children.Add(dayComboBox);
        
        foreach (var potBuyItem in SecretTreasureObjectList)
        {
            var checkBox = new CheckBox
            {
                Content = potBuyItem,
                Tag = potBuyItem,
                MinWidth = 180,
                IsChecked = SelectedConfig.SecretTreasureObjects.Contains(potBuyItem) 
            };
            checkBoxes[potBuyItem] = checkBox; 
            stackPanel.Children.Add(checkBox);
        }
        
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = _localizationService.GetString("oneDragon.selectSecretTreasure"),
            Content = new ScrollViewer
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = localizationService.GetString("common.close"),
            PrimaryButtonText = localizationService.GetString("common.ok"),
            Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Width, // Ensure window automatically adjusts to content size
            MinWidth = 200,
            MaxHeight = 500,
        };

        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            SelectedConfig.SecretTreasureObjects.Clear();
            SelectedConfig.SecretTreasureObjects.Add(dayComboBox.SelectedItem.ToString());
            List<string> selectedItems = new List<string>(); // Store all selected items
            foreach (var checkBox in checkBoxes.Values)
            {
                if (checkBox.IsChecked == true)
                {
                    var potBuyItem = checkBox.Tag as string;
                    if (potBuyItem != null)
                    {
                        selectedItems.Add(potBuyItem);
                        SelectedConfig.SecretTreasureObjects.Add(potBuyItem);
                    }
                    else
                    {
                        Toast.Error(localizationService.GetString("toast.loadFailed"));
                    }
                }
            }
            if (selectedItems.Count > 0)
            {
                return string.Join(",", selectedItems); // Return all selected items
            }
            else
            {
                Toast.Warning(localizationService.GetString("toast.selectTreasureWarning"));
            }
        }
        return null;
    }
    
    [ObservableProperty] private ObservableCollection<OneDragonFlowConfig> _configList = [];
    /// <summary>
    /// Currently effective configuration
    /// </summary>
    [ObservableProperty] private OneDragonFlowConfig? _selectedConfig;

    [ObservableProperty] private List<string> _craftingBenchCountry = [];

    [ObservableProperty] private List<string> _adventurersGuildCountry = [];

    [ObservableProperty] private List<string> _domainNameList = ["", ..MapLazyAssets.Instance.DomainNameList];

    [ObservableProperty] private List<string> _completionActionList = [];

    [ObservableProperty] private List<string> _sundayEverySelectedValueList = ["","1", "2", "3"];
    
    [ObservableProperty] private List<string> _sundaySelectedValueList = ["","1", "2", "3"];

    [ObservableProperty] private List<string> _secretTreasureObjectList = [];
    
    [ObservableProperty] private List<string> _sereniteaPotTpTypes = [];
    
    public AllConfig Config { get; set; } = TaskContext.Instance().Config;

    public OneDragonFlowViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        
        // Initialize localized lists
        CraftingBenchCountry = [
            _localizationService.GetString("oneDragon.countries.mondstadt"),
            _localizationService.GetString("oneDragon.countries.liyue"),
            _localizationService.GetString("oneDragon.countries.inazuma"),
            _localizationService.GetString("oneDragon.countries.sumeru")
        ];
        
        AdventurersGuildCountry = [
            _localizationService.GetString("oneDragon.countries.mondstadt"),
            _localizationService.GetString("oneDragon.countries.liyue"),
            _localizationService.GetString("oneDragon.countries.inazuma"),
            _localizationService.GetString("oneDragon.countries.sumeru")
        ];
        
        CompletionActionList = [
            _localizationService.GetString("oneDragon.completionActions.none"),
            _localizationService.GetString("oneDragon.closeGame"),
            _localizationService.GetString("oneDragon.closeGameAndApp"),
            _localizationService.GetString("oneDragon.shutdown")
        ];
        
        // Initialize default task list with localized names
        TaskList = [
            new OneDragonTaskItem(_localizationService.GetString("oneDragon.taskNames.collectMail")),
            new OneDragonTaskItem(_localizationService.GetString("oneDragon.taskNames.synthesizeResin")),
            new OneDragonTaskItem(_localizationService.GetString("oneDragon.taskNames.autoDomain")),
            new OneDragonTaskItem(_localizationService.GetString("oneDragon.taskNames.collectDailyRewards")),
            new OneDragonTaskItem(_localizationService.GetString("oneDragon.taskNames.collectExpeditionRewards"))
        ];
        
        // Initialize default script groups with localized names
        ScriptGroupsdefault = [
            new ScriptGroup { Name = _localizationService.GetString("oneDragon.taskNames.collectMail") },
            new ScriptGroup { Name = _localizationService.GetString("oneDragon.taskNames.synthesizeResin") },
            new ScriptGroup { Name = _localizationService.GetString("oneDragon.taskNames.autoDomain") },
            new ScriptGroup { Name = _localizationService.GetString("oneDragon.taskNames.collectDailyRewards") },
            new ScriptGroup { Name = _localizationService.GetString("oneDragon.taskNames.collectExpeditionRewards") }
        ];
        
        // Initialize secret treasure object list with localized names
        SecretTreasureObjectList = [
            _localizationService.GetString("oneDragon.secretTreasure.resin"),
            _localizationService.GetString("oneDragon.secretTreasure.synthesisResin"),
            _localizationService.GetString("oneDragon.secretTreasure.heroWit"),
            _localizationService.GetString("oneDragon.secretTreasure.adventurerExp"),
            _localizationService.GetString("oneDragon.secretTreasure.mysticOre"),
            _localizationService.GetString("oneDragon.secretTreasure.ore"),
            _localizationService.GetString("oneDragon.secretTreasure.sanctifyingEssence"),
            _localizationService.GetString("oneDragon.secretTreasure.sanctifyingOintment")
        ];
        
        // Initialize Serenitea Pot teleport types with localized names
        SereniteaPotTpTypes = [
            _localizationService.GetString("oneDragon.teleportTypes.mapTeleport"),
            _localizationService.GetString("oneDragon.teleportTypes.sereniteaPot")
        ];
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
        // Get files from folder and load json configurations, sorted by creation time
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
                    Name = _localizationService.GetString("oneDragon.defaultConfig")
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
        LoadDisplayTaskListFromConfig(); // Load DisplayTaskList from configuration file
        SetSomeSelectedConfig(SelectedConfig);
    }

    // Load DisplayTaskList from configuration file based on enabled tasks

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
            // _logger.LogInformation($"Loading task: {kvp.Key} {kvp.Value}");
        }
    }

    [RelayCommand]
    private void DeleteConfigDisplayTaskListFromConfig()
    {
        if (SelectedConfig == null || SelectedTask == null ||
            SelectedConfig.TaskEnabledList == null) //|| SelectedConfig.TaskEnabledList == null 
        {
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Warning(localizationService.GetString("toast.selectConfigAndTask"));
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
                var localizationService = App.GetService<ILocalizationService>();
                Toast.Information(localizationService.GetString("toast.deleted"));
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
    private async void AddPotBuyItem()
    {
        await OnPotBuyItemAsync();
        SaveConfig();
        SelectedTask = null;
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
        var localizationService = App.GetService<ILocalizationService>();
        Toast.Information(localizationService.GetString("toast.sortingSaved"));
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
        await Task.Delay(100); // Wait a moment before saving
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
            _logger.LogDebug(e, _localizationService.GetString("toast.saveConfigFailed"));
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Error(localizationService.GetString("toast.saveConfigFailed"));
        }
    }
    
    private bool _autoRun = true;
    
    [RelayCommand]
    private void OnLoaded()
    {
        // Load state check runs only once
        if (!_autoRun)
        {
            return;
        }
        _autoRun = false;
        //
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].Contains("startOneDragon"))
        {
            // Start One-Dragon via command line parameters
            if (args.Length > 2)
            {
                // Get One-Dragon configuration name from command line parameters
                _logger.LogInformation(_localizationService.GetString("oneDragon.startSpecifiedConfig"), args[2]);
                var argsOneDragonConfig = ConfigList.FirstOrDefault(x => x.Name == args[2], null);
                if (argsOneDragonConfig != null)
                {
                    // Set configuration and update selected config
                    SelectedConfig = argsOneDragonConfig;
                    // Update after config selection change
                    OnConfigDropDownChanged();
                }
                else
                {
                    _logger.LogWarning(_localizationService.GetString("oneDragon.configNotFound"));
                }
            }
            // Asynchronously execute One-Dragon
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Information(localizationService.GetString("toast.commandLineDragon", SelectedConfig.Name));
            OnOneKeyExecute();
        }
    }

    [RelayCommand]
    public async Task OnOneKeyExecute()
    {
        _logger.LogInformation(_localizationService.GetString("oneDragon.startDragonConfig"), SelectedConfig.Name);
        var taskListCopy = new List<OneDragonTaskItem>(TaskList); // Copy task list to avoid modification during execution
        foreach (var task in taskListCopy)
        {
            task.InitAction(SelectedConfig);
        }

        int finishOneTaskcount = 1;
        int finishTaskcount = 1;
        int enabledTaskCountall = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation(_localizationService.GetString("oneDragon.totalEnabledTasks"), enabledTaskCountall);

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
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Warning(localizationService.GetString("toast.selectTaskFirst"));
            _logger.LogInformation(_localizationService.GetString("oneDragon.noTasksExit"));
            return;
        }

        int enabledoneTaskCount = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation(_localizationService.GetString("oneDragon.dragonTaskCount"), enabledoneTaskCount);

        await ScriptService.StartGameTask();
        SaveConfig();
        int enabledTaskCount = SelectedConfig.TaskEnabledList.Count(t =>
            t.Value && ScriptGroupsdefault.All(defaultTask => defaultTask.Name != t.Key));
        _logger.LogInformation(_localizationService.GetString("oneDragon.configGroupTaskCount"), enabledTaskCount);

        if (enabledoneTaskCount <= 0)
        {
            _logger.LogInformation(_localizationService.GetString("oneDragon.noDragonTasks"));
        }

        Notify.Event(NotificationEvent.DragonStart).Success("notification.message.dragonStart");
        foreach (var task in taskListCopy)
        {
            if (task is { IsEnabled: true, Action: not null })
            {
                if (ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == task.Name))
                {
                    _logger.LogInformation(_localizationService.GetString("oneDragon.dragonTaskExecuting"), finishOneTaskcount++, enabledoneTaskCount);
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
                            _logger.LogInformation(_localizationService.GetString("oneDragon.noConfigGroupTasks"));
                            return;
                        }

                        Notify.Event(NotificationEvent.DragonStart).Success("notification.message.configGroupStart");

                        if (SelectedConfig.TaskEnabledList[task.Name])
                        {
                            _logger.LogInformation(_localizationService.GetString("oneDragon.configGroupExecuting"), finishTaskcount++, enabledTaskCount);
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
                        _logger.LogDebug(e, _localizationService.GetString("oneDragon.executeConfigGroupFailed"));
                        var localizationService = App.GetService<ILocalizationService>();
                        Toast.Error(localizationService.GetString("toast.executeConfigFailed"));
                    }
                }
                // Check if task has been cancelled and interrupt execution
                if (CancellationContext.Instance.Cts.IsCancellationRequested)
                {
                    _logger.LogInformation(_localizationService.GetString("oneDragon.taskCancelled"));
                    Notify.Event(NotificationEvent.DragonEnd).Success("notification.message.dragonEnd");
                    return; // Skip remaining tasks, no longer executing
                }
            }
        }

        // Check rewards and complete tasks
        await new TaskRunner().RunThreadAsync(async () =>
        {
            await new CheckRewardsTask().Start(CancellationContext.Instance.Cts.Token);
            await Task.Delay(500);
            Notify.Event(NotificationEvent.DragonEnd).Success("notification.message.dragonEnd");
            _logger.LogInformation(_localizationService.GetString("oneDragon.dragonCompleted"));

            // Execute completion action
            if (SelectedConfig != null && !string.IsNullOrEmpty(SelectedConfig.CompletionAction))
            {
                switch (SelectedConfig.CompletionAction)
                {
                    case var action when action == _localizationService.GetString("oneDragon.closeGame"):
                        SystemControl.CloseGame();
                        break;
                    case var action when action == _localizationService.GetString("oneDragon.closeGameAndApp"):
                        SystemControl.CloseGame();
                        Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
                        break;
                    case var action when action == _localizationService.GetString("oneDragon.shutdown"):
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
        // Add new configuration
        var str = PromptDialog.Prompt(_localizationService.GetString("oneDragon.enterNewConfigName"), _localizationService.GetString("oneDragon.addNewConfig"));
        if (!string.IsNullOrEmpty(str))
        {
            // Check if already exists
            if (ConfigList.Any(x => x.Name == str))
            {
                var localizationService = App.GetService<ILocalizationService>();
                Toast.Warning(localizationService.GetString("toast.configAlreadyExists", str));
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
