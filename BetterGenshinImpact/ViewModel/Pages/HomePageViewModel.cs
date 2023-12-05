using BetterGenshinImpact.Core;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SharpDX.Direct3D11;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Windows.Graphics.Printing.PrintSupport;
using Windows.System;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class HomePageViewModel : ObservableObject, INavigationAware
{
    [ObservableProperty] private string[] _modeNames = GameCaptureFactory.ModeNames();

    [ObservableProperty] private string? _selectedMode = CaptureModes.BitBlt.ToString();

    private bool _taskDispatcherEnabled = false;
    [ObservableProperty] private Visibility _startButtonVisibility = Visibility.Visible;
    [ObservableProperty] private Visibility _stopButtonVisibility = Visibility.Collapsed;

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

    public HomePageViewModel(IConfigService configService, TaskTriggerDispatcher taskTriggerDispatcher)
    {
        _taskDispatcher = taskTriggerDispatcher;
        Config = configService.Get();
        // 检查用户是否配置了原神安装目录，如果没有，尝试从注册表中读取
        if (string.IsNullOrEmpty(Config.InstallPath))
        {
            string installPath = string.Empty;
            if (ReadGameInstallPath(out installPath))
            {
                // 检查文件是否存在
                if (!File.Exists(installPath))
                {
                    System.Windows.MessageBox.Show("未找到原神安装目录，请手动选择！");
                }
                else
                {
                    Config.InstallPath = installPath;
                }
            }
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

    [RelayCommand]
    private void OnLoaded()
    {
    }

    private void OnClosed()
    {
        OnStopTrigger();
        // 等待任务结束
        _maskWindow?.Close();
    }

    [RelayCommand]
    private void OnStartCaptureTest()
    {
        //var hWnd = SystemControl.FindGenshinImpactHandle();
        //if (hWnd == IntPtr.Zero)
        //{
        //    System.Windows.MessageBox.Show("未找到原神窗口");
        //    return;
        //}

        //CaptureTestWindow captureTestWindow = new();
        //captureTestWindow.StartCapture(hWnd, Config.CaptureMode.ToCaptureMode());
        //captureTestWindow.Show();

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
    private async Task OpenDisplayAdvancedGraphicsSettingsAsync()
    {
        // ms-settings:display
        // ms-settings:display-advancedgraphics
        // ms-settings:display-advancedgraphics-default
        await Launcher.LaunchUriAsync(new Uri("ms-settings:display-advancedgraphics"));
    }

    private bool CanStartTrigger() => StartButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStartTrigger))]
    private async Task OnStartTriggerAsync()
    {
        var hWnd = SystemControl.FindGenshinImpactHandle();
        if (hWnd == IntPtr.Zero)
        {
            if (!string.IsNullOrEmpty(Config.InstallPath))
            {
                hWnd = await SystemControl.StartFromLocalAsync(Config.InstallPath);
            }
            if (hWnd == IntPtr.Zero)
            {
                System.Windows.MessageBox.Show("未找到原神窗口，请先启动原神！");
                return;
            }
        }

        if (!_taskDispatcherEnabled)
        {
            _taskDispatcher.Start(hWnd, Config.CaptureMode.ToCaptureMode(), Config.TriggerInterval);
            _maskWindow = MaskWindow.Instance();
            _maskWindow.RefreshPosition(hWnd);
            _mouseKeyMonitor.Subscribe(hWnd);
            _taskDispatcherEnabled = true;
            StartButtonVisibility = Visibility.Collapsed;
            StopButtonVisibility = Visibility.Visible;
        }
    }

    private bool CanStopTrigger() => StopButtonEnabled;

    [RelayCommand(CanExecute = nameof(CanStopTrigger))]
    private void OnStopTrigger()
    {
        if (_taskDispatcherEnabled)
        {
            _maskWindow?.Hide();
            _taskDispatcher.Stop();
            _taskDispatcherEnabled = false;
            _mouseKeyMonitor.Unsubscribe();
            StartButtonVisibility = Visibility.Visible;
            StopButtonVisibility = Visibility.Collapsed;
        }
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
        //var result = OcrFactory.Paddle.OcrResult(new Mat(@"E:\HuiTask\更好的原神\七圣召唤\d4.png", ImreadModes.Grayscale));
        //foreach (var region in result.Regions)
        //{
        //    Debug.WriteLine($"{region.Text}");
        //}

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
    }

    [RelayCommand]
    public async Task SelectInstallPathAsync()
    {
        await Task.Run(() =>
        {
            // 弹出选择文件夹对话框
            var dialog = new Ookii.Dialogs.Wpf.VistaOpenFileDialog();
            dialog.Filter = "原神|YuanShen.exe|云原神|Genshin Impact Cloud Game.exe|启动器|launcher.exe|所有文件|*.*";
            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                if (path.EndsWith("YuanShen.exe") || path.EndsWith("Genshin Impact Cloud Game.exe") || path.EndsWith("launcher.exe"))
                {
                    Config.InstallPath = path;
                }
                else
                {
                    System.Windows.MessageBox.Show("请选择有效的文件！");
                }
            }
        });
    }

    private bool ReadGameInstallPath(out string installPath)
    {
        // 首先尝试获取原神路径
        var path = ReadFromRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\原神", "InstallPath");
        if (!string.IsNullOrEmpty(path))
        {
            // 这里拿到了launcher的安装目录，读取同目录下的config.ini文件，获取游戏的安装目录
            var configIniPath = Path.Combine(path, "config.ini");
            if (File.Exists(configIniPath))
            {
                var lines = File.ReadAllLines(configIniPath);
                var gameInstallPath = lines.FirstOrDefault(x => x.StartsWith("game_install_path"));
                if (!string.IsNullOrEmpty(gameInstallPath))
                {
                    installPath = Path.Combine(gameInstallPath.Split('=')[1], "Yuanshen.exe").Replace("/", "\\");
                    return true;
                }
            }
            // 如果没读取到，就使用launcher的路径
            installPath = Path.Combine(path, "launcher.exe");
            return true;
        }
        // 如果没有读取到原神的路径，尝试读取云原神
        path = ReadFromRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\云·原神", "InstallPath");
        if (!string.IsNullOrEmpty(path))
        {
            installPath = Path.Combine(path, "Genshin Impact Cloud Game.exe");
            return true;
        }
        installPath = string.Empty;
        return false;
    }

    private string ReadFromRegistry(string key, string keyName)
    {
        var regKey = Registry.LocalMachine.OpenSubKey(key);
        if (regKey == null)
        {
            return string.Empty;
        }
        var value = regKey.GetValue(keyName);
        if (value == null)
        {
            return string.Empty;
        }
        return value.ToString();
    }
}
