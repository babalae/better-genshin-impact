using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.Service.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.HotkeyCapture;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Input;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 在页面展示快捷键配置的对象
/// </summary>
public partial class HotKeySettingModel : ObservableObject
{
    /// <summary>所有入口共用的注册所有权，避免全局热键与键鼠监听绑定同一个键后重复执行。</summary>
    private static readonly HashSet<HotKeySettingModel> _registeredHotkeys = [];
    /// <summary>共享注册表的同步锁；注册与注销仍须由热键所属 UI 线程调用。</summary>
    private static readonly object _registrationLock = new();
    /// <summary>是否禁止系统按住热键时重复发送全局热键消息，默认保留原功能行为。</summary>
    public bool SuppressRepeat { get; set; }
    /// <summary>最近一次注册失败原因，调用方可展示冲突而不吞掉错误。</summary>
    public string? RegistrationError { get; private set; }
    [ObservableProperty] private HotKey _hotKey;

    /// <summary>
    /// 键鼠监听、全局热键
    /// </summary>
    [ObservableProperty] private HotKeyTypeEnum _hotKeyType;

    [ObservableProperty] private string _hotKeyTypeName;

    public string LocalizedHotKeyTypeName => HotKeyType.ToLocalizedName();

    [ObservableProperty]
    private ObservableCollection<HotKeySettingModel> _children = [];

    public string FunctionName { get; set; }

    public string LocalizedFunctionName => I18nService.Instance.Translate(FunctionName);

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
        PropertyChangedEventManager.AddHandler(I18nService.Instance, OnI18nPropertyChanged, nameof(I18nService.Revision));
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
        PropertyChangedEventManager.AddHandler(I18nService.Instance, OnI18nPropertyChanged, nameof(I18nService.Revision));
    }

    /// <summary>使用软件统一注册入口，检查跨类型冲突并保留实际钩子的所有权。</summary>
    public void RegisterHotKey()
    {
        RegistrationError = null;
        if (HotKey.IsEmpty)
        {
            return;
        }

        try
        {
            UnRegisterHotKey();
            lock (_registrationLock)
            {
                if (_registeredHotkeys.Any(model => AreConflicting(model.HotKey, model.HotKeyType, HotKey, HotKeyType)))
                    throw new InvalidOperationException("此快捷键已由其他功能或任务计划使用。");
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
                    GlobalRegisterHook.RegisterHotKey(SuppressRepeat
                        ? hotkey.ModifierKey | User32.HotKeyModifiers.MOD_NOREPEAT : hotkey.ModifierKey, hotkey.Key);
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

                        KeyboardMonitorHook.RegisterHotKey((Keys)KeyInterop.VirtualKeyFromKey(HotKey.Key));
                    }
                }
                _registeredHotkeys.Add(this);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            // 失败的候选钩子不能留在监听表，也不能注销已占用相同键的其他功能。
            UnRegisterHotKey();
            RegistrationError = e.Message;
            HotKey = HotKey.None;
        }
    }

    /// <summary>按实际监听规则比较冲突；单键监听不检查修饰键，因此也与同主键的全局组合键冲突。</summary>
    public static bool AreConflicting(HotKey first, HotKeyTypeEnum firstType, HotKey second, HotKeyTypeEnum secondType)
    {
        if (first.IsEmpty || second.IsEmpty) return false;
        if (first.MouseButton != MouseButton.Left || second.MouseButton != MouseButton.Left)
            return first.MouseButton == second.MouseButton;
        return first.Key == second.Key && (first.Modifiers == second.Modifiers
            || firstType == HotKeyTypeEnum.KeyboardMonitor || secondType == HotKeyTypeEnum.KeyboardMonitor);
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

    /// <summary>释放本模型拥有的注册，重复注销也不会影响其他入口的快捷键。</summary>
    public void UnRegisterHotKey()
    {
        lock (_registrationLock) _registeredHotkeys.Remove(this);
        // 先撤掉所有权引用，再释放各类钩子；即使 NativeWindow 清理失败，也继续清理监听。
        var global = GlobalRegisterHook;
        var mouse = MouseMonitorHook;
        var keyboard = KeyboardMonitorHook;
        GlobalRegisterHook = null;
        MouseMonitorHook = null;
        KeyboardMonitorHook = null;
        try { global?.Dispose(); }
        finally
        {
            try { mouse?.Dispose(); }
            finally { keyboard?.Dispose(); }
        }
    }

    [RelayCommand]
    public void OnSwitchHotKeyType()
    {
        HotKeyType = HotKeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
        HotKeyTypeName = HotKeyType.ToChineseName();
        OnPropertyChanged(nameof(LocalizedHotKeyTypeName));
    }

    private void OnI18nPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(I18nService.Revision))
        {
            return;
        }

        OnPropertyChanged(nameof(LocalizedFunctionName));
        OnPropertyChanged(nameof(LocalizedHotKeyTypeName));
    }
}
