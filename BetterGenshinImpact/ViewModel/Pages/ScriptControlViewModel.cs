using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
        ScriptGroups.Add(new ScriptGroup { Name = new Random().Next(100, 1000).ToString() });
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
        SelectedScriptGroup?.Projects.Add(new ScriptGroupProject(1, new ScriptProject("AutoCrystalfly")));
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
            "脚本配置删除成功",
            $"{item.Name} 已经被删除",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2)
        );
    }

    private void ScriptGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // if (e.NewItems != null)
        // {
        //     foreach (ScriptGroup newItem in e.NewItems)
        //     {
        //         newItem.PropertyChanged += ScriptGroupPropertyChanged;
        //     }
        // }
        //
        // if (e.OldItems != null)
        // {
        //     foreach (ScriptGroup oldItem in e.OldItems)
        //     {
        //         oldItem.PropertyChanged -= ScriptGroupPropertyChanged;
        //     }
        // }

        Debug.WriteLine("ScriptGroupsCollectionChanged");
    }

    private void ScriptGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Debug.WriteLine($"ScriptGroupPropertyChanged: {e.PropertyName}");
    }
}
