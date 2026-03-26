using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Hardware;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using Vanara.PInvoke;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

public partial class MacroSettingsPageViewModel : ViewModel
{
    public AllConfig Config { get; set; }

    private readonly INavigationService _navigationService;
    private readonly DispatcherTimer _monitorTimer;
    private readonly (HardwareInputMonitorItemViewModel Item, int HidCode)[] _keyboardMonitorBindings;
    private readonly (HardwareInputMonitorItemViewModel Item, HardwareMouseButton Button)[] _mouseMonitorBindings;
    private string _lastKeyboardConnectedBaudRate = string.Empty;
    private string _lastMouseConnectedBaudRate = string.Empty;

    [ObservableProperty]
    private string[] _quickFightMacroHotkeyMode = [OneKeyFightTask.HoldOnMode, OneKeyFightTask.TickMode];

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _inputModeOptions =
    [
        new(HardwareInputConfigValues.Virtual, "虚拟信号"),
        new(HardwareInputConfigValues.Hardware, "硬体"),
    ];

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _keyboardHardwareVendorOptions =
    [
        new(HardwareInputConfigValues.Ferrum, "Ferrum"),
        new(HardwareInputConfigValues.Makxd, "Makxd"),
    ];

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _mouseHardwareVendorOptions =
    [
        new(HardwareInputConfigValues.Makcu, "Makcu"),
        new(HardwareInputConfigValues.Makxd, "Makxd"),
        new(HardwareInputConfigValues.Ferrum, "Ferrum"),
    ];

    [ObservableProperty]
    private List<KeyValuePair<string, string>> _ferrumApiOptions =
    [
        new(HardwareInputConfigValues.Km, "KM"),
        new(HardwareInputConfigValues.Dhz, "DHZ"),
        new(HardwareInputConfigValues.Net, "NET"),
    ];

    [ObservableProperty]
    private ObservableCollection<HardwareInputMonitorItemViewModel> _keyboardMonitorItems = [];

    [ObservableProperty]
    private ObservableCollection<HardwareInputMonitorItemViewModel> _mouseMonitorItems = [];

    [ObservableProperty]
    private bool _isKeyboardMonitorSupported;

    [ObservableProperty]
    private bool _isMouseMonitorSupported;

    [ObservableProperty]
    private string _keyboardMonitorHint = "当前为虚拟键盘输出，监听面板不会读取硬体状态。";

    [ObservableProperty]
    private string _mouseMonitorHint = "当前为虚拟滑鼠输出，监听面板不会读取硬体状态。";

    public MacroSettingsPageViewModel(IConfigService configService, INavigationService navigationService)
    {
        Config = configService.Get();
        _navigationService = navigationService;

        _keyboardMonitorBindings =
        [
            CreateKeyboardMonitor("W", 26),
            CreateKeyboardMonitor("A", 4),
            CreateKeyboardMonitor("S", 22),
            CreateKeyboardMonitor("D", 7),
            CreateKeyboardMonitor("Q", 20),
            CreateKeyboardMonitor("E", 8),
            CreateKeyboardMonitor("F", 9),
            CreateKeyboardMonitor("Shift", 225),
            CreateKeyboardMonitor("Space", 44),
        ];
        KeyboardMonitorItems = new ObservableCollection<HardwareInputMonitorItemViewModel>(_keyboardMonitorBindings.Select(x => x.Item));

        _mouseMonitorBindings =
        [
            CreateMouseMonitor("左键", HardwareMouseButton.Left),
            CreateMouseMonitor("右键", HardwareMouseButton.Right),
            CreateMouseMonitor("中键", HardwareMouseButton.Middle),
            CreateMouseMonitor("侧键1", HardwareMouseButton.Side1),
            CreateMouseMonitor("侧键2", HardwareMouseButton.Side2),
        ];
        MouseMonitorItems = new ObservableCollection<HardwareInputMonitorItemViewModel>(_mouseMonitorBindings.Select(x => x.Item));

        _monitorTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _monitorTimer.Tick += (_, _) => RefreshHardwareMonitor();
        RefreshHardwareMonitor();
    }

    public override void OnNavigatedTo()
    {
        _monitorTimer.Start();
        RefreshHardwareMonitor();
    }

    public override void OnNavigatedFrom()
    {
        _monitorTimer.Stop();
    }

    [RelayCommand]
    public void OnGoToHotKeyPage()
    {
        _navigationService.Navigate(typeof(HotKeyPage));
    }

    [RelayCommand]
    public void RefreshHardwarePorts()
    {
        Config.HardwareInputConfig.RefreshDetectedPorts();
        RefreshHardwareMonitor();
    }

    [RelayCommand]
    public void KeyboardInputTest()
    {
        var keyboard = Simulation.SendInput.Keyboard;
        keyboard.KeyPress(User32.VK.VK_LWIN).Sleep(150);

        foreach (var character in "BetterGI On Top")
        {
            keyboard.TextEntry(character).Sleep(30);
        }

        Config.HardwareInputConfig.RefreshDetectedPorts();
    }

    [RelayCommand]
    public void MouseMoveTest()
    {
        var hardwareConfig = Config.HardwareInputConfig;
        if (!hardwareConfig.IsMouseHardware)
        {
            return;
        }

        var backend = HardwareInputRouter.Instance.GetMouseBackend();
        if (backend == null)
        {
            return;
        }

        backend.MouseMoveBy(100, 100);
        Config.HardwareInputConfig.RefreshDetectedPorts();
    }

    [RelayCommand]
    public void OnEditAvatarMacro()
    {
        JsonMonoDialog.Show(OneKeyFightTask.GetAvatarMacroJsonPath());
    }

    [RelayCommand]
    public void OnGoToOneKeyMacroUrl()
    {
        Process.Start(new ProcessStartInfo("https://bettergi.com/feats/macro/onem.html") { UseShellExecute = true });
    }

    private void RefreshHardwareMonitor()
    {
        RefreshKeyboardMonitor();
        RefreshMouseMonitor();
        RefreshHardwareConnectionInfo();
    }

    private void RefreshHardwareConnectionInfo()
    {
        var keyboardConnectedBaudRate = HardwareInputRouter.Instance.GetKeyboardConnectedBaudRateText() ?? string.Empty;
        var mouseConnectedBaudRate = HardwareInputRouter.Instance.GetMouseConnectedBaudRateText() ?? string.Empty;

        if (string.Equals(_lastKeyboardConnectedBaudRate, keyboardConnectedBaudRate, StringComparison.Ordinal)
            && string.Equals(_lastMouseConnectedBaudRate, mouseConnectedBaudRate, StringComparison.Ordinal))
        {
            return;
        }

        _lastKeyboardConnectedBaudRate = keyboardConnectedBaudRate;
        _lastMouseConnectedBaudRate = mouseConnectedBaudRate;
        Config.HardwareInputConfig.RefreshDetectedPorts();
    }

    private void RefreshKeyboardMonitor()
    {
        var hardwareConfig = Config.HardwareInputConfig;
        if (!hardwareConfig.IsKeyboardHardware)
        {
            IsKeyboardMonitorSupported = false;
            KeyboardMonitorHint = "当前为虚拟键盘输出，监听面板不会读取硬体状态。";
            ResetMonitorItems(KeyboardMonitorItems);
            return;
        }

        var backend = HardwareInputRouter.Instance.GetKeyboardBackend();
        if (backend == null)
        {
            IsKeyboardMonitorSupported = false;
            KeyboardMonitorHint = "未连接到键盘硬体，暂时无法读取按键状态。";
            ResetMonitorItems(KeyboardMonitorItems);
            return;
        }

        if (backend is not IHardwareKeyboardStateBackend stateBackend)
        {
            IsKeyboardMonitorSupported = false;
            KeyboardMonitorHint = "当前键盘 API 暂不支援状态监听。";
            ResetMonitorItems(KeyboardMonitorItems);
            return;
        }

        IsKeyboardMonitorSupported = true;
        KeyboardMonitorHint = "状态映射：0=未触发  1=物理  2=硬体  3=同时";

        foreach (var (item, hidCode) in _keyboardMonitorBindings)
        {
            item.ApplyState(stateBackend.TryGetKeyState(hidCode, out var state) ? state : HardwareInputState.None);
        }
    }

    private void RefreshMouseMonitor()
    {
        var hardwareConfig = Config.HardwareInputConfig;
        if (!hardwareConfig.IsMouseHardware)
        {
            IsMouseMonitorSupported = false;
            MouseMonitorHint = "当前为虚拟滑鼠输出，监听面板不会读取硬体状态。";
            ResetMonitorItems(MouseMonitorItems);
            return;
        }

        var backend = HardwareInputRouter.Instance.GetMouseBackend();
        if (backend == null)
        {
            IsMouseMonitorSupported = false;
            MouseMonitorHint = "未连接到滑鼠硬体，暂时无法读取按键状态。";
            ResetMonitorItems(MouseMonitorItems);
            return;
        }

        if (backend is not IHardwareMouseStateBackend stateBackend)
        {
            IsMouseMonitorSupported = false;
            MouseMonitorHint = "当前滑鼠 API 暂不支援状态监听。";
            ResetMonitorItems(MouseMonitorItems);
            return;
        }

        IsMouseMonitorSupported = true;
        MouseMonitorHint = "状态映射：0=未触发  1=物理  2=硬体  3=同时";

        foreach (var (item, button) in _mouseMonitorBindings)
        {
            item.ApplyState(stateBackend.TryGetButtonState(button, out var state) ? state : HardwareInputState.None);
        }
    }

    private static void ResetMonitorItems(IEnumerable<HardwareInputMonitorItemViewModel> items)
    {
        foreach (var item in items)
        {
            item.ApplyState(HardwareInputState.None);
        }
    }

    private static (HardwareInputMonitorItemViewModel Item, int HidCode) CreateKeyboardMonitor(string label, int hidCode)
    {
        return (new HardwareInputMonitorItemViewModel(label), hidCode);
    }

    private static (HardwareInputMonitorItemViewModel Item, HardwareMouseButton Button) CreateMouseMonitor(string label, HardwareMouseButton button)
    {
        return (new HardwareInputMonitorItemViewModel(label), button);
    }
}
