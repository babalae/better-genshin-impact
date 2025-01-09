using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Enum;
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
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Recorder.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Genshin.Settings2;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Device;
using BetterGenshinImpact.Helpers.Upload;
using Newtonsoft.Json;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using MessageBoxButton = Wpf.Ui.Controls.MessageBoxButton;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class KeyMouseRecordPageViewModel : ObservableObject, INavigationAware, IViewModel
{
    private readonly ILogger<KeyMouseRecordPageViewModel> _logger = App.GetLogger<KeyMouseRecordPageViewModel>();
    private readonly string scriptPath = Global.Absolute(@"User\KeyMouseScript");

    [ObservableProperty]
    private ObservableCollection<KeyMouseScriptItem> _scriptItems = [];

    [ObservableProperty]
    private bool _isRecording = false;

    private readonly ISnackbarService _snackbarService;

    public AllConfig Config { get; set; }

    string fileName = $"{DateTime.Now:yyyyMMddHH_mmssffff}";

    public KeyMouseRecordPageViewModel(ISnackbarService snackbarService, IConfigService configService)
    {
        _snackbarService = snackbarService;
        Config = configService.Get();
    }

    private void InitScriptListViewData()
    {
        _scriptItems.Clear();
        var directoryInfos = LoadScriptDirectories(scriptPath)
            .OrderByDescending(d => d.CreationTime)
            .ToList();
        foreach (var d in directoryInfos)
        {
            _scriptItems.Add(new KeyMouseScriptItem
            {
                Name = d.Name,
                Path = d.FullName,
                CreateTime = d.CreationTime,
                CreateTimeStr = d.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }

    private List<DirectoryInfo> LoadScriptDirectories(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var directories = Directory.GetDirectories(folder);

        return directories.Select(dir => new DirectoryInfo(dir)).ToList();
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
        var s = RecordContext.Instance.SysParams; // 提前实例化，避免延迟
        Debug.WriteLine(s.EnhancePointerPrecision);
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先在启动页，启动截图器再使用本功能");
            return;
        }

        if (TaskContext.Instance().Config.CommonConfig.ProcessCheckEnabled && !RuntimeHelper.IsDebug)
        {
            try
            {
                if (EnvironmentUtil.IsProcessRunning("QQ")
                    || EnvironmentUtil.IsProcessRunning("WeChat")
                    || EnvironmentUtil.IsProcessRunning("dingtalk")
                    || EnvironmentUtil.IsProcessRunning("Feishu"))
                {
                    throw new Exception("请关闭 QQ、微信、飞书等聊天软件再进行录制");
                }

                if (EnvironmentUtil.GetMasterVolume() <= 0)
                {
                    throw new Exception("请保持系统音量大于0再进行录制");
                }
            }
            catch (Exception e)
            {
                // await MessageBox.ShowAsync(e.Message, "校验错误", System.Windows.MessageBoxButton.OK, MessageBoxImage.Error);
                // SystemControl.ActivateWindow(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle);
                Toast.Error(e.Message);
                _logger.LogError(e.Message);
                return;
            }
        }


        fileName = $"{DateTime.Now:yyyy_MM_dd_HH_mm_ss}";

        await Task.Run(() =>
        {
            try
            {
                var pcFolder = Global.Absolute(@$"User/KeyMouseScript/{fileName}");
                Directory.CreateDirectory(pcFolder);
                // 移动PC信息
                var src = Global.Absolute(@$"User/pc.json");
                if (File.Exists(src))
                {
                    File.Copy(Global.Absolute(@$"User/pc.json"), Path.Combine(pcFolder, "pc.json"), true);
                }
            }
            catch (Exception e)
            {
                TaskControl.Logger.LogDebug("移动PC信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            }
        });


        if (!IsRecording)
        {
            IsRecording = true;
            SystemSettingsManager.GetSystemSettings();
            SystemSettingsManager.SetSystemSettings();

            try
            {
                await GlobalKeyMouseRecord.Instance.StartRecord(fileName);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "启动录制时发生异常");
                _logger.LogError(e.Message);
                IsRecording = false;
            }
        }
    }

    [RelayCommand]
    public void OnStopRecord()
    {
        if (IsRecording)
        {
            try
            {
                GlobalKeyMouseRecord.Instance.StopRecord();
                // Genshin Copilot Macro
                // File.WriteAllText(Path.Combine(scriptPath, $"{fileName}/{fileName}.json"), macro);
                // 刷新ListView
                InitScriptListViewData();
                IsRecording = false;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "停止录制时发生异常");
                _logger.LogError(e.Message);
            }

            SystemSettingsManager.RestoreSystemSettings();

            var pcFolder = Global.Absolute(@$"User/KeyMouseScript/{fileName}");

            // 结束时检查游戏设置
            GameSettingsChecker.LoadGameSettingsAndCheck(Path.Combine(pcFolder, "gameSettings.json"));

            Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(pcFolder);
                    // 移动PC信息
                    var src = Global.Absolute(@$"User/pc.json");
                    if (File.Exists(src))
                    {
                        File.Copy(Global.Absolute(@$"User/pc.json"), Path.Combine(pcFolder, "pc.json"), true);
                    }
                }
                catch (Exception e)
                {
                    TaskControl.Logger.LogDebug("移动PC信息失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                }

                Thread.Sleep(5000);
                for (int i = 0; i < 5; i++)
                {
                    Thread.Sleep(1000);
                    try
                    {
                        // 记录 pcFolder 目录下所有文件的 hash 信息到 hashFolder 目录下的 hash.json 文件
                        var hashFolder = Global.Absolute(@$"User/Common/Km/{fileName}");
                        Directory.CreateDirectory(hashFolder);
                        RecordFileHashes(pcFolder, hashFolder);
                        break;
                    }
                    catch (Exception e)
                    {
                        TaskControl.Logger.LogDebug("信息统计失败：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                        continue;
                    }
                }

            });
        }
    }


    public void RecordFileHashes(string pcFolder, string hashFolder)
    {
        var fileHashes = new Dictionary<string, string>();

        foreach (var filePath in Directory.GetFiles(pcFolder, "*.*", SearchOption.AllDirectories))
        {
            using (var stream = File.OpenRead(filePath))
            {
                var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(stream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                fileHashes[Path.GetFileName(filePath)] = hashString;
            }
        }

        var hashFilePath = Path.Combine(hashFolder, "hash.json");
        Directory.CreateDirectory(hashFolder);
        File.WriteAllText(hashFilePath, JsonConvert.SerializeObject(fileHashes, Formatting.Indented));
    }

    [RelayCommand]
    public async Task OnStartPlay(string path)
    {
        var file = new FileInfo(Path.Combine(path, "systemInfo.json"));
        var name = file.Directory?.Name;
        _logger.LogInformation("重放开始：{Name}", name);
        try
        {
            await new TaskRunner(DispatcherTimerOperationEnum.UseSelfCaptureImage)
                .RunAsync(async () => await KeyMouseMacroPlayerJsonLine.PlayMacro(file.FullName, CancellationContext.Instance.Cts.Token));
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
                var originalFilePath = Path.Combine(scriptPath, item.Name);
                if (File.Exists(originalFilePath))
                {
                    // 重命名文件
                    File.Move(originalFilePath, Path.Combine(scriptPath, str + ".json"));
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
            var path = item.Path;
            // 校验文件夹是否在 scriptPath 下
            if (!path.StartsWith(scriptPath))
            {
                _snackbarService.Show(
                    "删除失败",
                    $"{path} 删除失败，不在脚本目录下",
                    ControlAppearance.Danger,
                    null,
                    TimeSpan.FromSeconds(3)
                );
                return;
            }

            // 删除目录
            Directory.Delete(path, true);

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
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/feats/autos/kmscript.html") { UseShellExecute = true });
    }

    [RelayCommand]
    public void OnOpenLocalScriptRepo()
    {
        Config.ScriptConfig.ScriptRepoHintDotVisible = false;
        ScriptRepoUpdater.Instance.OpenLocalRepoInWebView();
    }

    public bool VerifyFileHashes(string pcFolder, string hashFolder)
    {
        var hashFilePath = Path.Combine(hashFolder, "hash.json");
        if (!File.Exists(hashFilePath))
        {
            // 无文件hash信息，直接返回true
            _logger.LogDebug("Hash file not found");
            return true;
        }

        var storedHashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(hashFilePath));
        if (storedHashes == null)
        {
            throw new InvalidOperationException("Failed to deserialize hash file");
        }

        foreach (var filePath in Directory.GetFiles(pcFolder, "*.*", SearchOption.AllDirectories))
        {
            using (var stream = File.OpenRead(filePath))
            {
                if (!storedHashes.TryGetValue(Path.GetFileName(filePath), out var storedHash))
                {
                    Debug.WriteLine($"Hash not found for {filePath}");
                    continue;
                }

                var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(stream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                if (storedHash != hashString)
                {
                    Debug.WriteLine($"Hash mismatch for {filePath}");
                    return false;
                }
            }
        }

        return true;
    }
}