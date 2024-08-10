using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class ScriptControlViewModel : ObservableObject, INavigationAware, IViewModel
{
    private ISnackbarService _snackbarService;

    /// <summary>
    /// 脚本组配置
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ScriptGroup> _scriptGroups = [];

    /// <summary>
    /// 当前选中的脚本组
    /// </summary>
    [ObservableProperty]
    private ScriptGroup? _selectedScriptGroup;

    public void OnNavigatedFrom()
    {
    }

    public void OnNavigatedTo()
    {
    }

    public ScriptControlViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
        ScriptGroups.CollectionChanged += ScriptGroupsCollectionChanged;
    }

    [RelayCommand]
    private void OnAddScriptGroup()
    {
        var str = PromptDialog.Prompt("请输入脚本组名称", "新增脚本组");
        if (!string.IsNullOrEmpty(str))
        {
            ScriptGroups.Add(new ScriptGroup { Name = str });
        }
    }

    [RelayCommand]
    public void OnDeleteScriptGroup(ScriptGroup? item)
    {
        if (item == null)
        {
            return;
        }

        ScriptGroups.Remove(item);
        _snackbarService.Show(
            "脚本组删除成功",
            $"{item.Name} 已经被删除",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2)
        );
    }

    [RelayCommand]
    private void OnAddScript()
    {
        var list = LoadAllScriptProjects();
        var combobox = new ComboBox();

        foreach (var scriptProject in list)
        {
            combobox.Items.Add(scriptProject.FolderName + " - " + scriptProject.Manifest.Name);
        }

        var str = PromptDialog.Prompt("请选择需要添加的脚本", "请选择需要添加的脚本", combobox);
        if (!string.IsNullOrEmpty(str))
        {
            var folderName = str.Split(" - ")[0];
            SelectedScriptGroup?.Projects.Add(new ScriptGroupProject(new ScriptProject(folderName)));
        }
    }

    private List<ScriptProject> LoadAllScriptProjects()
    {
        var path = Global.ScriptPath();
        // 获取所有脚本项目
        var projects = Directory.GetDirectories(path)
            .Select(x => new ScriptProject(Path.GetFileName(x)))
            .ToList();
        return projects;
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
        Debug.WriteLine("ScriptGroupsCollectionChanged");
    }

    private void ScriptProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 补充排序字段
        if (SelectedScriptGroup is { Projects.Count: > 0 })
        {
            var i = 1;
            foreach (var project in SelectedScriptGroup.Projects)
            {
                project.Order = i++;
            }
        }
        Debug.WriteLine("---ScriptProjectsCollectionChanged");
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
        Debug.WriteLine(scriptGroupExample);
    }
}
