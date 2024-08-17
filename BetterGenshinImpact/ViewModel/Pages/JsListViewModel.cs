using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Project;
using Wpf.Ui;
using Wpf.Ui.Controls;
using static Vanara.PInvoke.Gdi32;
using Wpf.Ui.Violeta.Controls;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.GameTask;
using System.Threading.Tasks;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class JsListViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<JsListViewModel> _logger = App.GetLogger<JsListViewModel>();
    private readonly string scriptPath = Global.Absolute("Script");

    [ObservableProperty]
    private ObservableCollection<ScriptProject> _scriptItems = [];

    private ISnackbarService _snackbarService;

    public JsListViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
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
        if (item == null)
        {
            return;
        }
        Process.Start("explorer.exe", item.ProjectPath);
    }

    [RelayCommand]
    public async Task OnStartRun(string name)
    {
        _logger.LogInformation("重放开始：{Name}", name);
        try
        {
            // var s = await File.ReadAllTextAsync(Path.Combine(scriptPath, name));
            //
            // await new TaskRunner(DispatcherTimerOperationEnum.UseSelfCaptureImage)
            //     .RunAsync(async () => await KeyMouseMacroPlayer.PlayMacro(s, CancellationContext.Instance.Cts.Token));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "重放脚本时发生异常");
        }
        finally
        {
            _logger.LogInformation("重放结束：{Name}", name);
        }
    }
}
