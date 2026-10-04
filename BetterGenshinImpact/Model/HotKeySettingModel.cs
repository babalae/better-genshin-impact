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

    /// <summary>
    /// 最近一次注册失败的原因；注册成功或未配置快捷键时为 null
    /// </summary>
    public string? LastRegisterError { get; private set; }

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

    /// <summary>
    /// 注册快捷键。
    /// </summary>
    /// <returns>注册成功返回 true；失败返回 false 并保留用户当前的设置，由调用方决定是否回滚与提示。</returns>
    public bool RegisterHotKey()
    {
        LastRegisterError = null;

        if (HotKey.IsEmpty)
        {
            return true;
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
                    // 键鼠监听不支持组合键（界面已拦截，这里兜底处理手工编辑过的配置文件）
                    if (HotKey.Modifiers != ModifierKeys.None)
                    {
                        LastRegisterError = I18nService.Instance.Translate("键鼠监听不支持组合键，请重新设置或切换为全局热键");
                        return false;
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

            return true;
        }
        catch (Exception e)
        {
            // 注册失败时不能清空 HotKey：那会让用户「设置完自己变回 < None >」，
            // 且连原来可用的绑定也一起丢失。这里只记录失败原因，交给调用方处理。
            LastRegisterError = HotKeyType == HotKeyTypeEnum.GlobalRegister
                ? I18nService.Instance.Translate("该快捷键已被其它程序占用，或已被其它 BetterGI 实例注册")
                : e.Message;
            Debug.WriteLine(e);

            if (HotKeyType == HotKeyTypeEnum.GlobalRegister)
            {
                GlobalRegisterHook?.Dispose();
                GlobalRegisterHook = null;
            }

            return false;
        }
    }

    /// <summary>
    /// 全局热键按下
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">按键事件参数</param>
    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            return;
        }

        OnKeyPressAction?.Invoke(sender, e);
    }

    /// <summary>
    /// 长按功能在按住期间的持续触发
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">按键事件参数</param>
    private void OnKeyDown(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            return;
        }

        OnKeyDownAction?.Invoke(sender, e);
    }

    /// <summary>
    /// 长按功能松开时结束持续触发
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">按键事件参数</param>
    private void OnKeyUp(object? sender, KeyPressedEventArgs e)
    {
        if (ShouldBlockGlobalRegister())
        {
            ResetBlockedKeyUpState();
            return;
        }

        OnKeyUpAction?.Invoke(sender, e);
    }

    /// <summary>
    /// 聊天界面打开期间需要屏蔽全局热键，避免按键同时被游戏聊天框接收
    /// </summary>
    /// <returns>需要屏蔽时返回 true</returns>
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

    /// <summary>
    /// 注销当前已注册的快捷键并释放 hook
    /// </summary>
    public void UnRegisterHotKey()
    {
        // 置空引用，避免后续误用已释放的对象
        GlobalRegisterHook?.Dispose();
        GlobalRegisterHook = null;
        MouseMonitorHook?.Dispose();
        MouseMonitorHook = null;
        KeyboardMonitorHook?.Dispose();
        KeyboardMonitorHook = null;
    }

    /// <summary>
    /// 在「全局热键」与「键鼠监听」之间切换
    /// </summary>
    [RelayCommand]
    public void OnSwitchHotKeyType()
    {
        HotKeyType = HotKeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
    }

    /// <summary>
    /// 类型变化时同步展示用的类型名
    /// </summary>
    /// <param name="value">新的快捷键类型</param>
    partial void OnHotKeyTypeChanged(HotKeyTypeEnum value)
    {
        HotKeyTypeName = value.ToChineseName();
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
