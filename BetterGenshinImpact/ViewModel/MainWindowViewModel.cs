﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.GameCapture.BitBlt;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel;

public partial class MainWindowViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigService _configService;
    public string Title => $"BetterGI · 更好的原神 · {Global.Version}{(RuntimeHelper.IsDebug ? " · Dev" : string.Empty)}";

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private WindowState _windowState = WindowState.Normal;

    [ObservableProperty]
    private WindowBackdropType _currentBackdropType = WindowBackdropType.Auto;

    [ObservableProperty]
    private bool _isWin11Later = OsVersionHelper.IsWindows11_OrGreater;

    public AllConfig Config { get; set; }

    public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
    {
        _configService = configService;
        Config = _configService.Get();
        _logger = App.GetLogger<MainWindowViewModel>();
    }

    [RelayCommand]
    private async Task OnActivated()
    {
        await ScriptRepoUpdater.Instance.ImportScriptFromClipboard();
    }

    [RelayCommand]
    private void OnHide()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void OnSwitchBackdrop()
    {
        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            return; // win10 不支持切换主题
        }

        CurrentBackdropType = CurrentBackdropType switch
        {
            WindowBackdropType.Mica => WindowBackdropType.Acrylic,
            WindowBackdropType.Acrylic => WindowBackdropType.Mica,
            _ => WindowBackdropType.Acrylic
        };

        Config.CommonConfig.CurrentBackdropType = CurrentBackdropType;

        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            WindowBackdrop.ApplyBackdrop(mainWindow, CurrentBackdropType);
        }
    }

    [RelayCommand]
    private void OnClosing(CancelEventArgs e)
    {
        if (Config.CommonConfig.ExitToTray)
        {
            e.Cancel = true;
            OnHide();
        }
    }

    [RelayCommand]
    private async Task OnLoaded()
    {
        // 预热OCR
        await OcrPreheating();

        if (Environment.GetCommandLineArgs().Length > 1)
        {
            return;
        }

        // 自动处理目录配置
        await Patch1();
        

        // 首次运行
        if (Config.CommonConfig.IsFirstRun)
        {
            // 自动初始化键位绑定
            // InitKeyBinding();
            Config.AutoFightConfig.TeamNames = ""; // 此配置以后无用
            Config.CommonConfig.IsFirstRun = false;

        }
        // 版本是否运行过
        if (Config.CommonConfig.RunForVersion != Global.Version)
        {
            ModifyFolderSecurity();
            Config.CommonConfig.RunForVersion = Global.Version;
        }

        // 检查更新
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption());

        //  Win11下 BitBlt截图方式不可用，需要关闭窗口优化功能
        if (OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt)
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }

        // 更新仓库
        ScriptRepoUpdater.Instance.AutoUpdate();
        
        // 清理临时目录
        TempManager.CleanUp();
    }


    private void ModifyFolderSecurity()
    {
        // 检查程序是否位于C盘
        if (Global.StartUpPath.StartsWith(@"C:", StringComparison.OrdinalIgnoreCase))
        {
            // 修改文件夹权限
            SecurityControlHelper.AllowFullFolderSecurity(Global.StartUpPath);
        }
    }

    /*
    private void InitKeyBinding()
    {
        try
        {
            var kbVm = App.GetService<KeyBindingsSettingsPageViewModel>();
            if (kbVm != null)
            {
                kbVm.FetchFromRegistryCommand.Execute(null);
            }
        }
        catch (Exception e)
        {
            _logger.LogError("首次运行自动初始化按键绑定异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);

            MessageBox.Error("读取原神键位并设置键位绑定数据时发生异常：" + e.Message + "，后续可以手动设置");
        }
    }
    */

    /**
     * 不同的安装目录处理
     * 可能当前目录下存在 BetterGI 的文件，需要移动到新的目录
     */
    private async Task Patch1()
    {
        var embeddedPath = Global.Absolute("BetterGI");
        var embeddedUserPath = Global.Absolute("BetterGI/User");
        var exePath = Global.Absolute("BetterGI/BetterGI.exe");
        if (Directory.Exists(embeddedPath)
            && File.Exists(exePath)
            && Directory.Exists(embeddedUserPath)
           )
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
            // 低版本才需要迁移
            if (fileVersionInfo.FileVersion != null && !Global.IsNewVersion(fileVersionInfo.FileVersion))
            {
                var res = await MessageBox.ShowAsync("检测到旧的 BetterGI 配置，是否迁移配置并清理旧目录？", "BetterGI", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    // 迁移配置，拷贝整个目录并覆盖
                    DirectoryHelper.CopyDirectory(embeddedUserPath, Global.Absolute("User"));
                    // 删除旧目录
                    DirectoryHelper.DeleteReadOnlyDirectory(embeddedPath);
                    await MessageBox.InformationAsync("迁移配置成功, 软件将自动退出，请手动重新启动 BetterGI！");
                    Application.Current.Shutdown();
                }
            }
        }
    }

    private async Task OcrPreheating()
    {
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var s = OcrFactory.Paddle.Ocr(new Mat(Global.Absolute(@"Assets\Model\PaddleOCR\test_ocr.png"), ImreadModes.Grayscale));
                    Debug.WriteLine("PaddleOcr预热结果:" + s);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    _logger.LogError("PaddleOcr预热异常，解决方案：https://bettergi.com/faq.html：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                    var innerException = e.InnerException;
                    if (innerException != null)
                    {
                        _logger.LogError("PaddleOcr预热内部异常，解决方案：https://bettergi.com/faq.html：" + innerException.Source + "\r\n--" + Environment.NewLine + innerException.StackTrace + "\r\n---" + Environment.NewLine + innerException.Message);
                        throw innerException;
                    }
                    else
                    {
                        throw;
                    }
                }
            });
        }
        catch (Exception e)
        {
            MessageBox.Warning("PaddleOcr预热失败，解决方案：https://bettergi.com/faq.html，" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
    }
}