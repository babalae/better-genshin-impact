﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Core.Script.Project;
using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class JsListViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<JsListViewModel> _logger = App.GetLogger<JsListViewModel>();
    private readonly string scriptPath = Global.ScriptPath();

    [ObservableProperty]
    private ObservableCollection<ScriptProject> _scriptItems = [];

    private ISnackbarService _snackbarService;
    private IScriptService _scriptService;

    public AllConfig Config { get; set; }

    public JsListViewModel(ISnackbarService snackbarService, IScriptService scriptService, IConfigService configService)
    {
        _snackbarService = snackbarService;
        _scriptService = scriptService;
        Config = configService.Get();
    }

    private void InitScriptListViewData()
    {
        _scriptItems.Clear();
        var directoryInfos = LoadScriptFolder(scriptPath);
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
        Process.Start("explorer.exe", scriptPath);
    }

    [RelayCommand]
    public void OnOpenScriptProjectFolder(ScriptProject? item)
    {
        Process.Start("explorer.exe", item == null ? scriptPath : item.ProjectPath);
    }

    [RelayCommand]
    public async Task OnStartRun(ScriptProject? item)
    {
        if (item == null)
        {
            return;
        }
        await _scriptService.RunMulti([new ScriptGroupProject(item)]);
    }

    [RelayCommand]
    public void OnRefresh(ScriptProject? item)
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public void OnGoToJsScriptUrl()
    {
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/autos/jsscript.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
    }
}
