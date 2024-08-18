using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Editable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ISnackbarService _snackbarService;

    private readonly ILogger<ScriptControlViewModel> _logger = App.GetLogger<ScriptControlViewModel>();

    private readonly HomePageViewModel _homePageViewModel;

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

    public ScriptControlViewModel(ISnackbarService snackbarService, IScriptService scriptService, HomePageViewModel homePageViewModel)
    {
        _snackbarService = snackbarService;
        _scriptService = scriptService;
        _homePageViewModel = homePageViewModel;
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
            SelectedScriptGroup?.Projects.Add(new ScriptGroupProject(new ScriptProject(folderName)));
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
            SelectedScriptGroup?.Projects.Add(new ScriptGroupProject(str));
        }
    }

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

        foreach (var group in ScriptGroups)
        {
            WriteScriptGroup(group);
        }
    }

    public static void ShowEditWindow(object viewModel)
    {
        var uiMessageBox = new MessageBox
        {
            Title = "修改通用设置",
            Content = new ScriptGroupProjectEditor { DataContext = viewModel },
            CloseButtonText = "确定",
        };
        uiMessageBox.ShowDialogAsync();
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
            }
        }

        if (e.OldItems != null)
        {
            foreach (ScriptGroup oldItem in e.OldItems)
            {
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
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnGoToScriptProjectUrl()
    {
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnImportScriptGroup(string scriptGroupExample)
    {
        ScriptGroup group = new();
        if ("AutoCrystalflyExampleGroup" == scriptGroupExample)
        {
            group.Name = "晶蝶示例组";
            group.Projects.Add(new ScriptGroupProject(new ScriptProject("AutoCrystalfly")));
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

        await _scriptService.RunMulti(SelectedScriptGroup.Projects, SelectedScriptGroup.Name);
    }
}
