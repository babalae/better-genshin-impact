using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyMouseRecordPageViewModel : ViewModel
{
    private readonly ILogger<KeyMouseRecordPageViewModel> _logger = App.GetLogger<KeyMouseRecordPageViewModel>();
    private readonly string scriptPath = Global.Absolute(@"User\KeyMouseScript");

    [ObservableProperty]
    private ObservableCollection<KeyMouseScriptItem> _scriptItems = [];

    [ObservableProperty]
    private bool _isRecording = false;

    private readonly ISnackbarService _snackbarService;

    public AllConfig Config { get; set; }

    public KeyMouseRecordPageViewModel(ISnackbarService snackbarService, IConfigService configService)
    {
        _snackbarService = snackbarService;
        Config = configService.Get();
    }

    private void InitScriptListViewData()
    {
        ScriptItems.Clear();
        var fileInfos = LoadScriptFiles(scriptPath)
            .OrderByDescending(f => f.CreationTime)
            .ToList();
        foreach (var f in fileInfos)
        {
            ScriptItems.Add(new KeyMouseScriptItem
            {
                Name = f.Name,
                Path = f.FullName,
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

        var files = Directory.GetFiles(folder, "*.json",
            SearchOption.AllDirectories);

        return files.Select(file => new FileInfo(file)).ToList();
    }

    public override void OnNavigatedTo()
    {
        InitScriptListViewData();
    }

    [RelayCommand]
    public async Task OnStartRecord()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
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
    public async Task OnStartPlay(string path)
    {
        string name = Path.GetFileName(path);
        _logger.LogInformation("重放开始：{Name}", name);
        try
        {
            var s = await File.ReadAllTextAsync(path);

            await new TaskRunner()
                .RunThreadAsync(async () => await KeyMouseMacroPlayer.PlayMacro(s, CancellationContext.Instance.Cts.Token));
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
    public void OnEditScript(KeyMouseScriptItem? item)
    {
        if (item == null)
        {
            return;
        }
        try
        {
            var str = PromptDialog.Prompt("请输入要修改为的名称（实际就是文件名）", "修改名称");
            if (!string.IsNullOrEmpty(str))
            {
                // 检查原始文件是否存在
                var originalFilePath = item.Path;
                if (File.Exists(originalFilePath))
                {
                    // 重命名文件
                    File.Move(originalFilePath, Path.Combine(Path.GetDirectoryName(originalFilePath)!, str + ".json"));
                    _snackbarService.Show(
                        "修改名称成功",
                        $"脚本名称 {item.Name} 修改为 {str}",
                        ControlAppearance.Success,
                        null,
                        TimeSpan.FromSeconds(2)
                    );
                }
            }
        }
        catch (Exception)
        {
            _snackbarService.Show(
                "修改失败",
                $"{item.Name} 修改失败",
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

    [RelayCommand]
    public void OnDeleteScript(KeyMouseScriptItem? item)
    {
        if (item == null)
        {
            return;
        }
        try
        {
            File.Delete(item.Path);
            _snackbarService.Show(
                "删除成功",
                $"{item.Name} 已经被删除",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2)
            );
        }
        catch (Exception)
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

    [RelayCommand]
    public void OnGoToKmScriptUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/autos/kmscript.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenScriptRepoWindow();
    }
}
