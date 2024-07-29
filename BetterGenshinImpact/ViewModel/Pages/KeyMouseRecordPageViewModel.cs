using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyMouseRecordPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<KeyMouseRecordPageViewModel> _logger = App.GetLogger<KeyMouseRecordPageViewModel>();
    private readonly string scriptPath = Global.Absolute(@"User\KeyMouseScript");

    [ObservableProperty]
    private ObservableCollection<KeyMouseScriptItem> _scriptItems = [];

    [ObservableProperty]
    private bool _isRecording = false;

    private ISnackbarService _snackbarService;

    public KeyMouseRecordPageViewModel(ISnackbarService snackbarService)
    {
        _snackbarService = snackbarService;
    }

    private void InitScriptListViewData()
    {
        _scriptItems.Clear();
        var fileInfos = LoadScriptFiles(scriptPath);
        fileInfos = fileInfos.OrderByDescending(f => f.CreationTime).ToList();
        foreach (var f in fileInfos)
        {
            _scriptItems.Add(new KeyMouseScriptItem
            {
                Name = f.Name,
                CreateTime = f.CreationTime,
                CreateTimeStr = f.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }

    private List<FileInfo> LoadScriptFiles(string folder)
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
    public async Task OnStartRecord()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            MessageBox.Show("请先在启动页，启动截图器再使用本功能");
            return;
        }
        if (!IsRecording)
        {
            IsRecording = true;
            await GlobalKeyMouseRecord.Instance.StartRecord();
        }
    }

    [RelayCommand]
    public void OnStopRecord()
    {
        if (IsRecording)
        {
            try
            {
                var macro = GlobalKeyMouseRecord.Instance.StopRecord();
                // Genshin Copilot Macro
                File.WriteAllText(Path.Combine(scriptPath, $"BetterGI_GCM_{DateTime.Now:yyyyMMddHHmmssffff}.json"), macro);
                // 刷新ListView
                InitScriptListViewData();
                IsRecording = false;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "停止录制时发生异常");
                _logger.LogWarning(e.Message);
            }
        }
    }

    [RelayCommand]
    public async Task OnStartPlay(string name)
    {
        _logger.LogInformation("重放开始：{Name}", name);
        try
        {
            var s = await File.ReadAllTextAsync(Path.Combine(scriptPath, name));

            await new TaskRunner(DispatcherTimerOperationEnum.UseCacheImage)
                .RunAsync(async () => await KeyMouseMacroPlayer.PlayMacro(s, CancellationContext.Instance.Cts.Token));
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

    [RelayCommand]
    public void OnOpenScriptFolder()
    {
        Process.Start("explorer.exe", scriptPath);
    }

    [RelayCommand]
    public void OnDeleteScript(KeyMouseScriptItem item)
    {
        try
        {
            File.Delete(Path.Combine(scriptPath, item.Name));
            _snackbarService.Show(
                "删除成功",
                $"{item.Name} 已经被删除",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2)
            );
        }
        catch (Exception e)
        {
            _snackbarService.Show(
                "删除失败",
                $"{item.Name} 删除失败",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3)
            );
        }
        finally
        {
            InitScriptListViewData();
        }
    }
}
