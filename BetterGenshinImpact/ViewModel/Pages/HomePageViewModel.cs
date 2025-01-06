﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Webview;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Windows.System;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ObservableObject, INavigationAware, IViewModel
{
    [ObservableProperty]
    private string[] _modeNames = GameCaptureFactory.ModeNames();

    [ObservableProperty]
    private string? _selectedMode = CaptureModes.BitBlt.ToString();

    [ObservableProperty]
    private bool _taskDispatcherEnabled = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTriggerCommand))]
    private bool _startButtonEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopTriggerCommand))]
    private bool _stopButtonEnabled = true;

    public AllConfig Config { get; set; }

    private MaskWindow? _maskWindow;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly TaskTriggerDispatcher _taskDispatcher;
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();

    // 记录上次使用原神的句柄
    private IntPtr _hWnd;

    [ObservableProperty]
    private string[] _inferenceDeviceTypes = BgiSessionOption.InferenceDeviceTypes;

    public HomePageViewModel(IConfigService configService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        _taskDispatcher = taskTriggerDispatcher;
        Config = configService.Get();
        ReadGameInstallPath();

        // WindowsGraphicsCapture 只支持 Win10 18362 及以上的版本 (Windows 10 version 1903 or later)
        // https://github.com/babalae/better-genshin-impact/issues/394
        if (!OsVersionHelper.IsWindows10_1903_OrGreater)
        {
            _modeNames = _modeNames.Where(x => x != CaptureModes.WindowsGraphicsCapture.ToString()).ToArray();
            // DirectML 是在 Windows 10 版本 1903 和 Windows SDK 的相应版本中引入的。
            // https://learn.microsoft.com/zh-cn/windows/ai/directml/dml
            _inferenceDeviceTypes = _inferenceDeviceTypes.Where(x => x != "GPU_DirectML").ToArray();
        }

        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (sender, msg) =>
        {
            if (msg.PropertyName == "Close")
            {
                OnClosed();
            }
            else if (msg.PropertyName == "SwitchTriggerStatus")
            {
                if (_taskDispatcherEnabled)
                {
                    OnStopTrigger();
                }
                else
                {
                    _ = OnStartTriggerAsync();
                }
            }
        });

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            if (args[1].Contains("start"))
            {
                _ = OnStartTriggerAsync();
            }
        }
    }

    [RelayCommand]
    private void OnLoaded()
    {
        // OnTest();
    }

    private void OnClosed()
    {
        OnStopTrigger();
        // 等待任务结束
        _maskWindow?.Close();
    }

    [RelayCommand]
    private async Task OnCaptureModeDropDownChanged()
    {
        // 启动的情况下重启
        if (TaskDispatcherEnabled)
        {
            _logger.LogInformation("► 切换捕获模式至[{Mode}]，截图器自动重启...", Config.CaptureMode);
            OnStopTrigger();
            await OnStartTriggerAsync();
        }
    }

    [RelayCommand]
    private void OnInferenceDeviceTypeDropDownChanged(string value)
    {
    }

    [RelayCommand]
    private void OnStartCaptureTest()
    {
        var picker = new PickerWindow();
        var hWnd = picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle);
        if (hWnd != IntPtr.Zero)
        {
            var captureWindow = new CaptureTestWindow();
            captureWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
            captureWindow.Show();
        }
    }

    [RelayCommand]
    private void OnManualPickWindow()
    {
        var picker = new PickerWindow();
        var hWnd = picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle);
        if (hWnd != IntPtr.Zero)
        {
            _hWnd = hWnd;
            Start(hWnd);
        }
        else
        {
            MessageBox.Error("选择的窗体句柄为空！");
        }
    }

    [RelayCommand]
    private async Task OpenDisplayAdvancedGraphicsSettingsAsync()
    {
        // ms-settings:display
        // ms-settings:display-advancedgraphics
        // ms-settings:display-advancedgraphics-default
        await Launcher.LaunchUriAsync(new Uri("ms-settings:display-advancedgraphics"));
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    public async Task OnStartTriggerAsync()
    {
        var hWnd = SystemControl.FindGenshinImpactHandle();
        if (hWnd == IntPtr.Zero)
        {
            if (Config.GenshinStartConfig.LinkedStartEnabled && !string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
            {
                hWnd = await SystemControl.StartFromLocalAsync(Config.GenshinStartConfig.InstallPath);
                if (hWnd != IntPtr.Zero)
                {
                    TaskContext.Instance().LinkedStartGenshinTime = DateTime.Now; // 标识关联启动原神的时间
                }
            }

            if (hWnd == IntPtr.Zero)
            {
                MessageBox.Error("未找到原神窗口，请先启动原神！");
                return;
            }
        }

        Start(hWnd);
    }

    private void Start(IntPtr hWnd)
    {
        if (!TaskDispatcherEnabled)
        {
            _hWnd = hWnd;
            _taskDispatcher.Start(hWnd, Config.CaptureMode.ToCaptureMode(), Config.TriggerInterval);
            _taskDispatcher.UiTaskStopTickEvent -= OnUiTaskStopTick;
            _taskDispatcher.UiTaskStartTickEvent -= OnUiTaskStartTick;
            _taskDispatcher.UiTaskStopTickEvent += OnUiTaskStopTick;
            _taskDispatcher.UiTaskStartTickEvent += OnUiTaskStartTick;
            _maskWindow ??= new MaskWindow();
            _maskWindow.Show();
            _mouseKeyMonitor.Subscribe(hWnd);
            TaskDispatcherEnabled = true;
        }
    }

    private bool CanStopTrigger() => StopButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStopTrigger))]
    private void OnStopTrigger()
    {
        Stop();
    }

    private void Stop()
    {
        if (TaskDispatcherEnabled)
        {
            CancellationContext.Instance.Cancel(); // 取消独立任务的运行
            _taskDispatcher.Stop();
            if (_maskWindow != null && _maskWindow.IsExist())
            {
                _maskWindow?.Hide();
            }
            else
            {
                _maskWindow?.Close();
                _maskWindow = null;
            }

            TaskDispatcherEnabled = false;
            _mouseKeyMonitor.Unsubscribe();
        }
    }

    private void OnUiTaskStopTick(object? sender, EventArgs e)
    {
        UIDispatcherHelper.Invoke(Stop);
    }

    private void OnUiTaskStartTick(object? sender, EventArgs e)
    {
        UIDispatcherHelper.Invoke(() => Start(_hWnd));
    }

    public void OnNavigatedTo()
    {
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    public void OnGoToWikiUrl()
    {
        Process.Start(new ProcessStartInfo("https://bgi.huiyadan.com/doc.html") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OnTest()
    {
        // var result = OcrFactory.Paddle.OcrResult(new Mat(@"E:\HuiTask\更好的原神\自动秘境\自动战斗\队伍识别\x2.png", ImreadModes.Grayscale));
        // foreach (var region in result.Regions)
        // {
        //     Debug.WriteLine($"{region.Text}");
        // }

        //try
        //{
        //    YoloV8 predictor = new(Global.Absolute("Assets\\Model\\Fish\\bgi_fish.onnx"));
        //    using var memoryStream = new MemoryStream();
        //    new Bitmap(Global.Absolute("test_yolo.png")).Save(memoryStream, ImageFormat.Bmp);
        //    memoryStream.Seek(0, SeekOrigin.Begin);
        //    var result = predictor.Detect(memoryStream);
        //    MessageBox.Show(JsonSerializer.Serialize(result));
        //}
        //catch (Exception e)
        //{
        //    MessageBox.Show(e.StackTrace);
        //}

        // Mat tar = new(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\selected.png", ImreadModes.Grayscale);
        //  var mask = OpenCvCommonHelper.CreateMask(tar, new Scalar(0, 0, 0));
        // var src = new Mat(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\Clip_20240309_135839.png", ImreadModes.Grayscale);
        // var src2 = src.Clone();
        // var res = MatchTemplateHelper.MatchOnePicForOnePic(src, mask);
        // // 把结果画到原图上
        // foreach (var t in res)
        // {
        //     Cv2.Rectangle(src2, t, new Scalar(0, 0, 255));
        // }
        //
        // Cv2.ImWrite(@"E:\HuiTask\更好的原神\自动剧情\自动邀约\x1.png", src2);
    }

    [RelayCommand]
    public async Task SelectInstallPathAsync()
    {
        await Task.Run(() =>
        {
            // 弹出选择文件夹对话框
            var dialog = new Ookii.Dialogs.Wpf.VistaOpenFileDialog
            {
                Filter = "原神|YuanShen.exe|原神国际服|GenshinImpact.exe|所有文件|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                Config.GenshinStartConfig.InstallPath = path;
            }
        });
    }

    private void ReadGameInstallPath()
    {
        // 检查用户是否配置了原神安装目录，如果没有，尝试从注册表中读取
        if (string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
        {
            Task.Run(async () =>
            {
                var p1 = RegistryGameLocator.GetDefaultGameInstallPath();
                if (!string.IsNullOrEmpty(p1))
                {
                    Config.GenshinStartConfig.InstallPath = p1;
                }
                else
                {
                    var p2 = await UnityLogGameLocator.LocateSingleGamePathAsync();
                    if (!string.IsNullOrEmpty(p2))
                    {
                        Config.GenshinStartConfig.InstallPath = p2;
                    }
                }
            });
        }
    }

    [RelayCommand]
    private void OnOpenGameCommandLineDocument()
    {
        string md = ResourceHelper.GetString($"pack://application:,,,/Assets/Strings/gicli.md", Encoding.UTF8);

        md = WebUtility.HtmlEncode(md);
        string md2html = ResourceHelper.GetString($"pack://application:,,,/Assets/Strings/md2html.html", Encoding.UTF8);
        var html = md2html.Replace("{{content}}", md);

        WebpageWindow win = new()
        {
            Title = "启动参数说明",
            Width = 800,
            Height = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        win.NavigateToHtml(html);
        win.ShowDialog();
    }
}