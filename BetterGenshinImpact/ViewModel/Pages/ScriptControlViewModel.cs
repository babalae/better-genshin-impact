using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.LogParse;
using BetterGenshinImpact.GameTask.TaskProgress;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Editable;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows.Editable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using Button = Wpf.Ui.Controls.Button;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using StackPanel = Wpf.Ui.Controls.StackPanel;
using TextBlock = Wpf.Ui.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ViewModel
{
    private readonly ISnackbarService _snackbarService;

    private readonly ILogger<ScriptControlViewModel> _logger = App.GetLogger<ScriptControlViewModel>();

    private readonly IScriptService _scriptService;

    /// <summary>
    /// 配置组配置
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = [];

    /// <summary>
    /// 当前选中的配置组
    /// </summary>
    [ObservableProperty]
    private ScriptGroup? _selectedScriptGroup = null;

    public readonly string ScriptGroupPath = Global.Absolute(@"User\ScriptGroup");
    public readonly string LogPath = Global.Absolute(@"log");


    public override void OnNavigatedTo()
    {
        ReadScriptGroup();
    }

    public ScriptControlViewModel(ISnackbarService snackbarService, IScriptService scriptService)
    {
        _snackbarService = snackbarService;
        _scriptService = scriptService;
        ScriptGroups.CollectionChanged += ScriptGroupsCollectionChanged;
    }

    [RelayCommand]
    private void OnAddScriptGroup()
    {
        // 创建一个TextBox并设置自动聚焦
        var textBox = new System.Windows.Controls.TextBox()
        {
            VerticalAlignment = VerticalAlignment.Top
        };
        textBox.Loaded += (sender, e) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };
        var str = PromptDialog.Prompt("请输入配置组名称", "新增配置组", textBox);
        if (!string.IsNullOrEmpty(str))
        {
            // 检查是否已存在
            if (ScriptGroups.Any(x => x.Name == str))
            {
                _snackbarService.Show(
                    "配置组已存在",
                    $"配置组 {str} 已经存在，请勿重复添加",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(2)
                );
            }
            else
            {
                ScriptGroups.Add(new ScriptGroup { Name = str });
            }
        }
    }

    [RelayCommand]
    private void ClearTasks()
    {
        // 确认？
        var result = MessageBox.Show("是否清空所有任务？", "清空任务", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        if (SelectedScriptGroup == null)
        {
            return;
        }

        SelectedScriptGroup.Projects.Clear();
        WriteScriptGroup(SelectedScriptGroup);
    }

    [RelayCommand]
    private async Task OpenLogParse()
    {
        if (SelectedScriptGroup == null)
        {
            return;
        }

        GameInfo? gameInfo = null;
        var config = LogParse.LoadConfig();

        OtherConfig.Miyoushe mcfg = TaskContext.Instance().Config.OtherConfig.MiyousheConfig;
        if (mcfg.LogSyncCookie && !string.IsNullOrEmpty(mcfg.Cookie))
        {
            config.Cookie = mcfg.Cookie;
        }

        if (!string.IsNullOrEmpty(config.Cookie))
        {
            config.CookieDictionary.TryGetValue(config.Cookie, out gameInfo);
        }



        LogParseConfig.ScriptGroupLogParseConfig? sgpc;
        if (!config.ScriptGroupLogDictionary.TryGetValue(SelectedScriptGroup.Name, out sgpc))
        {
            sgpc = new LogParseConfig.ScriptGroupLogParseConfig();
        }


        // 创建 StackPanel
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10)
        };

        // 创建 ComboBox
        var rangeComboBox = new ComboBox
        {
            Width = 200,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        var rangeComboBoxItems = new List<object>
        {
            new { Text = "当前配置组", Value = "CurrentConfig" },
            new { Text = "所有", Value = "All" }
        };
        rangeComboBox.DisplayMemberPath = "Text"; // 显示的文本
        rangeComboBox.SelectedValuePath = "Value"; // 绑定的值
        rangeComboBox.ItemsSource = rangeComboBoxItems;
        rangeComboBox.SelectedIndex = 0; // 默认选中第一个项
        stackPanel.Children.Add(rangeComboBox);


        var dayRangeComboBox = new ComboBox
        {
            Width = 200,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        // 定义范围选项数据
        var dayRangeComboBoxItems = new List<object>
        {
            new { Text = "1天" , Value = "1" },
            new { Text = "3天", Value = "3" },
            new { Text = "7天", Value = "7" },
            new { Text = "15天", Value = "15" },
            new { Text = "31天", Value = "31" },
            new { Text = "61天", Value = "61" },
            new { Text = "92天", Value = "92" },
            new { Text = "所有", Value = "All" }
        };
        dayRangeComboBox.ItemsSource = dayRangeComboBoxItems;
        dayRangeComboBox.DisplayMemberPath = "Text"; // 显示的文本
        dayRangeComboBox.SelectedValuePath = "Value"; // 绑定的值
        dayRangeComboBox.SelectedIndex = 0;
        stackPanel.Children.Add(dayRangeComboBox);

        CheckBox mergerStatsSwitch = new CheckBox
        {
            Content = "合并相邻同名配置组",
            VerticalAlignment = VerticalAlignment.Center
        };
        stackPanel.Children.Add(mergerStatsSwitch);

        // 开关控件：ToggleButton 或 CheckBox
        CheckBox faultStatsSwitch = new CheckBox
        {
            Content = "异常情况统计",
            VerticalAlignment = VerticalAlignment.Center
        };
        stackPanel.Children.Add(faultStatsSwitch);

        // 开关控件：ToggleButton 或 CheckBox
        CheckBox hoeingStatsSwitch = new CheckBox
        {
            Content = "统计锄地摩拉怪物数",
            VerticalAlignment = VerticalAlignment.Center
        };

        CheckBox GenerateFarmingPlanData = new CheckBox
        {
            Content = "生成锄地规划数据",
            VerticalAlignment = VerticalAlignment.Center
        };
        stackPanel.Children.Add(GenerateFarmingPlanData);

        //firstRow.Children.Add(toggleSwitch);

        // 将第一行添加到 StackPanel
        stackPanel.Children.Add(hoeingStatsSwitch);

        // 第二行：文本框和“？”按钮
        StackPanel secondRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        // 文本框
        TextBox cookieTextBox = new TextBox
        {
            Width = 200,
            Margin = new Thickness(0, 0, 10, 0)
        };
        secondRow.Children.Add(cookieTextBox);

        // “？”按钮
        Button questionButton = new Button
        {
            Content = "?",
            Width = 30,
            Height = 30
        };

        secondRow.Children.Add(questionButton);

        StackPanel threeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        // 创建一个 TextBlock
        TextBlock hoeingDelayBlock = new TextBlock
        {
            Text = "锄地延时(秒)：",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16,
            Margin = new Thickness(0, 0, 10, 0)
        };


        TextBox hoeingDelayTextBox = new TextBox
        {
            Width = 100,
            FontSize = 16,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        threeRow.Children.Add(hoeingDelayBlock);
        threeRow.Children.Add(hoeingDelayTextBox);


        // 将第二行添加到 StackPanel
        stackPanel.Children.Add(secondRow);
        stackPanel.Children.Add(threeRow);
        //PrimaryButtonText
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "日志分析",
            Content = stackPanel,
            CloseButtonText = "取消",
            PrimaryButtonText = "确定",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        void OnQuestionButtonOnClick(object sender, RoutedEventArgs args)
        {
            WebpageWindow cookieWin = new()
            {
                Title = "日志分析",
                Width = 800,
                Height = 600,
                Owner = uiMessageBox,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            cookieWin.NavigateToHtml(TravelsDiaryDetailManager.generHtmlMessage());
            cookieWin.Show();
        }

        questionButton.Click += OnQuestionButtonOnClick;

        //对象赋值
        rangeComboBox.SelectedValue = sgpc.RangeValue;
        dayRangeComboBox.SelectedValue = sgpc.DayRangeValue;
        cookieTextBox.Text = config.Cookie;
        hoeingStatsSwitch.IsChecked = sgpc.HoeingStatsSwitch;
        GenerateFarmingPlanData.IsChecked = sgpc.GenerateFarmingPlanData;
        faultStatsSwitch.IsChecked = sgpc.FaultStatsSwitch;
        mergerStatsSwitch.IsChecked = sgpc.MergerStatsSwitch;

        hoeingDelayTextBox.Text = sgpc.HoeingDelay;

        MessageBoxResult result = await uiMessageBox.ShowDialogAsync();


        if (result == MessageBoxResult.Primary)
        {
            string rangeValue = ((dynamic)rangeComboBox.SelectedItem).Value;
            string dayRangeValue = ((dynamic)dayRangeComboBox.SelectedItem).Value;
            string cookieValue = cookieTextBox.Text;

            //保存配置文件
            sgpc.DayRangeValue = dayRangeValue;
            sgpc.RangeValue = rangeValue;
            sgpc.HoeingStatsSwitch = hoeingStatsSwitch.IsChecked ?? false;
            sgpc.GenerateFarmingPlanData = GenerateFarmingPlanData.IsChecked ?? false;
            sgpc.FaultStatsSwitch = faultStatsSwitch.IsChecked ?? false;
            sgpc.MergerStatsSwitch = mergerStatsSwitch.IsChecked ?? false;
            sgpc.HoeingDelay = hoeingDelayTextBox.Text;

            config.Cookie = cookieValue;
            config.ScriptGroupLogDictionary[SelectedScriptGroup.Name] = sgpc;

            if (mcfg.LogSyncCookie && !string.IsNullOrEmpty(cookieValue))
            {
                mcfg.Cookie = cookieValue;
            }

            LogParse.WriteConfigFile(config);


            WebpageWindow win = new()
            {
                Title = "日志分析",
                Width = 800,
                Height = 600,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            void OnHtmlGenerationStatusChanged(string status)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Toast.Information(status, time: 5000);
                });
            }

            LogParse.HtmlGenerationStatusChanged += OnHtmlGenerationStatusChanged;
            Toast.Information("正在准备数据...");
            List<(string FileName, string Date)> fs = LogParse.GetLogFiles(LogPath);
            if (dayRangeValue != "All")
            {
                int n = int.Parse(dayRangeValue);
                if (n < fs.Count)
                {
                    fs = fs.GetRange(fs.Count - n, n);
                }
            }


            //最终确定是否打开锄地开关
            bool hoeingStats = false;

            if ((hoeingStatsSwitch.IsChecked ?? false) && string.IsNullOrEmpty(cookieValue))
            {
                Toast.Warning("未填写cookie，此次将不启用锄地统计！");
            }

            //真正存储的gameinfo
            GameInfo? realGameInfo = gameInfo;
            //统计锄地开关打开，并且不为cookie不为空
            if ((hoeingStatsSwitch.IsChecked ?? false) && !string.IsNullOrEmpty(cookieValue))
            {
                try
                {
                    Toast.Information("正在从米游社获取旅行札记数据，请耐心等待！");
                    gameInfo = await TravelsDiaryDetailManager.UpdateTravelsDiaryDetailManager(cookieValue);
                    Toast.Success($"米游社数据获取成功，开始进行解析，请耐心等待！");
                }
                catch (Exception)
                {
                    if (realGameInfo != null)
                    {
                        Toast.Warning("访问米游社接口异常，此次将锄地统计将不更新最新数据！");
                    }
                    else
                    {
                        Toast.Warning("访问米游社接口异常，此次将不启用锄地统计！");
                    }
                }
            }

            if (gameInfo != null)
            {
                realGameInfo = gameInfo;

                config.CookieDictionary[cookieValue] = realGameInfo;
                LogParse.WriteConfigFile(config);
            }

            if ((hoeingStatsSwitch.IsChecked ?? false) && realGameInfo != null)
            {
                hoeingStats = true;
            }

            var configGroupEntities = LogParse.ParseFile(fs);
            if (rangeValue == "CurrentConfig")
            {
                //Toast.Success(_selectedScriptGroup.Name);
                configGroupEntities = configGroupEntities.Where(item => SelectedScriptGroup.Name == item.Name).ToList();
            }

            if (configGroupEntities.Count == 0)
            {
                Toast.Warning("未解析出日志记录！");
                LogParse.HtmlGenerationStatusChanged -= OnHtmlGenerationStatusChanged;
            }
            else
            {
                configGroupEntities.Reverse();
                try
                {
                    // 生成HTML并加载
                    win.NavigateToHtml(LogParse.GenerHtmlByConfigGroupEntity(configGroupEntities,
                    hoeingStats ? realGameInfo : null, sgpc));
                    win.ShowDialog();
                    // 取消订阅事件
                    LogParse.HtmlGenerationStatusChanged -= OnHtmlGenerationStatusChanged;

                }
                catch (Exception ex)
                {
                    LogParse.HtmlGenerationStatusChanged -= OnHtmlGenerationStatusChanged;
                    Toast.Error($"生成日志分析时出错: {ex.Message}");
                }
            }
        }
    }

    static string[] GetJsonFiles(string folderPath)
    {
        // 检查文件夹是否存在
        if (!Directory.Exists(folderPath))
        {
            return new string[0];
        }

        // 获取所有 .json 文件
        return Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        TaskContext.Instance().Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }

    [RelayCommand]
    private void UpdateTasks()
    {
        List<ScriptGroupProject> projects = new();
        List<ScriptGroupProject> oldProjects = new();
        oldProjects.AddRange(SelectedScriptGroup?.Projects ?? []);
        var oldcount = oldProjects.Count;
        List<string> folderNames = new();
        foreach (var project in oldProjects)
        {
            if (project.Type == "Pathing")
            {
                if (!folderNames.Contains(project.FolderName))
                {
                    folderNames.Add(project.FolderName);
                    //根据文件夹更新
                    var dirPath = $@"{MapPathingViewModel.PathJsonPath}\{project.FolderName}";
                    foreach (var jsonFile in GetJsonFiles(dirPath))
                    {
                        var fileInfo = new FileInfo(jsonFile);
                        var oldProject = oldProjects.FirstOrDefault(item => item.Name == fileInfo.Name);
                        if (oldProject == null)
                        {
                            projects.Add(ScriptGroupProject.BuildPathingProject(fileInfo.Name, project.FolderName));
                        }
                        else
                        {
                            projects.Add(oldProject);
                        }
                    }
                }
            }
            else
            {
                projects.Add(project);
            }
        }

        SelectedScriptGroup?.Projects.Clear();
        foreach (var scriptGroupProject in projects)
        {
            SelectedScriptGroup?.AddProject(scriptGroupProject);
        }

        Toast.Success($"增加了{projects.Count - oldcount}个地图追踪任务");
        if (SelectedScriptGroup != null) WriteScriptGroup(SelectedScriptGroup);
    }

    [RelayCommand]
    private void ReverseTaskOrder()
    {
        List<ScriptGroupProject> projects = new();
        projects.AddRange(SelectedScriptGroup?.Projects.Reverse() ?? []);
        SelectedScriptGroup?.Projects.Clear();
        projects.ForEach(item => SelectedScriptGroup?.Projects.Add(item));
        if (SelectedScriptGroup != null) WriteScriptGroup(SelectedScriptGroup);
    }
    [RelayCommand]
    private void ExportMergerJsons()
    {
        int count = 0;
        var pathDir = Path.Combine(LogPath, "exportMergerJson", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), "AutoPathing");
        foreach (var scriptGroupProject in SelectedScriptGroup?.Projects ?? [])
        {
            if (scriptGroupProject.Type == "Pathing")
            {
                var mergerJson = JsonMerger.getMergePathingJson(Path.Combine(MapPathingViewModel.PathJsonPath,
                    scriptGroupProject.FolderName, scriptGroupProject.Name));
                string fullPath = Path.Combine(pathDir, scriptGroupProject.FolderName, scriptGroupProject.Name);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, mergerJson);
                count++;
            }
        }
        if (count > 0)
        {
            Process.Start("explorer.exe", pathDir);
        }
    }


    [RelayCommand]
    public void AddScriptGroupNextFlag(ScriptGroup? item)
    {
        foreach (var scriptGroup in ScriptGroups)
        {
            scriptGroup.NextFlag = false;
        }

        if (item != null)
        {
            item.NextFlag = true;
            TaskContext.Instance().Config.NextScriptGroupName = item.Name;
        }
    }

    [RelayCommand]
    public void OnCopyScriptGroup(ScriptGroup? item)
    {
        if (item == null)
        {
            return;
        }

        var textBox = new System.Windows.Controls.TextBox()
        {
            VerticalAlignment = VerticalAlignment.Top
        };
        textBox.Loaded += (sender, e) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        var str = PromptDialog.Prompt("请输入配置组名称", "复制配置组", textBox, item.Name);
        if (!string.IsNullOrEmpty(str))
        {
            // 检查是否已存在
            if (ScriptGroups.Any(x => x.Name == str))
            {
                _snackbarService.Show(
                    "配置组已存在",
                    $"配置组 {str} 已经存在，复制失败",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(2)
                );
            }
            else
            {
                var newScriptGroup = JsonSerializer.Deserialize<ScriptGroup>(JsonSerializer.Serialize(item));
                if (newScriptGroup != null)
                {
                    newScriptGroup.Name = str;
                    ScriptGroups.Add(newScriptGroup);
                }

                //WriteScriptGroup(newScriptGroup);
            }
        }
    }

    [RelayCommand]
    public void OnRenameScriptGroup(ScriptGroup? item)
    {
        if (item == null)
        {
            return;
        }

        var textBox = new System.Windows.Controls.TextBox()
        {
            VerticalAlignment = VerticalAlignment.Top
        };
        textBox.Loaded += (sender, e) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        var str = PromptDialog.Prompt("请输入配置组名称", "重命名配置组", textBox, item.Name);
        if (!string.IsNullOrEmpty(str))
        {
            if (item.Name == str)
            {
                return;
            }

            // 检查是否已存在
            if (ScriptGroups.Any(x => x.Name == str))
            {
                _snackbarService.Show(
                    "配置组已存在",
                    $"配置组 {str} 已经存在，重命名失败",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(2)
                );
            }
            else
            {
                File.Move(Path.Combine(ScriptGroupPath, $"{item.Name}.json"), Path.Combine(ScriptGroupPath, $"{str}.json"));
                item.Name = str;
                if (item.NextFlag)
                {
                    TaskContext.Instance().Config.NextScriptGroupName = item.Name;
                }
                WriteScriptGroup(item);
            }
        }
    }

    [RelayCommand]
    public void OnDeleteScriptGroup(ScriptGroup? item)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            ScriptGroups.Remove(item);
            File.Delete(Path.Combine(ScriptGroupPath, $"{item.Name}.json"));
            _snackbarService.Show(
                "配置组删除成功",
                $"配置组 {item.Name} 已经被删除",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2)
            );
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "删除配置组配置时失败");
            _snackbarService.Show(
                "删除配置组配置失败",
                $"配置组 {item.Name} 删除失败！",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3)
            );
        }
    }

    [RelayCommand]
    private void OnAddJsScript()
    {
        var list = LoadAllJsScriptProjects();
        var stackPanel = CreateJsScriptSelectionPanel(list, typeof(CheckBox));

        var result = PromptDialog.Prompt("请选择需要添加的JS脚本", "请选择需要添加的JS脚本", stackPanel, new Size(500, 600));
        if (!string.IsNullOrEmpty(result))
        {
            AddSelectedJsScripts((StackPanel)stackPanel.Content);
        }
    }

    internal static ScrollViewer CreateJsScriptSelectionPanel(List<ScriptProject> list, Type selectType)
    {
        var stackPanel = new StackPanel();

        var filterTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            PlaceholderText = "输入搜索条件...",
        };
        filterTextBox.TextChanged += delegate { ApplyJsScriptFilter(stackPanel, list, filterTextBox.Text, selectType); };
        stackPanel.Children.Add(filterTextBox);

        AddJsScriptsToPanel(stackPanel, list, filterTextBox.Text, selectType);

        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            //Height = 435 // 固定高度
        };

        return scrollViewer;
    }

    private static void ApplyJsScriptFilter(StackPanel parentPanel, List<ScriptProject> scripts, string filter, Type selectType)
    {
        if (parentPanel.Children.Count > 0)
        {
            List<UIElement> removeElements = new List<UIElement>();
            foreach (UIElement parentPanelChild in parentPanel.Children)
            {
                if (parentPanelChild is FrameworkElement frameworkElement && frameworkElement.Name.StartsWith("dynamic_"))
                {
                    removeElements.Add(frameworkElement);
                }
            }

            removeElements.ForEach(parentPanel.Children.Remove);
        }

        AddJsScriptsToPanel(parentPanel, scripts, filter, selectType);
    }

    private static void AddJsScriptsToPanel(StackPanel parentPanel, List<ScriptProject> scripts, string filter, Type selectType)
    {
        foreach (var script in scripts)
        {
            var displayText = script.FolderName + " - " + script.Manifest.Name;

            if (!string.IsNullOrEmpty(filter) &&
                !displayText.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !script.FolderName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !script.Manifest.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (selectType == typeof(CheckBox))
            {
                var checkBox = new CheckBox
                {
                    Content = displayText,
                    Tag = script.FolderName,
                    Margin = new Thickness(0, 2, 0, 2),
                    Name = "dynamic_" + Guid.NewGuid().ToString().Replace("-", "_")
                };
                parentPanel.Children.Add(checkBox);
            }
            else if (selectType == typeof(RadioButton))
            {
                var radioButton = new RadioButton
                {
                    Content = displayText,
                    Tag = script.FolderName,
                    Margin = new Thickness(0, 2, 0, 2),
                    Name = "dynamic_" + Guid.NewGuid().ToString().Replace("-", "_"),
                    GroupName = "JsScriptsRadioButtonGroup"
                };
                parentPanel.Children.Add(radioButton);
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
        }
    }

    private void AddSelectedJsScripts(StackPanel stackPanel)
    {
        foreach (var child in stackPanel.Children)
        {
            if (child is CheckBox { IsChecked: true } checkBox && checkBox.Tag is string folderName)
            {
                SelectedScriptGroup?.AddProject(new ScriptGroupProject(new ScriptProject(folderName)));
            }
        }
    }

    [RelayCommand]
    private void OnAddKmScript()
    {
        var list = LoadAllKmScripts();
        var combobox = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Top
        };

        foreach (var fileInfo in list)
        {
            combobox.Items.Add(fileInfo.Name);
        }

        var str = PromptDialog.Prompt("请选择需要添加的键鼠脚本", "请选择需要添加的键鼠脚本", combobox);
        if (!string.IsNullOrEmpty(str))
        {
            SelectedScriptGroup?.AddProject(ScriptGroupProject.BuildKeyMouseProject(str));
        }
    }

    [RelayCommand]
    private void OnAddShell()
    {
        var str = PromptDialog.Prompt("执行 shell 操作存在极大风险！请勿输入你看不懂的指令！以免引发安全隐患并损坏系统！\n执行 shell 的时候，游戏可能会失去焦点", "请输入需要执行的shell");
        if (!string.IsNullOrEmpty(str))
        {
            SelectedScriptGroup?.AddProject(ScriptGroupProject.BuildShellProject(str));
        }
    }

    [RelayCommand]
    private async Task OnAddPathing()
    {
        try
        {
            // 在后台线程中加载数据
            var root = await Task.Run(() => FileTreeNodeHelper.LoadDirectory<PathingTask>(MapPathingViewModel.PathJsonPath));

            // 异步创建选择面板
            var stackPanel = await CreatePathingScriptSelectionPanelAsync(root.Children);

            // 显示选择对话框
            var result = PromptDialog.Prompt("请选择需要添加的地图追踪任务", "请选择需要添加的地图追踪任务", stackPanel, new Size(600, 720));

            if (!string.IsNullOrEmpty(result))
            {
                AddSelectedPathingScripts((StackPanel)stackPanel.Content);
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"加载地图追踪任务失败: {ex.Message}");
            _logger.LogError(ex, "加载地图追踪任务时发生错误");
        }
    }

    // 添加防抖计时器字段
    private DispatcherTimer? _debounceTimer;
    private const int DebounceDelayMs = 300;

    // 存储路径与UI元素的映射
    private readonly Dictionary<string, FrameworkElement> _nodeUIElements = [];

    /// <summary>
    /// 异步创建地图追踪任务选择面板
    /// </summary>
    private async Task<ScrollViewer> CreatePathingScriptSelectionPanelAsync(IEnumerable<FileTreeNode<PathingTask>> list)
    {
        var stackPanel = new StackPanel();
        CheckBox excludeCheckBox = new()
        {
            Content = "排除已选择过的目录",
            VerticalAlignment = VerticalAlignment.Center,
        };
        CheckBox deepCheckBox = new()
        {
            Content = "深度搜索",
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBox filterTextBox = new()
        {
            Margin = new Thickness(0, 0, 0, 10),
            PlaceholderText = "输入筛选条件...",
        };

        // 初始化防抖计时器
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceDelayMs)
        };

        excludeCheckBox.Click += delegate
        {
            _ = ApplyFilterToExistingNodesAsync(list, filterTextBox.Text, excludeCheckBox.IsChecked, deepCheckBox.IsChecked);
        };
        deepCheckBox.Click += delegate
        {
            _ = ApplyFilterToExistingNodesAsync(list, filterTextBox.Text, excludeCheckBox.IsChecked, deepCheckBox.IsChecked);
        };
        filterTextBox.TextChanged += delegate
        {
            _debounceTimer.Stop();

            // 设置计时器回调
            _debounceTimer.Tick -= OnDebounceTimerTick;
            _debounceTimer.Tick += OnDebounceTimerTick;

            _debounceTimer.Start();
            void OnDebounceTimerTick(object? sender, EventArgs e)
            {
                _debounceTimer.Stop();
                _debounceTimer.Tick -= OnDebounceTimerTick;
                _ = ApplyFilterToExistingNodesAsync(list, filterTextBox.Text, excludeCheckBox.IsChecked, deepCheckBox.IsChecked);
            }
        };

        stackPanel.Children.Add(excludeCheckBox);
        stackPanel.Children.Add(deepCheckBox);
        stackPanel.Children.Add(filterTextBox);

        // 异步构建UI树
        await BuildCompleteUITreeAsync(stackPanel, list, 0);

        filterTextBox.Focus();

        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        return scrollViewer;
    }

    /// <summary>
    /// 异步构建完整的UI树
    /// </summary>
    /// <param name="parentPanel">构建内容的父容器</param>
    /// <param name="nodes">要构建的节点集合</param>
    /// <param name="depth">构建的深度</param>
    private async Task BuildCompleteUITreeAsync(StackPanel parentPanel, IEnumerable<FileTreeNode<PathingTask>>? nodes, int depth)
    {
        if (nodes == null)
            return;

        var nodeList = nodes.ToList();

        for (int i = 0; i < nodeList.Count; i += 1)
        {
            var batch = nodeList.Skip(i).Take(1);

            // 在UI线程中创建UI元素并添加到面板
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                foreach (var node in batch)
                {
                    var element = CreateUIElementForNode(node, depth);
                    parentPanel.Children.Add(element);

                    // 如果是目录且有子节点，递归构建子节点
                    if (node.IsDirectory && node.Children?.Any() == true && element is Expander expander)
                    {
                        if (expander.Content is StackPanel childPanel)
                        {
                            await BuildCompleteUITreeAsync(childPanel, node.Children, depth + 1);
                        }
                    }
                }
            }, DispatcherPriority.Background);

            // 让出控制权，避免长时间阻塞UI线程
            await Task.Delay(1);
        }
    }
    
    /// <summary>
    /// 为单个节点创建UI元素
    /// </summary>
    /// <param name="node">文件树节点</param>
    /// <param name="depth">节点深度</param>
    /// <returns>创建的UI元素</returns>
    private FrameworkElement CreateUIElementForNode(FileTreeNode<PathingTask> node, int depth)
    {
        var checkBox = new CheckBox
        {
            Content = node.FileName,
            Tag = node.FilePath,
            Margin = new Thickness(depth * 30, 0, 0, 0),
            Name = "dynamic_" + Guid.NewGuid().ToString().Replace("-", "_")
        };

        // 存储路径与UI元素的映射
        if (!string.IsNullOrEmpty(node.FilePath))
            _nodeUIElements[node.FilePath] = checkBox;

        if (node.IsDirectory)
        {
            // 如果父节点没有任何子内容，则不可勾选
            if (node.Children == null || node.Children.Count == 0)
                checkBox.IsEnabled = false;

            var childPanel = new StackPanel();
            checkBox.IsThreeState = true;
            var expander = new Expander
            {
                Header = checkBox,
                Content = childPanel,
                IsExpanded = false,
                Name = "dynamic_" + Guid.NewGuid().ToString().Replace("-", "_"),
                Visibility = Visibility.Visible
            };

            // 存储路径与UI元素的映射
            if (!string.IsNullOrEmpty(node.FilePath))
            {
                _nodeUIElements[node.FilePath + "_expander"] = expander;
            }

            // 修改事件处理：用户点击时只在全选和全不选之间切换
            checkBox.Click += (s, e) => HandleDirectoryCheckBoxClick(checkBox, childPanel);

            return expander;
        }
        else
        {
            // 为文件复选框添加状态改变事件，用于更新父级状态
            checkBox.Checked += (s, e) => UpdateParentCheckBoxState(checkBox);
            checkBox.Unchecked += (s, e) => UpdateParentCheckBoxState(checkBox);

            return checkBox;
        }
    }

    /// <summary>
    /// 异步应用筛选到已存在的节点
    /// <param name="nodes">要筛选的节点集合</param>
    /// <param name="filter">用户输入的筛选关键词</param>
    /// <param name="excludeSelectedFolder">排除选择的目录</param>
    /// <param name="isDeepSearch">深度搜索功能</param>
    /// </summary>
    private async Task ApplyFilterToExistingNodesAsync(IEnumerable<FileTreeNode<PathingTask>> nodes, string filter, bool? excludeSelectedFolder = false, bool? isDeepSearch = false)
    {
        var filteredResult = await Task.Run(() =>
        {
            IEnumerable<FileTreeNode<PathingTask>> filteredNodes = nodes;

            // 如果启用排除已选择过的目录，先过滤掉这些目录
            if (excludeSelectedFolder ?? false)
            {
                List<string> skipFolderNames = SelectedScriptGroup?.Projects.ToList().Select(item => item.FolderName).Distinct().ToList() ?? [];
                string jsonString = JsonSerializer.Serialize(nodes);
                var copiedNodes = JsonSerializer.Deserialize<ObservableCollection<FileTreeNode<PathingTask>>>(jsonString);
                if (copiedNodes != null)
                {
                    copiedNodes = FileTreeNodeHelper.FilterTree(copiedNodes, skipFolderNames);
                    copiedNodes = FileTreeNodeHelper.FilterEmptyNodes(copiedNodes);
                    filteredNodes = copiedNodes;
                }
            }

            return filteredNodes;
        });

        // 在UI线程中更新可见性
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // 重置所有节点的可见性
            foreach (var element in _nodeUIElements.Values)
                element.Visibility = Visibility.Collapsed;

            UpdateNodesVisibility(filteredResult, filter, isDeepSearch);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 更新节点的可见性和展开状态
    /// </summary>
    /// <param name="nodes">要处理的文件树节点集合</param>
    /// <param name="filter">用户输入的筛选关键词</param>
    /// <param name="isDeepSearch">是否启用深度搜索</param>
    /// <param name="depth">当前节点在树中的深度级别</param>
    /// <param name="parentMatched">当前节点的父级是否已经匹配筛选条件</param>
    /// <param name="returnMatchStatus">是否返回匹配状态（用于子节点处理）</param>
    /// <returns>returnMatchStatus则返回是否包含匹配的节点</returns>
    private bool UpdateNodesVisibility(IEnumerable<FileTreeNode<PathingTask>> nodes, string filter, bool? isDeepSearch, int depth = 0, bool parentMatched = false, bool returnMatchStatus = false)
    {
        bool containsMatch = false;

        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.FilePath))
                continue;

            bool nodeMatches = !string.IsNullOrEmpty(filter) && IsNodeMatched(node, filter);
            bool shouldShow = ShouldShowNode(node, filter, isDeepSearch, depth, parentMatched);

            // 更新节点可见性
            if (_nodeUIElements.TryGetValue(node.FilePath, out var element))
            {
                element.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                if (shouldShow && nodeMatches && returnMatchStatus)
                    containsMatch = true;
            }

            // 如果是目录节点，递归处理子节点并更新展开状态
            if (node.IsDirectory && _nodeUIElements.TryGetValue(node.FilePath + "_expander", out var expanderElement) && expanderElement is Expander expander)
            {
                if (shouldShow)
                {
                    // 递归处理子节点，传入returnMatchStatus = true来获取子节点匹配状态
                    bool childContainsMatch = UpdateNodesVisibility(node.Children, filter, isDeepSearch, depth + 1, nodeMatches || parentMatched, true);

                    // 如果子节点包含匹配且需要返回匹配状态，当前层级也标记为包含匹配
                    if (childContainsMatch && returnMatchStatus)
                        containsMatch = true;

                    expander.IsExpanded = ShouldExpandNode(filter, nodeMatches, parentMatched, childContainsMatch, depth, isDeepSearch, GetParentFolderName(node));
                    expander.Visibility = Visibility.Visible;
                }
                else
                {
                    expander.Visibility = Visibility.Collapsed;
                }
            }
        }

        return containsMatch;
    }

    /// <summary>
    /// 该节点是否应该显示
    /// </summary>
    /// <param name="node">要检查的节点</param>
    /// <param name="filter">筛选条件</param>
    /// <param name="isDeepSearch">是否启用深度搜索</param>
    /// <param name="currentDepth">当前深度</param>
    /// <param name="parentMatched">父节点是否已匹配</param>
    /// <returns>是否应该显示该节点</returns>
    private static bool ShouldShowNode(FileTreeNode<PathingTask> node, string filter, bool? isDeepSearch = false, int currentDepth = 0, bool parentMatched = false)
    {
        // 如果没有筛选条件，显示所有节点
        if (string.IsNullOrEmpty(filter))
            return true;

        // 如果该节点任意层级父节点已匹配，则忽略深度限制显示其全部子内容
        if (parentMatched)
            return true;

        bool currentNodeMatches = IsNodeMatched(node, filter);

        // 如果该节点匹配，显示该节点
        if (currentNodeMatches)
            return true;

        // 不超过允许深度的前提下，递归目录节点，逐一判断其所有子节点是否应该显示
        if (currentDepth >= GetMaxDepth(isDeepSearch, GetParentFolderName(node)))
            return false;

        if (node.IsDirectory && node.Children?.Any() == true)
        {
            foreach (var child in node.Children)
            {
                // 递归时，传递当前节点的匹配状态，任意当前深度的节点应该显示，则当前节点也应该显示
                if (ShouldShowNode(child, filter, isDeepSearch, currentDepth + 1, currentNodeMatches))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 该节点是否匹配
    /// </summary>
    /// <param name="node">要检查的节点</param>
    /// <param name="filter">筛选条件</param>
    /// <returns>是否匹配</returns>
    private static bool IsNodeMatched(FileTreeNode<PathingTask> node, string filter)
    {
        // 该节点名称是否匹配
        if (node.FileName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // 往前追溯，该节点路径中是否至少有一段匹配
        if (!string.IsNullOrEmpty(node.FilePath))
        {
            var relativePath = Path.GetRelativePath(MapPathingViewModel.PathJsonPath, node.FilePath);
            var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 处理路径段匹配，对于文件名需要去除扩展名
            foreach (var segment in pathSegments)
            {
                // 如果这是最后一个段且不是目录，则去除扩展名后匹配
                var segmentToMatch = segment;
                if (segment == pathSegments.Last() && !node.IsDirectory)
                {
                    segmentToMatch = Path.GetFileNameWithoutExtension(segment);
                }

                if (segmentToMatch.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 该节点是否应该自动展开
    /// </summary>
    /// <param name="filter">筛选条件</param>
    /// <param name="currentNodeMatches">当前节点是否匹配</param>
    /// <param name="parentMatched">父节点是否已匹配</param>
    /// <param name="childContainsMatch">子树是否包含匹配</param>
    /// <param name="depth">当前深度</param>
    /// <param name="isDeepSearch">是否启用深度搜索</param>
    /// <param name="parentFolderName">父文件夹名称</param>
    /// <returns>是否应该展开</returns>
    private static bool ShouldExpandNode(string filter, bool currentNodeMatches, bool parentMatched, bool childContainsMatch, int depth, bool? isDeepSearch, string? parentFolderName)
    {
        // 如果没有筛选条件（输入框为空），所有节点都不展开
        if (string.IsNullOrEmpty(filter))
            return false;

        // 如果该节点的父节点已匹配，子目录不再展开，便于浏览
        if (parentMatched)
            return false;

        // 该节点的深度大于等于深度限制时，不再展开
        if (depth >= GetMaxDepth(isDeepSearch, parentFolderName))
            return false;

        // 该节点名称直接匹配，自动展开
        if (!string.IsNullOrEmpty(filter) && currentNodeMatches)
            return true;

        // 该节点的子树中存在至少一个匹配的节点，自动展开该节点以显示深层匹配节点
        if (childContainsMatch)
            return true;

        return false;
    }

    /// <summary>
    /// 获取该节点的父文件夹名称
    /// </summary>
    /// <param name="node">节点</param>
    /// <returns>父文件夹名称</returns>
    private static string? GetParentFolderName(FileTreeNode<PathingTask> node)
    {
        // 如果节点没有文件路径，返回 null
        if (string.IsNullOrEmpty(node.FilePath))
            return null;

        // 获取相对于 PathJsonPath 的路径
        var relativePath = Path.GetRelativePath(MapPathingViewModel.PathJsonPath, node.FilePath);
        var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 返回第一级目录名称
        return pathSegments.Length > 0 ? pathSegments[0] : null;
    }

    /// <summary>
    /// 获取允许的最大深度
    /// </summary>
    /// <param name="isDeepSearch">是否启用深度搜索</param>
    /// <param name="parentFolderName">父文件夹文件名内容</param>
    /// <param name="currentNodeMatched">当前节点是否匹配</param>
    /// <param name="parentMatched">父节点是否已匹配</param>
    /// <returns>允许的深度</returns>
    private static int GetMaxDepth(bool? isDeepSearch, string? parentFolderName, bool currentNodeMatched = false, bool parentMatched = false)
    {
        // 如果开启深度搜索，允许全部子内容
        if (isDeepSearch == true)
            return int.MaxValue;

        // 如果当前节点匹配或父节点已匹配，允许全部子内容
        if (currentNodeMatched || parentMatched)
            return int.MaxValue;

        int defaultDepth = 1;

        // 特殊目录的深度扩展
        if (parentFolderName == "地方特产")
            return defaultDepth + 1;

        return defaultDepth;
    }

    /// <summary>
    /// 处理目录复选框点击事件
    /// </summary>
    /// <param name="checkBox">被点击的目录复选框</param>
    /// <param name="childPanel">子面板</param>
    private void HandleDirectoryCheckBoxClick(CheckBox checkBox, StackPanel childPanel)
    {
        var childCheckBoxes = GetAllChildCheckBoxes(childPanel);

        // 判断目标状态：如果所有子项都已选中，则全不选；否则全选
        bool allChildrenChecked = childCheckBoxes.Count > 0 && childCheckBoxes.All(cb => cb.IsChecked == true);
        bool targetState = !allChildrenChecked;

        checkBox.IsChecked = targetState;
        SetChildCheckBoxesState(childPanel, targetState);
        UpdateParentCheckBoxState(checkBox);
    }

    /// <summary>
    /// 递归获取面板中所有的子复选框
    /// </summary>
    /// <param name="panel">获取的面板</param>
    /// <returns>所有子复选框列表</returns>
    private List<CheckBox> GetAllChildCheckBoxes(StackPanel panel)
    {
        var checkBoxes = new List<CheckBox>();

        foreach (var child in panel.Children)
        {
            if (child is CheckBox checkBox)
            {
                checkBoxes.Add(checkBox);
            }
            else if (child is Expander expander)
            {
                if (expander.Header is CheckBox headerCheckBox)
                {
                    checkBoxes.Add(headerCheckBox);
                }

                if (expander.Content is StackPanel nestedPanel)
                {
                    checkBoxes.AddRange(GetAllChildCheckBoxes(nestedPanel));
                }
            }
        }

        return checkBoxes;
    }

    /// <summary>
    /// 更新父级复选框的三态状态
    /// </summary>
    /// <param name="changedCheckBox">状态发生改变的复选框</param>
    private void UpdateParentCheckBoxState(CheckBox changedCheckBox)
    {
        // 查找父级复选框
        var parentCheckBox = FindParentCheckBox(changedCheckBox);
        if (parentCheckBox == null)
            return;

        // 获取同级所有复选框
        var siblingCheckBoxes = GetSiblingCheckBoxes(changedCheckBox);

        // 计算状态
        int checkedCount = siblingCheckBoxes.Count(cb => cb.IsChecked == true);
        int uncheckedCount = siblingCheckBoxes.Count(cb => cb.IsChecked == false);
        int indeterminateCount = siblingCheckBoxes.Count(cb => cb.IsChecked == null);

        // 设置父级复选框状态
        if (checkedCount == siblingCheckBoxes.Count)
            parentCheckBox.IsChecked = true;
        else if (uncheckedCount == siblingCheckBoxes.Count)
            parentCheckBox.IsChecked = false;
        else
            parentCheckBox.IsChecked = null;

        // 递归更新上级父级
        UpdateParentCheckBoxState(parentCheckBox);
    }

    /// <summary>
    /// 查找指定复选框的父级复选框
    /// </summary>
    /// <param name="checkBox">当前复选框</param>
    /// <returns>父级复选框，如果没有则返回null</returns>
    private CheckBox? FindParentCheckBox(CheckBox checkBox)
    {
        var filePath = checkBox.Tag as string;
        if (string.IsNullOrEmpty(filePath))
            return null;

        // 获取父目录路径
        var parentPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(parentPath))
            return null;

        // 查找父级复选框
        if (_nodeUIElements.TryGetValue(parentPath, out var parentElement) && parentElement is CheckBox parentCheckBox)
        {
            return parentCheckBox;
        }

        return null;
    }

    /// <summary>
    /// 获取同级的所有复选框
    /// </summary>
    /// <param name="checkBox">当前复选框</param>
    /// <returns>同级复选框列表</returns>
    private static List<CheckBox> GetSiblingCheckBoxes(CheckBox checkBox)
    {
        // 先尝试获取逻辑父级
        var parent = LogicalTreeHelper.GetParent(checkBox) as FrameworkElement;

        // 如果逻辑父级不存在，尝试可视化父级
        parent ??= VisualTreeHelper.GetParent(checkBox) as FrameworkElement;

        // 如果当前元素是Expander的Header，获取Expander的父级
        if (parent is Expander expander)
        {
            parent = LogicalTreeHelper.GetParent(expander) as FrameworkElement ??
                     VisualTreeHelper.GetParent(expander) as FrameworkElement;
        }

        // 遍历同级元素
        var siblings = new List<CheckBox>();
        if (parent is StackPanel stackPanel)
        {
            foreach (var child in stackPanel.Children)
            {
                if (child is CheckBox siblingCheckBox)
                    siblings.Add(siblingCheckBox);
                else if (child is Expander childExpander && childExpander.Header is CheckBox expanderCheckBox)
                    siblings.Add(expanderCheckBox);
            }
        }

        return siblings;
    }

    /// <summary>
    /// 递归设置子复选框状态
    /// </summary>
    /// <param name="childStackPanel">子面板</param>
    /// <param name="state">目标状态</param>
    private static void SetChildCheckBoxesState(StackPanel childStackPanel, bool state)
    {
        foreach (var child in childStackPanel.Children)
        {
            if (child is CheckBox checkBox)
            {
                checkBox.IsChecked = state;
            }
            else if (child is Expander expander && expander.Content is StackPanel nestedStackPanel)
            {
                if (expander.Header is CheckBox headerCheckBox)
                {
                    headerCheckBox.IsChecked = state;
                }

                SetChildCheckBoxesState(nestedStackPanel, state);
            }
        }
    }

    private void AddSelectedPathingScripts(StackPanel stackPanel)
    {
        foreach (var child in stackPanel.Children)
        {
            if (child is CheckBox { IsChecked: true } checkBox && checkBox.Tag is string filePath)
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Attributes.HasFlag(FileAttributes.Directory))
                {
                    var relativePath = Path.GetRelativePath(MapPathingViewModel.PathJsonPath, fileInfo.Directory!.FullName);
                    SelectedScriptGroup?.AddProject(ScriptGroupProject.BuildPathingProject(fileInfo.Name, relativePath));
                }
            }
            else if (child is Expander { Content: StackPanel nestedStackPanel })
            {
                AddSelectedPathingScripts(nestedStackPanel);
            }
        }
    }

    // private Dictionary<string, List<FileInfo>> LoadAllPathingScripts()
    // {
    //     var folder = Global.Absolute(@"User\AutoPathing");
    //     var directories = Directory.GetDirectories(folder);
    //     var result = new Dictionary<string, List<FileInfo>>();
    //
    //     foreach (var directory in directories)
    //     {
    //         var dirInfo = new DirectoryInfo(directory);
    //         var files = dirInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).ToList();
    //         result.Add(dirInfo.Name, files);
    //     }
    //
    //     return result;
    // }

    internal static List<ScriptProject> LoadAllJsScriptProjects()
    {
        var path = Global.ScriptPath();
        Directory.CreateDirectory(path);
        // 获取所有脚本项目
        var projects = Directory.GetDirectories(path)
            .Select(x =>
            {
                try
                {
                    return new ScriptProject(Path.GetFileName(x));
                }
                catch (Exception e)
                {
                    Toast.Warning($"加载单个脚本失败：{e.Message}");
                    return null;
                }
            })
            .Where(x => x != null)
            .ToList();
        return projects;
    }

    private List<FileInfo> LoadAllKmScripts()
    {
        var folder = Global.Absolute(@"User\KeyMouseScript");
        // 获取所有脚本项目
        var files = Directory.GetFiles(folder, "*.*",
            SearchOption.AllDirectories);

        return files.Select(file => new FileInfo(file)).ToList();
    }

    [RelayCommand]
    public void OnEditScriptCommon(ScriptGroupProject? item)
    {
        if (item == null)
        {
            return;
        }

        ShowEditWindow(item);

        // foreach (var group in ScriptGroups)
        // {
        //     WriteScriptGroup(group);
        // }
    }

    [RelayCommand]
    private void AddNextFlag(ScriptGroupProject? item)
    {
        if (item == null || SelectedScriptGroup == null)
        {
            return;
        }

        List<ValueTuple<string, int, string, string>> nextScheduledTask = TaskContext.Instance().Config.NextScheduledTask;
        var nst = nextScheduledTask.Find(item2 => item2.Item1 == SelectedScriptGroup?.Name);
        if (nst != default)
        {
            nextScheduledTask.Remove(nst);
        }

        nextScheduledTask.Add((SelectedScriptGroup?.Name ?? "", item.Index, item.FolderName, item.Name));
        foreach (var item1 in SelectedScriptGroup?.Projects ?? [])
        {
            item1.NextFlag = false;
        }

        item.NextFlag = true;
    }

    public static void ShowEditWindow(ScriptGroupProject project)
    {
        var viewModel = new ScriptGroupProjectEditorViewModel(project);
        var editor = new ScriptGroupProjectEditor(project)
        {
            DataContext = viewModel
        };
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "修改通用设置",
            Content = editor,
            CloseButtonText = "关闭",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        uiMessageBox.ShowDialogAsync();
    }

    [RelayCommand]
    public void OnEditJsScriptSettings(ScriptGroupProject? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.Project == null)
        {
            item.BuildScriptProjectRelation();
        }

        if (item.Project == null)
        {
            return;
        }

        if (item.Type == "Javascript")
        {
            if (item.JsScriptSettingsObject == null)
            {
                item.JsScriptSettingsObject = new ExpandoObject();
            }

            var ui = item.Project.LoadSettingUi(item.JsScriptSettingsObject);
            if (ui == null)
            {
                Toast.Warning("此脚本未提供自定义配置");
                return;
            }

            var uiMessageBox = new Wpf.Ui.Controls.MessageBox
            {
                Title = "修改JS脚本自定义设置    ",
                Content = ui,
                CloseButtonText = "关闭",
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            uiMessageBox.ShowDialogAsync();

            // 由于 JsScriptSettingsObject 的存在，这里只能手动再次保存配置
            foreach (var group in ScriptGroups)
            {
                WriteScriptGroup(group);
            }
        }
        else
        {
            Toast.Warning("只有JS脚本才有自定义配置");
        }
    }

    [RelayCommand]
    public async void OnDeleteScriptByFolder(ScriptGroupProject? item)
    {
        if (item == null)
        {
            return;
        }

        if (SelectedScriptGroup != null)
        {
            var toBeDeletedProjects = SelectedScriptGroup.Projects
                .Where(item2 => item2.FolderName == item.FolderName)
                .ToList();

            foreach (var project in toBeDeletedProjects)
            {
                SelectedScriptGroup.Projects.Remove(project);
            }

            _snackbarService.Show(
                "脚本配置移除成功",
                $"已移除 {item.FolderName} 下的所有关联配置",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2)
            );
        }
    }

    [RelayCommand]
    public void OnDeleteScript(ScriptGroupProject? item)
    {
        if (item == null)
        {
            return;
        }

        SelectedScriptGroup?.Projects.Remove(item);
        _snackbarService.Show(
            "脚本配置移除成功",
            $"{item.Name} 的关联配置已经移除",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2)
        );
    }

    [RelayCommand]
    public void OnOpenScriptFolder(ScriptGroupProject? item)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            string? path = null;
            switch (item.Type)
            {
                case "Javascript":
                    path = Path.Combine(Global.ScriptPath(), item.FolderName);
                    break;
                case "KeyMouse":
                    path = Global.Absolute(@"User\KeyMouseScript");
                    break;
                case "Pathing":
                    path = Path.Combine(MapPathingViewModel.PathJsonPath, item.FolderName);
                    break;
            }

            if (path != null && Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                _snackbarService.Show("打开失败", "目录不存在", ControlAppearance.Caution, null, TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "打开脚本目录失败");
            _snackbarService.Show("打开失败", e.Message, ControlAppearance.Danger, null, TimeSpan.FromSeconds(3));
        }
    }
    private void ScriptGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ScriptGroup newItem in e.NewItems)
            {
                newItem.Projects.CollectionChanged += ScriptProjectsCollectionChanged;
                foreach (var project in newItem.Projects)
                {
                    project.PropertyChanged += ScriptProjectsPChanged;
                }
            }
        }

        if (e.OldItems != null)
        {
            foreach (ScriptGroup oldItem in e.OldItems)
            {
                foreach (var project in oldItem.Projects)
                {
                    project.PropertyChanged -= ScriptProjectsPChanged;
                }

                oldItem.Projects.CollectionChanged -= ScriptProjectsCollectionChanged;
            }
        }

        // 补充排序字段
        var i = 1;
        foreach (var group in ScriptGroups)
        {
            group.Index = i++;
        }

        // 保存配置组配置
        foreach (var group in ScriptGroups)
        {
            WriteScriptGroup(group);
        }
    }

    private void ScriptProjectsPChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var group in ScriptGroups)
        {
            WriteScriptGroup(group);
        }
    }

    private void ScriptProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 补充排序字段
        if (SelectedScriptGroup is { Projects.Count: > 0 })
        {
            var i = 1;
            foreach (var project in SelectedScriptGroup.Projects)
            {
                project.Index = i++;
            }
        }

        // 保存配置组配置
        if (SelectedScriptGroup != null)
        {
            WriteScriptGroup(SelectedScriptGroup);
        }
    }


    private void WriteScriptGroup(ScriptGroup scriptGroup)
    {
        try
        {
            if (!Directory.Exists(ScriptGroupPath))
            {
                Directory.CreateDirectory(ScriptGroupPath);
            }

            var file = Path.Combine(ScriptGroupPath, $"{scriptGroup.Name}.json");
            File.WriteAllText(file, scriptGroup.ToJson());
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "保存配置组配置时失败");
            _snackbarService.Show(
                "保存配置组配置失败",
                $"{scriptGroup.Name} 保存失败！",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3)
            );
        }
    }

    private static void SetTaskContextNextFlag(ScriptGroup group)
    {
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
    }

    private void ReadScriptGroup()
    {
        try
        {
            if (!Directory.Exists(ScriptGroupPath))
            {
                Directory.CreateDirectory(ScriptGroupPath);
            }

            ScriptGroups.Clear();
            var files = Directory.GetFiles(ScriptGroupPath, "*.json");
            List<ScriptGroup> groups = [];
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var group = ScriptGroup.FromJson(json);
                    SetTaskContextNextFlag(group);
                    if (group.Name == TaskContext.Instance().Config.NextScriptGroupName)
                    {
                        group.NextFlag = true;
                    }
                    groups.Add(group);
                }
                catch (Exception e)
                {
                    _logger.LogDebug(e, "读取单个配置组配置时失败");
                    _snackbarService.Show(
                        "读取配置组配置失败",
                        "读取配置组配置失败:" + e.Message,
                        ControlAppearance.Danger,
                        null,
                        TimeSpan.FromSeconds(3)
                    );
                }
            }

            // 按index排序
            groups.Sort((a, b) => a.Index.CompareTo(b.Index));
            foreach (var group in groups)
            {
                ScriptGroups.Add(group);
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "读取配置组配置时失败");
            _snackbarService.Show(
                "读取配置组配置失败",
                "读取配置组配置失败！",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3)
            );
        }
    }

    [RelayCommand]
    public void OnGoToScriptGroupUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/autos/dispatcher.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnImportScriptGroup(string scriptGroupExample)
    {
        ScriptGroup group = new();
        if ("AutoCrystalflyExampleGroup" == scriptGroupExample)
        {
            group.Name = "晶蝶示例组";
            group.AddProject(new ScriptGroupProject(new ScriptProject("AutoCrystalfly")));
        }

        if (ScriptGroups.Any(x => x.Name == group.Name))
        {
            _snackbarService.Show(
                "配置组已存在",
                $"配置组 {group.Name} 已经存在，请勿重复添加",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(2)
            );
            return;
        }

        ScriptGroups.Add(group);
    }

    [RelayCommand]
    public async Task OnStartScriptGroupAsync()
    {
        if (SelectedScriptGroup == null)
        {
            _snackbarService.Show(
                "未选择配置组",
                "请先选择一个配置组",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(2)
            );
            return;
        }

        RunnerContext.Instance.Reset();

        TaskProgress taskProgress = new()
        {
            ScriptGroupNames = [SelectedScriptGroup.Name]
        };
        RunnerContext.Instance.taskProgress = taskProgress;
        taskProgress.CurrentScriptGroupName = SelectedScriptGroup.Name;
        TaskProgressManager.SaveTaskProgress(taskProgress);
        await _scriptService.RunMulti(GetNextProjects(SelectedScriptGroup), SelectedScriptGroup.Name, taskProgress);
    }

    [RelayCommand]
    public void OnOpenScriptGroupSettings()
    {
        if (SelectedScriptGroup == null)
        {
            return;
        }

        // var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        // {
        //     Content = new ScriptGroupConfigView(SelectedScriptGroup.Config),
        //     Title = "配置组设置"
        // };
        //
        // await uiMessageBox.ShowDialogAsync();

        var dialogWindow = new Window
        {
            Title = "配置组设置",
            Content = new ScriptGroupConfigView(new ScriptGroupConfigViewModel(TaskContext.Instance().Config, SelectedScriptGroup.Config)),
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        // var dialogWindow = new WpfUiWindow(new ScriptGroupConfigView(SelectedScriptGroup.Config))
        // {
        //     Title = "配置组设置"
        // };

        // 显示对话框
        var result = dialogWindow.ShowDialog();

        // if (result == true)
        // {
        //     // 用户点击了确定或关闭
        // }

        WriteScriptGroup(SelectedScriptGroup);
    }

    public static List<ScriptGroup> GetNextScriptGroups(List<ScriptGroup> groups)
    {
        if (groups.Where(g => g.NextFlag).Count() > 0)
        {
            List<ScriptGroup> ng = new();
            bool start = false;
            foreach (var group in groups)
            {
                if (group.NextFlag)
                {
                    start = true;
                    group.NextFlag = false;
                    TaskContext.Instance().Config.NextScriptGroupName = String.Empty;
                }

                if (start)
                {
                    ng.Add(group);
                }
            }

            return ng;
        }

        return groups;
    }

    public static List<ScriptGroupProject> GetNextProjects(ScriptGroup group)
    {
        SetTaskContextNextFlag(group);
        List<ScriptGroupProject> ls = new List<ScriptGroupProject>();
        if (group.Projects.Where(g => g.NextFlag ?? false).Count() > 0)
        {
            bool start = false;
            foreach (var item in group.Projects)
            {
                if (item.NextFlag ?? false)
                {
                    start = true;
                }

                if (!start)
                {
                    item.SkipFlag = true;
                }
                ls.Add(item);
            }

            if (!start)
            {
                ls.AddRange(group.Projects);
            }

            //拿出来后清空，和置状态
            if (start)
            {
                List<ValueTuple<string, int, string, string>> nextScheduledTask = TaskContext.Instance().Config.NextScheduledTask;
                foreach (var item in nextScheduledTask)
                {
                    if (item.Item1 == group.Name)
                    {
                        nextScheduledTask.Remove(item);
                        break;
                    }
                }

                foreach (var item in group.Projects)
                {
                    item.NextFlag = false;
                }
            }

            return ls;
        }

        return group.Projects.Select(g => g).ToList();
    }

    [RelayCommand]
    public async Task OnContinueMultiScriptGroupAsync()
    {

        // 创建一个 StackPanel 来包含全选按钮和所有配置组的 CheckBox
        var stackPanel = new StackPanel();


        // 添加分割线
        var separator = new Separator
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        stackPanel.Children.Add(separator);

        List<TaskProgress> taskProgresses = TaskProgressManager.LoadAllTaskProgress();
        var checkBox = new ComboBox(); ;
        stackPanel.Children.Add(checkBox);
        ObservableCollection<KeyValuePair<string, string>> kvs = new ObservableCollection<KeyValuePair<string, string>>();
        foreach (var taskProgress in taskProgresses)
        {
            var name = taskProgress.Name + "_" + taskProgress.CurrentScriptGroupName + "_";
            if (taskProgress.Loop)
            {
                name += "循环(" + taskProgress.LoopCount + ")_";
            }
            if (taskProgress.CurrentScriptGroupProjectInfo != null)
            {
                name = name + taskProgress.CurrentScriptGroupProjectInfo.Index + "_" + taskProgress.CurrentScriptGroupProjectInfo.Name;
            }
            kvs.Add(new KeyValuePair<string, string>(taskProgress.Name, name));
        }

        checkBox.SelectedValuePath = "Key";
        checkBox.DisplayMemberPath = "Value";
        checkBox.ItemsSource = kvs;
        checkBox.SelectedIndex = 0;
        //SelectedValuePath="Key"
        // DisplayMemberPath="Value"
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "选择需要继续执行的进度记录",
            Content = new ScrollViewer
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 300 // 设置固定高度
                ,
                Width = 600
            },
            CloseButtonText = "关闭",
            PrimaryButtonText = "确认执行",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = await uiMessageBox.ShowDialogAsync();
        if (result == MessageBoxResult.Primary)
        {

            /*var selectedGroups = checkBoxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToList();*/
            Object val = checkBox.SelectedValue;
            if (val == null)
            {
                return;
            }
            await OnContinueTaskProgressAsync(Convert.ToString(val), taskProgresses);

        }
    }

    public async Task OnContinueTaskProgressAsync(string name, List<TaskProgress>? taskProgresses = null)
    {
        if (taskProgresses == null)
        {
            taskProgresses = TaskProgressManager.LoadAllTaskProgress();
        }
        TaskProgress? taskProgress = null;
        if (name == "latest")
        {
            if (taskProgresses.Count > 0)
            {
                taskProgress = taskProgresses[0];
            }
        }
        else
        {
            taskProgress = taskProgresses.FirstOrDefault(t => t.Name == name);
        }



        if (taskProgress != null)
        {
            //await StartGroups(selectedGroups);
            //taskProgress.Next
            var sg = ScriptGroups.ToList().Where(sg => taskProgress.ScriptGroupNames.Contains(sg.Name)).ToList();
            TaskProgressManager.GenerNextProjectInfo(taskProgress, sg);
            if (taskProgress.Next == null)
            {
                _logger.LogWarning("无法定位到下一个要执行的项目：next为空（" + taskProgress.Name + ")");
            }
            else
            {
                await StartGroups(sg, taskProgress);
            }

        }
        else
        {
            _logger.LogWarning("无法定位到下一个要执行的项目:taskProgress为空");
        }
    }

    public async Task OnStartMultiScriptTaskProgressAsync(params string[] names)
    {
        if (ScriptGroups.Count == 0)
        {
            ReadScriptGroup();
        }

        string taskProgressName;
        if (names == null || names.Length == 0)
        {
            taskProgressName = "latest";
        }
        else
        {
            taskProgressName = names[0];
        }

        await OnContinueTaskProgressAsync(taskProgressName);
    }

    [RelayCommand]
    public async Task OnStartMultiScriptGroupAsync()
    {
        // 创建一个 StackPanel 来包含全选按钮和所有配置组的 CheckBox
        var stackPanel = new StackPanel();
        var checkBoxes = new Dictionary<ScriptGroup, CheckBox>();


        var loopCheckBox = new CheckBox
        {
            Content = "循环",
        };


        // 创建全选按钮
        var selectAllCheckBox = new CheckBox
        {
            Content = "全选",
            IsThreeState = true
        };
        selectAllCheckBox.Checked += (s, e) =>
        {
            foreach (var checkBox in checkBoxes.Values)
            {
                checkBox.IsChecked = true;
            }
        };
        selectAllCheckBox.Unchecked += (s, e) =>
        {
            foreach (var checkBox in checkBoxes.Values)
            {
                checkBox.IsChecked = false;
            }
        };
        selectAllCheckBox.Indeterminate += (s, e) =>
        {
            if (checkBoxes.Values.All(cb => cb.IsChecked == true))
            {
                selectAllCheckBox.IsChecked = false;
            }
            else if (checkBoxes.Values.All(cb => cb.IsChecked == false))
            {
                selectAllCheckBox.IsChecked = true;
            }
        };

        stackPanel.Children.Add(loopCheckBox);
        stackPanel.Children.Add(selectAllCheckBox);

        // 添加分割线
        var separator = new Separator
        {
            Margin = new Thickness(0, 4, 0, 4)
        };
        stackPanel.Children.Add(separator);

        // 创建每个配置组的 CheckBox
        foreach (var scriptGroup in ScriptGroups)
        {
            if (scriptGroup.Config.PathingConfig.HideOnRepeat)
            {
                continue;
            }
            var checkBox = new CheckBox
            {
                Content = scriptGroup.Name,
                Tag = scriptGroup
            };
            checkBoxes[scriptGroup] = checkBox;
            stackPanel.Children.Add(checkBox);

            checkBox.Checked += (s, e) => UpdateSelectAllCheckBoxState();
            checkBox.Unchecked += (s, e) => UpdateSelectAllCheckBoxState();
        }

        void UpdateSelectAllCheckBoxState()
        {
            int checkedCount = checkBoxes.Values.Count(cb => cb.IsChecked == true);
            if (checkedCount == 0)
            {
                selectAllCheckBox.IsChecked = false;
            }
            else if (checkedCount == checkBoxes.Count)
            {
                selectAllCheckBox.IsChecked = true;
            }
            else
            {
                selectAllCheckBox.IsChecked = null;
            }
        }

        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "选择需要执行的配置组",
            Content = new ScrollViewer
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 300 // 设置固定高度
            },
            CloseButtonText = "关闭",
            PrimaryButtonText = "确认执行",
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = await uiMessageBox.ShowDialogAsync();
        if (result == MessageBoxResult.Primary)
        {
            var selectedGroups = checkBoxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToList();

            if (selectedGroups.Count == 0)
            {
                _snackbarService.Show(
                    "未选择配置组",
                    "请至少选择一个配置组进行执行",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(3)
                );
                return;
            }
            await StartGroups(selectedGroups, null, loopCheckBox.IsChecked ?? false);
        }
    }

    private void SelectAllCheckBox_Indeterminate(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    public async Task OnStartMultiScriptGroupWithNamesAsync(params string[] names)
    {
        if (ScriptGroups.Count == 0)
        {
            ReadScriptGroup();
        }
        List<ScriptGroup> scriptGroups = new List<ScriptGroup>();
        foreach (var name in names)
        {
            try
            {
                var group = ScriptGroups.First(x => x.Name == name);
                scriptGroups.Add(group);
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning("传入的配置组名称不存在:{Name}", name);
            }
        }

        if (scriptGroups.Count > 0)
        {
            await StartGroups(scriptGroups);
        }
        else
        {
            _logger.LogWarning("需要执行的配置组为空");
        }
    }

    public async Task StartGroups(List<ScriptGroup> scriptGroups, TaskProgress? taskProgress = null, bool loop = false)
    {
        _logger.LogInformation("开始连续执行选中配置组:{Names}", string.Join(",", scriptGroups.Select(x => x.Name)));
        try
        {
            RunnerContext.Instance.IsContinuousRunGroup = true;
            if (taskProgress == null)
            {
                taskProgress = new()
                {
                    ScriptGroupNames = scriptGroups.Select(x => x.Name).ToList()
                    ,
                    Loop = loop
                };
            }

            RunnerContext.Instance.taskProgress = taskProgress;
            var sg = GetNextScriptGroups(scriptGroups);
            foreach (var scriptGroup in sg)
            {
                if (taskProgress.Next != null)
                {
                    if (scriptGroup.Name != taskProgress.Next.GroupName)
                    {
                        continue;
                    }
                }
                taskProgress.CurrentScriptGroupName = scriptGroup.Name;
                TaskProgressManager.SaveTaskProgress(taskProgress);
                await _scriptService.RunMulti(GetNextProjects(scriptGroup), scriptGroup.Name, taskProgress);
                await Task.Delay(2000);
            }

            taskProgress.LoopCount++;
            if (taskProgress is { Loop: true })
            {
                taskProgress.LastScriptGroupName = null;
                taskProgress.LastSuccessScriptGroupProjectInfo = null;
                taskProgress.Next = null;
                await StartGroups(scriptGroups, taskProgress);
            }
            else
            {
                //只有最后一次成功才算
                if (taskProgress.ConsecutiveFailureCount == 0)
                {
                    taskProgress.EndTime = DateTime.Now;
                    TaskProgressManager.SaveTaskProgress(taskProgress);
                }

            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
        }
        finally
        {
            RunnerContext.Instance.Reset();
        }


    }
}
