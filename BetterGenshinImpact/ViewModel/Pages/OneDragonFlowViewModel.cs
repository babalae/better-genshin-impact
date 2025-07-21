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

    [ObservableProperty] private ObservableCollection<OneDragonTaskItem> _taskList =
    [
        new("��ȡ�ʼ�"),
        new("�ϳ���֬"),
        // new ("ÿ��ί��"),
        new("�Զ��ؾ�"),
        // new ("�Զ�����"),
        // new ("�Զ�ˢ������"),
        new("��ȡÿ�ս���"),
        new ("��ȡ���������"),
        // new ("�Զ���ʥ�ٻ�"),
    ];


    [ObservableProperty] private OneDragonTaskItem _selectedTask;

    partial void OnSelectedTaskChanged(OneDragonTaskItem value)
    {
        if (value != null)
        {
            InputScriptGroupName = value.Name;
        }
    }

    // �������Ժͷ���...
    [ObservableProperty] private string _inputScriptGroupName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<OneDragonTaskItem> _playTaskList = new ObservableCollection<OneDragonTaskItem>();

    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = new ObservableCollection<ScriptGroup>();

    [ObservableProperty] private ObservableCollection<ScriptGroup> _scriptGroupsdefault =
        new ObservableCollection<ScriptGroup>()
        {
            new() { Name = "��ȡ�ʼ�" },
            new() { Name = "�ϳ���֬" },
            new() { Name = "�Զ��ؾ�" },
            new() { Name = "��ȡÿ�ս���" },
            new() {Name = "��ȡ���������" },
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
                    _logger.LogInformation(e, "��ȡ����������ʱʧ��");
                }
            }

            ScriptGroups = new ObservableCollection<ScriptGroup>(ScriptGroups.OrderBy(g => g.Index));
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "��ȡ����������ʱʧ��");
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
        CheckBox selectedCheckBox = null; // ���ڱ��浱ǰѡ�е� CheckBox
        foreach (var scriptGroup in ScriptGroups)
        {
            if (TaskList.Any(taskName => scriptGroup.Name == taskName.Name))
            {
                continue; // ֻ�е��ļ�����ȫ��ͬʱ��������ʾ
            }
            var checkBox = new CheckBox
            {
                Content = scriptGroup.Name,
                Tag = scriptGroup,
                IsChecked = false // ��ʼ״̬�¶�δѡ��
            };
            checkBoxes[scriptGroup] = checkBox;
            stackPanel.Children.Add(checkBox);
        }
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
        Title = "ѡ�����ӵ������飨�ɶ�ѡ��",
        Content = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        },
        CloseButtonText = localizationService.GetString("common.close"),
        PrimaryButtonText = localizationService.GetString("common.ok"),
        Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        SizeToContent = SizeToContent.Width , // ȷ���������������Զ�������С
        MaxHeight = 600,
        };
        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            List<string> selectedItems = new List<string>(); // ���ڴ洢����ѡ�е���
            foreach (var checkBox in checkBoxes.Values)
            {
                if (checkBox.IsChecked == true)
                {
                    // ȷ�� Tag �� ScriptGroup ���ͣ��������� Name ����
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
            return string.Join(",", selectedItems); // ��������ѡ�е���
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
            SelectedConfig.SecretTreasureObjects.Add("ÿ���ظ�");
        }
        var infoTextBlock = new TextBlock
        {
            Text = "���ڲ�Ӱ����ȡ�øк�Ǯ��",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };

        stackPanel.Children.Add(infoTextBlock);
        // �������ѡ���
        var dayComboBox = new ComboBox
        {
            ItemsSource = new List<string> { "����һ", "���ڶ�", "������", "������", "������", "������", "������", "ÿ���ظ�" },
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
            Title = "����ٱ�����ѡ��",
            Content = new ScrollViewer
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = localizationService.GetString("common.close"),
            PrimaryButtonText = localizationService.GetString("common.ok"),
            Owner = Application.Current.ShutdownMode == ShutdownMode.OnMainWindowClose ? null : Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Width, // ȷ���������������Զ�������С
            MinWidth = 200,
            MaxHeight = 500,
        };

        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            SelectedConfig.SecretTreasureObjects.Clear();
            SelectedConfig.SecretTreasureObjects.Add(dayComboBox.SelectedItem.ToString());
            List<string> selectedItems = new List<string>(); // ���ڴ洢����ѡ�е���
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
                return string.Join(",", selectedItems); // ��������ѡ�е���
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
    /// ��ǰ��Ч����
    /// </summary>
    [ObservableProperty] private OneDragonFlowConfig? _selectedConfig;

    [ObservableProperty] private List<string> _craftingBenchCountry = ["�㵤", "����", "����", "�ɵ�"];

    [ObservableProperty] private List<string> _adventurersGuildCountry = ["�㵤", "����", "����", "�ɵ�"];

    [ObservableProperty] private List<string> _domainNameList = ["", ..MapLazyAssets.Instance.DomainNameList];

    [ObservableProperty] private List<string> _completionActionList = ["��", "�ر���Ϸ", "�ر���Ϸ�����", "�ػ�"];

    [ObservableProperty] private List<string> _sundayEverySelectedValueList = ["","1", "2", "3"];
    
    [ObservableProperty] private List<string> _sundaySelectedValueList = ["","1", "2", "3"];

    [ObservableProperty] private List<string> _secretTreasureObjectList = ["��ƥ","������֬","��Ӣ�۵ľ���","�����ߵľ���","������ħ��","Ħ��","ףʥ����","ףʥ�͸�"];
    
    [ObservableProperty] private List<string> _sereniteaPotTpTypes = ["��ͼ����", "���������"];
    
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
        // ��ȡ�ļ���������json���ã�������ʱ������
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
                    Name = "Ĭ������"
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
        LoadDisplayTaskListFromConfig(); // ���� DisplayTaskList �������ļ�
        SetSomeSelectedConfig(SelectedConfig);
    }

    // �����������������ļ����� DisplayTaskList

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
            // _logger.LogInformation($"��������: {kvp.Key} {kvp.Value}");
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
        await Task.Delay(100); //�Ȼ�������ٱ���
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
            _logger.LogDebug(e, "��������ʱʧ��");
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Error(localizationService.GetString("toast.saveConfigFailed"));
        }
    }
    
    private bool _autoRun = true;
    
    [RelayCommand]
    private void OnLoaded()
    {
        // ����״μ���ʱ����һ�Ρ�
        if (!_autoRun)
        {
            return;
        }
        _autoRun = false;
        //
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].Contains("startOneDragon"))
        {
            // ͨ�������в�������һ������
            if (args.Length > 2)
            {
                // �������в�������ȡһ�����������ơ�
                _logger.LogInformation($"����ָ����һ�������ã�{args[2]}");
                var argsOneDragonConfig = ConfigList.FirstOrDefault(x => x.Name == args[2], null);
                if (argsOneDragonConfig != null)
                {
                    // �趨���ã������������ѡ����
                    SelectedConfig = argsOneDragonConfig;
                    // ����ѡ�����º�����
                    OnConfigDropDownChanged();
                }
                else
                {
                    _logger.LogWarning("δ�ҵ������顣");
                }
            }
            // �첽ִ��һ����
            var localizationService = App.GetService<ILocalizationService>();
            Toast.Information(localizationService.GetString("toast.commandLineDragon", SelectedConfig.Name));
            OnOneKeyExecute();
        }
    }

    [RelayCommand]
    public async Task OnOneKeyExecute()
    {
        _logger.LogInformation($"����һ�������ã�{SelectedConfig.Name}");
        var taskListCopy = new List<OneDragonTaskItem>(TaskList);//����ִ�й������޸�TaskList
        foreach (var task in taskListCopy)
        {
            task.InitAction(SelectedConfig);
        }

        int finishOneTaskcount = 1;
        int finishTaskcount = 1;
        int enabledTaskCountall = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation($"��������������: {enabledTaskCountall}");

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
            _logger.LogInformation("û������,�˳�ִ��!");
            return;
        }

        int enabledoneTaskCount = SelectedConfig.TaskEnabledList.Count(t => t.Value);
        _logger.LogInformation($"����һ�������������: {enabledoneTaskCount}");

        await ScriptService.StartGameTask();
        SaveConfig();
        int enabledTaskCount = SelectedConfig.TaskEnabledList.Count(t =>
            t.Value && ScriptGroupsdefault.All(defaultTask => defaultTask.Name != t.Key));
        _logger.LogInformation($"�������������������: {enabledTaskCount}");

        if (enabledoneTaskCount <= 0)
        {
            _logger.LogInformation("û��һ��������!");
        }

        Notify.Event(NotificationEvent.DragonStart).Success("notification.message.dragonStart");
        foreach (var task in taskListCopy)
        {
            if (task is { IsEnabled: true, Action: not null })
            {
                if (ScriptGroupsdefault.Any(defaultSg => defaultSg.Name == task.Name))
                {
                    _logger.LogInformation($"һ��������ִ��: {finishOneTaskcount++}/{enabledoneTaskCount}");
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
                            _logger.LogInformation("û������������,�˳�ִ��!");
                            return;
                        }

                        Notify.Event(NotificationEvent.DragonStart).Success("notification.message.configGroupStart");

                        if (SelectedConfig.TaskEnabledList[task.Name])
                        {
                            _logger.LogInformation($"����������ִ��: {finishTaskcount++}/{enabledTaskCount}");
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
                        _logger.LogDebug(e, "ִ������������ʱʧ��");
                        var localizationService = App.GetService<ILocalizationService>();
                        Toast.Error(localizationService.GetString("toast.executeConfigFailed"));
                    }
                }
                // ��������Ѿ���ȡ�����ж���������
                if (CancellationContext.Instance.Cts.IsCancellationRequested)
                {
                    _logger.LogInformation("����ȡ�����˳�ִ��");
                    Notify.Event(NotificationEvent.DragonEnd).Success("notification.message.dragonEnd");
                    return; // �����ļ������Ҳ��ִ��
                }
            }
        }

        // �������ս���������
        await new TaskRunner().RunThreadAsync(async () =>
        {
            await new CheckRewardsTask().Start(CancellationContext.Instance.Cts.Token);
            await Task.Delay(500);
            Notify.Event(NotificationEvent.DragonEnd).Success("notification.message.dragonEnd");
            _logger.LogInformation("һ�������������������");

            // ִ����ɺ����
            if (SelectedConfig != null && !string.IsNullOrEmpty(SelectedConfig.CompletionAction))
            {
                switch (SelectedConfig.CompletionAction)
                {
                    case "�ر���Ϸ":
                        SystemControl.CloseGame();
                        break;
                    case "�ر���Ϸ�����":
                        SystemControl.CloseGame();
                        Application.Current.Dispatcher.Invoke(() => { Application.Current.Shutdown(); });
                        break;
                    case "�ػ�":
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
        // �������
        var str = PromptDialog.Prompt("������һ������������", "����һ��������");
        if (!string.IsNullOrEmpty(str))
        {
            // ����Ƿ��Ѵ���
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
