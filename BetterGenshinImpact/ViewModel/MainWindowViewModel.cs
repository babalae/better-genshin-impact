using BetterGenshinImpact.Core.Config;
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
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel;

public partial class MainWindowViewModel : ObservableObject, IViewModel
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigService _configService;
    public string Title => $"BetterGI · 更好的原神 · {Global.Version}{(RuntimeHelper.IsDebug ? " · Dev" : string.Empty)}";

    [ObservableProperty]
    public bool _isVisible = true;

    [ObservableProperty]
    public WindowState _windowState = WindowState.Normal;

    [ObservableProperty]
    public WindowBackdropType _currentBackdropType = WindowBackdropType.Auto;
    
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
        CurrentBackdropType = CurrentBackdropType switch
        {
            WindowBackdropType.Auto => WindowBackdropType.Mica,
            WindowBackdropType.Mica => WindowBackdropType.Acrylic,
            WindowBackdropType.Acrylic => WindowBackdropType.Tabbed,
            WindowBackdropType.Tabbed => WindowBackdropType.Auto,
            _ => WindowBackdropType.Auto
        };

        Config.CommonConfig.CurrentBackdropType = CurrentBackdropType;

        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));;
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
    [SuppressMessage("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "MVVMTK0039:Async void returning method annotated with RelayCommand")]
    private async void OnLoaded()
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
                    _logger.LogError("PaddleOcr预热异常，解决方案：https://bgi.huiyadan.com/faq.html：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                    var innerException = e.InnerException;
                    if (innerException != null)
                    {
                        _logger.LogError("PaddleOcr预热内部异常，解决方案：https://bgi.huiyadan.com/faq.html：" + innerException.Source + "\r\n--" + Environment.NewLine + innerException.StackTrace + "\r\n---" + Environment.NewLine + innerException.Message);
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
            MessageBox.Warning("PaddleOcr预热失败，解决方案：https://bgi.huiyadan.com/faq.html，" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }

        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption());

        //  Win11下 BitBlt截图方式不可用，需要关闭窗口优化功能
        if (OsVersionHelper.IsWindows11_OrGreater && TaskContext.Instance().Config.AutoFixWin11BitBlt)
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }

        // 更新仓库
        ScriptRepoUpdater.Instance.AutoUpdate();
    }
}
