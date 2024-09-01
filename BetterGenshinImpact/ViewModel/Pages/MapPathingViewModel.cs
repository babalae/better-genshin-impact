using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<MapPathingViewModel> _logger = App.GetLogger<MapPathingViewModel>();
    private readonly string jsonPath = Global.Absolute("AutoPathing");

    [ObservableProperty]
    private ObservableCollection<PathingTask> _pathItems = [];

    private void InitScriptListViewData()
    {
        _pathItems.Clear();
        var fileInfos = LoadScriptFolder(jsonPath);
        foreach (var f in fileInfos)
        {
            try
            {
                _pathItems.Add(PathingTask.BuildFromFilePath(f.FullName));
            }
            catch (Exception e)
            {
                Toast.Warning($"地图追踪任务 {f.Name} 载入失败：{e.Message}");
            }
        }
    }

    private IEnumerable<FileInfo> LoadScriptFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var files = Directory.GetFiles(folder, "*.*",
            SearchOption.AllDirectories);

        return files.Select(file => new FileInfo(file)).ToList();
    }

    public void OnNavigatedTo()
    {
        InitScriptListViewData();
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnOpenScriptsFolder()
    {
        if (!Directory.Exists(jsonPath))
        {
            Directory.CreateDirectory(jsonPath);
        }
        Process.Start("explorer.exe", jsonPath);
    }

    [RelayCommand]
    public void OnOpenScriptProjectFolder(ScriptProject? item)
    {
        if (item == null)
        {
            return;
        }
        Process.Start("explorer.exe", item.ProjectPath);
    }

    // [RelayCommand]
    // public async Task OnStartRun(ScriptProject? item)
    // {
    //     if (item == null)
    //     {
    //         return;
    //     }
    //     await _scriptService.RunMulti([item.FolderName]);
    // }
}
