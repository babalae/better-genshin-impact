using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Fischless.HotkeyCapture;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Input;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 在页面展示快捷键配置的对象
/// </summary>
public partial class HotKeySettingModel : ObservableObject
{
    [ObservableProperty] private HotKey _hotKey;

    /// <summary>
    /// 键鼠监听、全局热键
    /// </summary>
    [ObservableProperty] private HotKeyTypeEnum _hotKeyType;

    [ObservableProperty] private string _hotKeyTypeName;

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _children = [];

    public string FunctionName { get; set; }

    public bool IsExpanded => true;

    /// <summary>
    /// 界面上显示是文件夹而不是快捷键
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    public string ConfigPropertyName { get; set; }

    public Action<object?, KeyPressedEventArgs>? OnKeyPressAction { get; set; }
    public Action<object?, KeyPressedEventArgs>? OnKeyDownAction { get; set; }
    public Action<object?, KeyPressedEventArgs>? OnKeyUpAction { get; set; }

    public bool IsHold { get; set; }

    [ObservableProperty] private bool _switchHotkeyTypeEnabled;

    /// <summary>
    /// 全局热键配置
    /// </summary>
    public HotkeyHook? GlobalRegisterHook { get; set; }

    /// <summary>
    /// 键盘监听配置
    /// </summary>
    public KeyboardHook? KeyboardMonitorHook { get; set; }

    /// <summary>
    /// 鼠标监听配置
    /// </summary>
    public MouseHook? MouseMonitorHook { get; set; }

    public HotKeySettingModel(string functionName)
    {
        FunctionName = functionName;
        IsDirectory = true;
    }

    public HotKeySettingModel(string functionName, string configPropertyName, string hotkey, string hotKeyTypeCode, Action<object?, KeyPressedEventArgs>? onKeyPressAction, bool isHold = false)
    {
        FunctionName = functionName;
        ConfigPropertyName = configPropertyName;
        HotKey = HotKey.FromString(hotkey);
        HotKeyType = (HotKeyTypeEnum)Enum.Parse(typeof(HotKeyTypeEnum), hotKeyTypeCode);
        HotKeyTypeName = HotKeyType.ToChineseName();
        OnKeyPressAction = onKeyPressAction;
        IsHold = isHold;
        SwitchHotkeyTypeEnabled = !isHold;

        // 初始化遮罩显示勾选状态：直接写 backing field，避免触发 OnShowOnOverlayChanged 把初始值写回配置
        try
        {
            _showOnOverlay = TaskContext.Instance().Config.MaskWindowConfig.IsOverlayHotkeyEnabled(configPropertyName);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            _showOnOverlay = false;
        }
    }

    /// <summary>
    /// 是否在遮罩快捷键速查条上显示该快捷键。
    /// 持久化到 MaskWindowConfig.OverlayHotkeyItems（key 为 ConfigPropertyName）。
    /// 注意：目录行走单参构造，ConfigPropertyName 为 null，属性恒为 false 且变更被忽略。
    /// </summary>
    [ObservableProperty]
    private bool _showOnOverlay;

    partial void OnShowOnOverlayChanged(bool value)
    {
        // 目录行/未携带配置属性名的行：防御性忽略，避免 NullReferenceException
        // （TreeListView 会为目录行实例化同一个 CellTemplate）
        if (IsDirectory || string.IsNullOrEmpty(ConfigPropertyName))
        {
            return;
        }

        try
        {
            TaskContext.Instance().Config.MaskWindowConfig.SetOverlayHotkeyEnabled(ConfigPropertyName, value);
            // 通知遮罩重算速查条。勾选是离散点击操作，无需防抖；
            // 遮罩侧 handler 已通过 UIDispatcherHelper.Invoke 回到 UI 线程，线程安全。
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "RefreshSettings", value, "快捷键遮罩显示变更"));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    public void RegisterHotKey()
    {
        if (HotKey.IsEmpty)
        {
            return;
        }

        try
        {
            if (HotKeyType == HotKeyTypeEnum.GlobalRegister)
            {
                Hotkey hotkey = new(HotKey.ToString());
                GlobalRegisterHook?.Dispose();
                GlobalRegisterHook = new HotkeyHook();
                if (OnKeyPressAction != null)
                {
                    GlobalRegisterHook.KeyPressed -= OnKeyPressed;
                    GlobalRegisterHook.KeyPressed += OnKeyPressed;
                }
                GlobalRegisterHook.RegisterHotKey(hotkey.ModifierKey, hotkey.Key);
            }
            else
            {
                MouseMonitorHook?.Dispose();
                KeyboardMonitorHook?.Dispose();
                if (HotKey.MouseButton is MouseButton.XButton1 or MouseButton.XButton2)
                {
                    MouseMonitorHook = new MouseHook
                    {
                        IsHold = IsHold,
                        ConfigPropertyName = ConfigPropertyName
                    };

                    if (OnKeyPressAction != null)
                    {
                        MouseMonitorHook.MousePressed -= OnKeyPressed;
                        MouseMonitorHook.MousePressed += OnKeyPressed;
                    }
                    if (OnKeyDownAction != null)
                    {
                        MouseMonitorHook.MouseDownEvent -= OnKeyDown;
                        MouseMonitorHook.MouseDownEvent += OnKeyDown;
                    }
                    if (OnKeyUpAction != null)
                    {
                        MouseMonitorHook.MouseUpEvent -= OnKeyUp;
                        MouseMonitorHook.MouseUpEvent += OnKeyUp;
                    }
                    MouseMonitorHook.RegisterHotKey((MouseButtons)Enum.Parse(typeof(MouseButtons), HotKey.MouseButton.ToString()));
                }
                else
                {
                    // 如果是组合键，不支持
                    if (HotKey.Modifiers != ModifierKeys.None)
                    {
                        HotKey = HotKey.None;
                        return;
                    }
                    KeyboardMonitorHook = new KeyboardHook
                    {
                        IsHold = IsHold,
                        ConfigPropertyName = ConfigPropertyName
                    };
                    if (OnKeyPressAction != null)
                    {
                        KeyboardMonitorHook.KeyPressedEvent -= OnKeyPressed;
                        KeyboardMonitorHook.KeyPressedEvent += OnKeyPressed;
                    }
                    if (OnKeyDownAction != null)
                    {
                        KeyboardMonitorHook.KeyDownEvent -= OnKeyDown;
                        KeyboardMonitorHook.KeyDownEvent += OnKeyDown;
                    }
                    if (OnKeyUpAction != null)
                    {
                        KeyboardMonitorHook.KeyUpEvent -= OnKeyUp;
                        KeyboardMonitorHook.KeyUpEvent += OnKeyUp;
                    }

                    KeyboardMonitorHook.RegisterHotKey((Keys)Enum.Parse(typeof(Keys), HotKey.Key.ToString()));
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            HotKey = HotKey.None;
        }
    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            return;
        }

        OnKeyPressAction?.Invoke(sender, e);
    }

    private void OnKeyDown(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            return;
        }

        OnKeyDownAction?.Invoke(sender, e);
    }

    private void OnKeyUp(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            ResetBlockedKeyUpState();
            return;
        }

        OnKeyUpAction?.Invoke(sender, e);
    }

    private bool ShouldBlockGlobalRegister()
    {
        return HotKeyType == HotKeyTypeEnum.GlobalRegister && ChatUiHotkeyGuard.ShouldBlockHotkey(ConfigPropertyName);
    }

    private void ResetBlockedKeyUpState()
    {
        if (string.Equals(ConfigPropertyName, nameof(HotKeyConfig.OneKeyFightHotkey), StringComparison.Ordinal))
        {
            OneKeyFightTask.Instance.KeyUp();
        }
    }

    public void UnRegisterHotKey()
    {
        GlobalRegisterHook?.Dispose();
        MouseMonitorHook?.Dispose();
        KeyboardMonitorHook?.Dispose();
    }

    [RelayCommand]
    public void OnSwitchHotKeyType()
    {
        HotKeyType = HotKeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
        HotKeyTypeName = HotKeyType.ToChineseName();
    }
}
