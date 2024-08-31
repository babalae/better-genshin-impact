using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Project;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MapPathingViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<JsListViewModel> _logger = App.GetLogger<JsListViewModel>();
    private readonly string jsonPath = Global.Absolute("AutoPathing");

    [ObservableProperty]
    private ObservableCollection<ScriptProject> _scriptItems = [];

    private void InitScriptListViewData()
    {
        _scriptItems.Clear();
        var directoryInfos = LoadScriptFolder(jsonPath);
        foreach (var f in directoryInfos)
        {
            try
            {
                _scriptItems.Add(new ScriptProject(f.Name));
            }
            catch (Exception e)
            {
                Toast.Warning($"脚本 {f.Name} 载入失败：{e.Message}");
            }
        }
    }

    private IEnumerable<DirectoryInfo> LoadScriptFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var di = new DirectoryInfo(folder);

        return di.GetDirectories();
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
