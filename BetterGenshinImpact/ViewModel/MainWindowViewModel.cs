using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeviceId;
using Fischless.GameCapture.BitBlt;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using BetterGenshinImpact.Helpers.Http;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel;

public partial class MainWindowViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigService _configService;
    private readonly INavigationService _navigationService;
    public string Title => $"BetterGI · 更好的原神 · {Global.Version}{(RuntimeHelper.IsDebug ? " · Dev" : string.Empty)}";

    [ObservableProperty] private bool _isVisible = true;

    [ObservableProperty] private WindowState _windowState = WindowState.Normal;

    [ObservableProperty] private WindowBackdropType _currentBackdropType = WindowBackdropType.Auto;

    [ObservableProperty] private bool _isWin11Later = OsVersionHelper.IsWindows11_OrGreater;
    
    [ObservableProperty] private Brush _redeemCodeButtonForeground = Brushes.White;
    
    private string? _redeemCodeUpdateNewVersion;

    private bool _firstActivated = true;

    public AllConfig Config { get; set; }

    public MainWindowViewModel(INavigationService navigationService, IConfigService configService)
    {
        _navigationService = navigationService;
        _configService = configService;
        Config = _configService.Get();
        _logger = App.GetLogger<MainWindowViewModel>();
    }

    [RelayCommand]
    private async Task OnActivated()
    {
        // 首次激活时不处理
        if (_firstActivated)
        {
            _firstActivated = false;
            return;
        }

        // 激活时候获取剪切板内容 用于脚本导入、兑换码自动兑换等
        try
        {
            if (Clipboard.ContainsText())
            {
                string clipboardText = Clipboard.GetText();

                if (string.IsNullOrEmpty(clipboardText)
                    || clipboardText.Length > 1000)
                {
                    return;
                }


                // 1. 导入脚本
                await ScriptRepoUpdater.Instance.ImportScriptFromClipboard(clipboardText);
                // 2. 自动兑换码
                await RedeemCodeManager.ImportFromClipboard(clipboardText);
            }
        }
        catch
        {
            // 忽略异常，可能是因为没有权限访问剪切板
        }
    }

    [RelayCommand]
    private void OnHide()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void OnSwitchBackdrop()
    {
        // Windows11_22523以下版本支持基本深浅主题切换，以上版本额外还支持Mica/Acrylic主题切换
        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            Config.CommonConfig.CurrentThemeType = Config.CommonConfig.CurrentThemeType switch
            {
                ThemeType.DarkNone => ThemeType.LightNone,
                ThemeType.LightNone => ThemeType.DarkNone,
                _ => ThemeType.DarkNone
            };
        }
        else
        {
            Config.CommonConfig.CurrentThemeType = Config.CommonConfig.CurrentThemeType switch
            {
                ThemeType.DarkMica => ThemeType.DarkAcrylic,
                ThemeType.DarkAcrylic => ThemeType.LightMica,
                ThemeType.LightMica => ThemeType.LightAcrylic,
                ThemeType.LightAcrylic => ThemeType.DarkMica,
                _ => ThemeType.DarkMica
            };
        }

        ApplyTheme(Config.CommonConfig.CurrentThemeType);
        _configService.Save();
    }

    private void ApplyTheme(ThemeType themeType)
    {
        var originalThemeType = themeType;

        // 根据主题类型设置应用程序主题（深色/浅色）和背景效果类型（Mica/Acrylic/None）
        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            // 22523以下版本只支持深浅色切换,修正背景材质为纯色
            if (themeType == ThemeType.DarkMica || themeType == ThemeType.DarkAcrylic)
            {
                themeType = ThemeType.DarkNone;
            }
            else if (themeType == ThemeType.LightMica || themeType == ThemeType.LightAcrylic)
            {
                themeType = ThemeType.LightNone;
            }
        }

        // 如果主题类型被修正，更新配置并保存
        if (themeType != originalThemeType)
        {
            Config.CommonConfig.CurrentThemeType = themeType;
            _configService.Save();
            _logger.LogInformation($"主题类型已从 {originalThemeType} 修正为 {themeType}，因为当前系统不支持该主题效果");
        }

        switch (themeType)
        {
            case ThemeType.DarkNone:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.None;
                break;
            case ThemeType.DarkMica:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.Mica;
                break;
            case ThemeType.DarkAcrylic:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.Acrylic;
                break;
            case ThemeType.LightNone:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.None;
                break;
            case ThemeType.LightMica:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.Mica;
                break;
            case ThemeType.LightAcrylic:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.Acrylic;
                break;
        }

        // 立即应用主题到当前窗口
        if (Application.Current.MainWindow != null)
        {
            WindowHelper.ApplyThemeToWindow(Application.Current.MainWindow, themeType);
        }

        // 根据当前主题更新兑换码按钮的默认前景色（若无更新高亮）
        if (_redeemCodeUpdateNewVersion == null)
        {
            UpdateRedeemCodeButtonDefaultForeground();
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
    private void OnOpenFeed()
    {
        if (_redeemCodeUpdateNewVersion != null)
        {
            Config.CommonConfig.RedeemCodeFeedsUpdateVersion = _redeemCodeUpdateNewVersion;
            // 重置为主题默认前景色，避免浅色主题下显示为白色
            UpdateRedeemCodeButtonDefaultForeground();
            _redeemCodeUpdateNewVersion = null;
        }

        var feedWindow = new FeedWindow(new FeedWindowViewModel());
        feedWindow.Show();
    }

    [RelayCommand]
    private async Task OnLoaded()
    {

        // 应用上次保存的主题
        ApplyTheme(Config.CommonConfig.CurrentThemeType);


        // 预热OCR
        await OcrPreheating();

        if (Environment.GetCommandLineArgs().Length > 1)
        {
            return;
        }

        // 自动处理目录配置
        await Patch1();

        // 删除多余特征点
        Patch2();

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

        OnceRun();

        // 检查更新
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption());
        
        // 检查兑换码更新
        await CheckRedeemCodeFeedsUpdateAsync();

        //  Win11下 BitBlt截图方式不可用，需要关闭窗口优化功能
        if (OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt)
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }

        // 更新仓库
        // ScriptRepoUpdater.Instance.AutoUpdate();

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
                var res = await MessageBox.ShowAsync("检测到旧的 BetterGI 配置，是否迁移配置并清理旧目录？", "BetterGI",
                    System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question);
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

    /**
     * 0.45版本开始
     * 地图特征的存储格式变化
     */
    private void Patch2()
    {
        List<string> files =
        [
            Global.Absolute(@"Assets\Map\mainMap256Block_SIFT.kp"),
            Global.Absolute(@"Assets\Map\mainMap256Block_SIFT.mat"),
            Global.Absolute(@"Assets\Map\mainMap2048Block_SIFT.kp"),
            Global.Absolute(@"Assets\Map\mainMap2048Block_SIFT.mat"),
            Global.Absolute(@"Assets\Map\Teyvat\map_info.json"),
        ];

        // 循环删除
        foreach (var file in files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private async Task OcrPreheating()
    {
        try
        {
            await Task.Run(async () =>
            {
                try
                {
                    // 现在OCR创建的时候会自己读设置了
                    // string gameCultureInfoName = TaskContext.Instance().Config.OtherConfig.GameCultureInfoName;
                    // await OcrFactory.ChangeCulture(gameCultureInfoName);
                    var s = OcrFactory.Paddle.Ocr(new Mat(Global.Absolute(@"Assets\Model\PaddleOCR\test_pp_ocr.png")));
                    Debug.WriteLine("PaddleOcr预热结果:" + s);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    _logger.LogError("PaddleOcr预热异常，解决方案：【https://bettergi.com/faq.html】\r\n" + e.Source + "\r\n--" +
                                     Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                    var innerException = e.InnerException;
                    if (innerException != null)
                    {
                        _logger.LogError("PaddleOcr预热内部异常，解决方案：【https://bettergi.com/faq.html】\r\n" +
                                         innerException.Source + "\r\n--" + Environment.NewLine +
                                         innerException.StackTrace + "\r\n---" + Environment.NewLine +
                                         innerException.Message);
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
            MessageBox.Warning("PaddleOcr预热失败，解决方案：【https://bettergi.com/faq.html】   \r\n" + e.Source + "\r\n--" +
                               Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            Process.Start(
                new ProcessStartInfo(
                        "https://bettergi.com/faq.html#%E2%9D%93%E6%8F%90%E7%A4%BA-paddleocr%E9%A2%84%E7%83%AD%E5%A4%B1%E8%B4%A5-%E5%BA%94%E8%AF%A5%E5%A6%82%E4%BD%95%E8%A7%A3%E5%86%B3")
                { UseShellExecute = true });
        }
    }

    private void OnceRun()
    {
        string deviceId = DeviceIdHelper.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            deviceId = "default"; // 如果获取设备ID失败，使用默认值
        }

        // 每个设备只运行一次
        if (!Config.CommonConfig.OnceHadRunDeviceIdList.Contains(deviceId))
        {
            WelcomeDialog prompt = new WelcomeDialog
            {
                Owner = Application.Current.MainWindow
            };
            prompt.ShowDialog();
            prompt.Focus();

            Config.CommonConfig.OnceHadRunDeviceIdList.Add(deviceId);
            _configService.Save();
        }
    }
    
    private async Task CheckRedeemCodeFeedsUpdateAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/update_time.txt");
            var response = await HttpClientFactory.GetCommonSendClient().SendAsync(request);
            response.EnsureSuccessStatusCode();
            var txt = await response.Content.ReadAsStringAsync();


            if (!string.IsNullOrEmpty(txt))
            {
                if (long.TryParse(txt, out long v2) 
                    && long.TryParse(Config.CommonConfig.RedeemCodeFeedsUpdateVersion, out long v1))
                {
                    if (v2 > v1)
                    {
                        RedeemCodeButtonForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E9BFA"));
                        _redeemCodeUpdateNewVersion = txt;
                    }
                }
            }
            
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, $"获取兑换码是否存在更新失败");
        }
    }

    // 更新兑换码按钮在当前主题下的默认前景色
    private void UpdateRedeemCodeButtonDefaultForeground()
    {
        try
        {
            var brush = Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush;
            if (brush != null)
            {
                RedeemCodeButtonForeground = brush;
                return;
            }
        }
        catch
        {
            // 忽略资源查找异常，走回退逻辑
        }

        // 回退：根据当前主题类型使用黑/白色
        var isLightTheme = Config.CommonConfig.CurrentThemeType == ThemeType.LightNone
                           || Config.CommonConfig.CurrentThemeType == ThemeType.LightMica
                           || Config.CommonConfig.CurrentThemeType == ThemeType.LightAcrylic;
        RedeemCodeButtonForeground = isLightTheme ? Brushes.Black : Brushes.White;
    }
}