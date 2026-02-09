using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Controls.Webview;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.System;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ViewModel
{
    [ObservableProperty] private IEnumerable<EnumItem<CaptureModes>> _modeNames = EnumExtensions.ToEnumItems<CaptureModes>();

    [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

    [ObservableProperty] private bool _taskDispatcherEnabled = false;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StartTriggerCommand))]
    private bool _startButtonEnabled = true;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(StopTriggerCommand))]
    private bool _stopButtonEnabled = true;

    public AllConfig Config { get; set; }

    private MaskWindow? _maskWindow;
    private readonly ILogger<HomePageViewModel> _logger = App.GetLogger<HomePageViewModel>();

    private readonly TaskTriggerDispatcher _taskDispatcher;
    private readonly MouseKeyMonitor _mouseKeyMonitor = new();

    // 记录上次使用原神的句柄
    private IntPtr _hWnd;

    [ObservableProperty] private InferenceDeviceType[] _inferenceDeviceTypes = Enum.GetValues<InferenceDeviceType>();

    [ObservableProperty] private ImageSource _bannerImageSource;

    private const string DefaultBannerImagePath = "pack://application:,,,/Resources/Images/banner.jpg";
    private readonly string _customBannerImagePath = Global.Absolute("User/Images/custom_banner.jpg");

    public HomePageViewModel(IConfigService configService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        _taskDispatcher = taskTriggerDispatcher;
        Config = configService.Get();
        ReadGameInstallPath();
        InitializeBannerImage();


        // WindowsGraphicsCapture 只支持 Win10 18362 及以上的版本 (Windows 10 version 1903 or later)
        // https://github.com/babalae/better-genshin-impact/issues/394
        if (!OsVersionHelper.IsWindows10_1903_OrGreater)
        {
            // 删除 _modeNames 中的 CaptureModes.WindowsGraphicsCapture
            _modeNames = _modeNames.Where(x => x.EnumName != CaptureModes.WindowsGraphicsCapture.ToString()).ToList();

            // DirectML 是在 Windows 10 版本 1903 和 Windows SDK 的相应版本中引入的。
            // https://learn.microsoft.com/zh-cn/windows/ai/directml/dml
            _inferenceDeviceTypes = _inferenceDeviceTypes
                .Where(x => x != InferenceDeviceType.GpuDirectMl)
                .ToArray();
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
    }

    private bool _autoRun = true;

    [RelayCommand]
    private void OnLoaded()
    {
        // OnTest();

        // 组件首次加载时运行一次。
        if (!_autoRun)
        {
            return;
        }

        _autoRun = false;

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].Contains("start"))
        {
            _ = OnStartTriggerAsync();
        }
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
            _logger.LogInformation(Lang.S["Gen_12239_391797"], Config.CaptureMode);
            OnStopTrigger();
            await OnStartTriggerAsync();
        }
    }

    // [RelayCommand]
    // private void OnInferenceDeviceTypeDropDownChanged(string value)
    // {
    // }

    [RelayCommand]
    private void OnStartCaptureTest()
    {
        var picker = new PickerWindow(true);

        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                var captureWindow = new CaptureTestWindow();
                captureWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
                captureWindow.Show();
            }
            else
            {
                ThemedMessageBox.Error(Lang.S["Gen_1002_9a2665"]);
            }
        }
    }

    [RelayCommand]
    private void OnManualPickWindow()
    {
        var picker = new PickerWindow();
        if (picker.PickCaptureTarget(new WindowInteropHelper(UIDispatcherHelper.MainWindow).Handle, out var hWnd))
        {
            if (hWnd != IntPtr.Zero)
            {
                _hWnd = hWnd;
                Start(hWnd);
            }
            else
            {
                ThemedMessageBox.Error(Lang.S["Gen_1003_1a77a5"]);
            }
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
            if (Config.GenshinStartConfig.LinkedStartEnabled)
            {
                if (string.IsNullOrEmpty(Config.GenshinStartConfig.InstallPath))
                {
                    await ThemedMessageBox.ErrorAsync(Lang.S["Gen_12238_0f5493"]);
                    return;
                }

                hWnd = await SystemControl.StartFromLocalAsync(Config.GenshinStartConfig.InstallPath);
                if (hWnd != IntPtr.Zero)
                {
                    TaskContext.Instance().LinkedStartGenshinTime = DateTime.Now; // 标识关联启动原神的时间
                }
                else
                {
                    return;
                }
            }

            if (hWnd == IntPtr.Zero)
            {
                await ThemedMessageBox.ErrorAsync(Lang.S["Gen_12237_f88c90"]);
                return;
            }
        }

        Start(hWnd);
    }

    private void Start(IntPtr hWnd)
    {
        Debug.WriteLine($"{Lang.S["Gen_12236_6c711e"]});
        lock (this)
        {
            if (Config.TriggerInterval <= 0)
            {
                ThemedMessageBox.Error(Lang.S["Gen_1004_4647a1"]);
                return;
            }

            if (!TaskDispatcherEnabled)
            {
                _hWnd = hWnd;
                _taskDispatcher.Start(hWnd, GetCaptureMode(), Config.TriggerInterval);
                _taskDispatcher.UiTaskStopTickEvent -= OnUiTaskStopTick;
                _taskDispatcher.UiTaskStartTickEvent -= OnUiTaskStartTick;
                _taskDispatcher.UiTaskStopTickEvent += OnUiTaskStopTick;
                _taskDispatcher.UiTaskStartTickEvent += OnUiTaskStartTick;
                _maskWindow ??= new MaskWindow();
                _maskWindow.Show();
                MaskWindow.Instance().RefreshPosition();
                _mouseKeyMonitor.Subscribe(hWnd);
                TaskDispatcherEnabled = true;
            }
        }
    }

    private CaptureModes GetCaptureMode()
    {
        try
        {
            return Config.CaptureMode.ToCaptureMode();
        }
        catch (Exception e)
        {
            TaskContext.Instance().Config.CaptureMode = CaptureModes.BitBlt.ToString();
            return CaptureModes.BitBlt;
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
        lock (this)
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
                TaskContext.Instance().IsInitialized = false;
            }
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

    [RelayCommand]
    public void OnGoToWikiUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/doc.html") { UseShellExecute = true });
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
        //    ThemedMessageBox.Show(JsonSerializer.Serialize(result));
        //}
        //catch (Exception e)
        //{
        //    ThemedMessageBox.Show(e.StackTrace);
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
                Filter = Lang.S["Gen_12235_b1b548"]
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

    //[RelayCommand]
    //private void OnOpenGameCommandLineDocument()
    //{
    //    string md = File.ReadAllText(Global.Absolute(@"Assets\Strings\gicli.md"), Encoding.UTF8);

    //    md = WebUtility.HtmlEncode(md);
    //    string md2html = File.ReadAllText(Global.Absolute(@"Assets\Strings\md2html.html"), Encoding.UTF8);
    //    var html = md2html.Replace("{{content}}", md);

    //    WebpageWindow win = new()
    //    {
    //        Title = "启动参数说明",
    //        Width = 800,
    //        Height = 600,
    //        Owner = Application.Current.MainWindow,
    //        WindowStartupLocation = WindowStartupLocation.CenterOwner
    //    };

    //    win.NavigateToHtml(html);
    //    win.ShowDialog();
    //}

    [RelayCommand]
    private void OnOpenGameCommandLineDocument()
    {
        string md = File.ReadAllText(Global.Absolute(@"Assets\Strings\gicli.md"), Encoding.UTF8);

        var flowDoc = MarkdownToFlowDocumentConverter.ConvertToFlowDocument(md);

        // 创建 RichTextBox 来显示内容
        var richTextBox = new System.Windows.Controls.RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = true,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Document = flowDoc,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 12)
        };

        // 创建两行的 Grid 容器
        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TitleBar 行
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容行

        // 创建 TitleBar
        var titleBar = new TitleBar
        {
            Title = Lang.S["Gen_1005_845503"],
            Icon = new ImageIcon
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(@"pack://application:,,,/Resources/Images/logo.png", UriKind.Absolute))
            },
        };
        System.Windows.Controls.Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        // 将 RichTextBox 添加到第二行
        System.Windows.Controls.Grid.SetRow(richTextBox, 1);
        grid.Children.Add(richTextBox);

        // 创建 FluentWindow 来显示内容
        var dialogWindow = new FluentWindow
        {
            Content = grid,
            Width = 800,
            Height = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Manual,
            WindowBackdropType = WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        dialogWindow.ShowDialog();
    }

    [RelayCommand]
    public void OnOpenHardwareAccelerationSettings()
    {
        var dialogWindow = new FluentWindow
        {
            Title = Lang.S["Gen_1006_09c053"],
            Content = new HardwareAccelerationView(new HardwareAccelerationViewModel()),
            Width = 800,
            Height = 600,
            MinWidth = 800,
            MaxWidth = 800,
            MinHeight = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Auto,
        };
        dialogWindow.SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        var result = dialogWindow.ShowDialog();
    }

    #region 背景图片管理

    private void InitializeBannerImage()
    {
        try
        {
            // 检查是否存在自定义图片
            if (File.Exists(_customBannerImagePath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                BannerImageSource = bitmap;
                _logger.LogInformation(Lang.S["Gen_12234_f888d9"]);
            }
            else
            {
                // 使用默认图片
                BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
                _logger.LogInformation(Lang.S["Gen_12233_8c6581"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, Lang.S["Gen_12232_f8c281"]);
            BannerImageSource = new BitmapImage(new Uri(DefaultBannerImagePath, UriKind.Absolute));
        }
    }

    [RelayCommand]
    private void ChangeBannerImage()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = Lang.S["Gen_1007_2c7b91"],
                Filter = Lang.S["Gen_12231_89d3b4"],
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ResetBannerImage();
                
                var selectedFile = openFileDialog.FileName;

                // 确保目标目录存在
                var directory = Path.GetDirectoryName(_customBannerImagePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 复制图片到自定义路径
                File.Copy(selectedFile, _customBannerImagePath, true);

                // 更新UI
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(_customBannerImagePath));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
                bitmap.EndInit();
                BannerImageSource = bitmap;
                Toast.Success(Lang.S["Gen_1008_60e507"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, Lang.S["Gen_12230_bb4c5e"]);
            Toast.Error($"{Lang.S["Gen_12229_4ee022"]});
        }
    }

    [RelayCommand]
    private void ResetBannerImage()
    {
        try
        {
            // 获取自定义图片的完整路径
            var customImageFullPath = Path.GetFullPath(_customBannerImagePath);
            _logger.LogInformation(Lang.S["Gen_12228_a93570"], customImageFullPath);

            // 先切换到默认图片，释放自定义图片的文件锁
            var defaultBitmap = new BitmapImage();
            defaultBitmap.BeginInit();
            defaultBitmap.UriSource = new Uri(DefaultBannerImagePath, UriKind.Absolute);
            defaultBitmap.CacheOption = BitmapCacheOption.OnLoad;
            defaultBitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache; 
            defaultBitmap.EndInit();
            BannerImageSource = defaultBitmap;
            
            if (File.Exists(customImageFullPath))
            {
                File.Delete(customImageFullPath);
                Toast.Success(Lang.S["Gen_1009_756538"]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, Lang.S["Gen_12227_6238a0"]);
            Toast.Warning(Lang.S["Gen_1010_e7f5a5"]);
        }
    }

    #endregion
}