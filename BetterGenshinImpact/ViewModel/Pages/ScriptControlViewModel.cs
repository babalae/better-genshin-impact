using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Editable;
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.GameTask;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using StackPanel = Wpf.Ui.Controls.StackPanel;
using System.Windows.Navigation;
using Newtonsoft.Json.Linq;
using static Vanara.PInvoke.User32;
using TextBox = Wpf.Ui.Controls.TextBox;
using BetterGenshinImpact.ViewModel.Pages.View;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
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
    private ScriptGroup? _selectedScriptGroup;

    public readonly string ScriptGroupPath = Global.Absolute(@"User\ScriptGroup");

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
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
        var str = PromptDialog.Prompt("请输入配置组名称", "新增配置组");
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
        SelectedScriptGroup.Projects.Clear();
        WriteScriptGroup(SelectedScriptGroup);
    }
    private void UpdateTasks()
    {
        //PromptDialog.Prompt
       // SelectedScriptGroup.Projects.Clear();
       // WriteScriptGroup(SelectedScriptGroup);
    }
    
    [RelayCommand]
    public void OnRenameScriptGroup(ScriptGroup? item)
    {
        if (item == null)
        {
            return;
        }

        var str = PromptDialog.Prompt("请输入配置组名称", "重命名配置组", item.Name);
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
        var combobox = new ComboBox();

        foreach (var scriptProject in list)
        {
            combobox.Items.Add(scriptProject.FolderName + " - " + scriptProject.Manifest.Name);
        }

        var str = PromptDialog.Prompt("请选择需要添加的JS脚本", "请选择需要添加的JS脚本", combobox);
        if (!string.IsNullOrEmpty(str))
        {
            var folderName = str.Split(" - ")[0];
            SelectedScriptGroup?.AddProject(new ScriptGroupProject(new ScriptProject(folderName)));
        }
    }

    [RelayCommand]
    private void OnAddKmScript()
    {
        var list = LoadAllKmScripts();
        var combobox = new ComboBox();

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
    private void OnAddPathing()
    {
        var root = FileTreeNodeHelper.LoadDirectory<PathingTask>(MapPathingViewModel.PathJsonPath);
        var stackPanel = CreatePathingScriptSelectionPanel(root.Children);

        var result = PromptDialog.Prompt("请选择需要添加的路径追踪任务", "请选择需要添加的路径追踪任务", stackPanel, new Size(500, 600));
        if (!string.IsNullOrEmpty(result))
        {
            AddSelectedPathingScripts((StackPanel)stackPanel.Content);
        }
    }

    private ScrollViewer CreatePathingScriptSelectionPanel(IEnumerable<FileTreeNode<PathingTask>> list)
    {
        var stackPanel = new StackPanel();
        var filterTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            PlaceholderText = "输入筛选条件..."
        };
        filterTextBox.TextChanged += (s, e) => ApplyFilter(stackPanel, list, filterTextBox.Text);
        stackPanel.Children.Add(filterTextBox);
        AddNodesToPanel(stackPanel, list, 0, filterTextBox.Text);

        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 435 // 固定高度
        };

        return scrollViewer;
    }

    private void ApplyFilter(StackPanel parentPanel, IEnumerable<FileTreeNode<PathingTask>> nodes, string filter)
    {
        if (parentPanel.Children.Count > 0 && parentPanel.Children[0] is TextBox filterTextBox)
        {
            parentPanel.Children.Clear();
            parentPanel.Children.Add(filterTextBox); // 保留筛选框
            AddNodesToPanel(parentPanel, nodes, 0, filter);
        }
    }

    private void AddNodesToPanel(StackPanel parentPanel, IEnumerable<FileTreeNode<PathingTask>> nodes, int depth, string filter)
    {
        foreach (var node in nodes)
        {
            if (depth == 0 && !string.IsNullOrEmpty(filter) && !node.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var checkBox = new CheckBox
            {
                Content = node.FileName,
                Tag = node.FilePath,
                Margin = new Thickness(depth * 30, 0, 0, 0) // 根据深度计算Margin
            };

            if (node.IsDirectory)
            {
                var childPanel = new StackPanel();
                AddNodesToPanel(childPanel, node.Children, depth + 1, filter);

                var expander = new Expander
                {
                    Header = checkBox,
                    Content = childPanel,
                    IsExpanded = false // 默认不展开
                };

                checkBox.Checked += (s, e) => SetChildCheckBoxesState(childPanel, true);
                checkBox.Unchecked += (s, e) => SetChildCheckBoxesState(childPanel, false);

                parentPanel.Children.Add(expander);
            }
            else
            {
                parentPanel.Children.Add(checkBox);
            }
        }
    }

    private void SetChildCheckBoxesState(StackPanel childStackPanel, bool state)
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

    private List<ScriptProject> LoadAllJsScriptProjects()
    {
        var path = Global.ScriptPath();
        // 获取所有脚本项目
        var projects = Directory.GetDirectories(path)
            .Select(x => new ScriptProject(Path.GetFileName(x)))
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
        if (item == null)
        {
            return;
        }

        List<ValueTuple<string, int, string, string>> nextScheduledTask = TaskContext.Instance().Config.NextScheduledTask;
        var nst = nextScheduledTask.Find(item2 => item2.Item1 == SelectedScriptGroup?.Name);
        if (nst != default)
        {
            nextScheduledTask.Remove(nst);
        }

        nextScheduledTask.Add((SelectedScriptGroup?.Name, item.Index, item.FolderName, item.Name));
        foreach (var item1 in SelectedScriptGroup.Projects)
        {
            item1.NextFlag = false;
        }

        item.NextFlag = true;
    }

    public static void ShowEditWindow(object viewModel)
    {
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "修改通用设置",
            Content = new ScriptGroupProjectEditor { DataContext = viewModel },
            CloseButtonText = "关闭",
            Owner = Application.Current.MainWindow,
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
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/feats/autos/dispatcher.html") { UseShellExecute = true });
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
        await _scriptService.RunMulti(GetNextProjects(SelectedScriptGroup), SelectedScriptGroup.Name);
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

    public static List<ScriptGroupProject> GetNextProjects(ScriptGroup group)
    {
        List<ScriptGroupProject> ls = new List<ScriptGroupProject>();
        bool start = false;
        foreach (var item in group.Projects)
        {
            if (item.NextFlag ?? false)
            {
                start = true;
            }

            if (start)
            {
                ls.Add(item);
            }
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

    [RelayCommand]
    public async Task OnStartMultiScriptGroupAsync()
    {
        // 创建一个 StackPanel 来包含全选按钮和所有配置组的 CheckBox
        var stackPanel = new StackPanel();
        var checkBoxes = new Dictionary<ScriptGroup, CheckBox>();

        // 创建全选按钮
        var selectAllCheckBox = new CheckBox
        {
            Content = "全选",
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
            var checkBox = new CheckBox
            {
                Content = scriptGroup.Name,
                Tag = scriptGroup
            };
            checkBoxes[scriptGroup] = checkBox;
            stackPanel.Children.Add(checkBox);
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
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var result = await uiMessageBox.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var selectedGroups = checkBoxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToList();

            _logger.LogInformation("开始连续执行选中配置组:{Names}", string.Join(",", selectedGroups.Select(x => x.Name)));

            RunnerContext.Instance.IsContinuousRunGroup = true;
            foreach (var scriptGroup in selectedGroups)
            {
                await _scriptService.RunMulti(GetNextProjects(scriptGroup), scriptGroup.Name);
                await Task.Delay(2000);
            }

            RunnerContext.Instance.Reset();
        }
    }
}