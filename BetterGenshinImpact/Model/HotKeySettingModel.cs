using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fischless.HotkeyCapture;
using System;
using System.Diagnostics;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 在页面展示快捷键配置的对象
/// </summary>
public partial class HotKeySettingModel : ObservableObject
{
    [ObservableProperty] private HotKey _hotKey;


    /// <summary>
    /// 键盘监听、全局热键
    /// </summary>
    [ObservableProperty] private HotKeyTypeEnum _hotKeyType;

    [ObservableProperty]
    private string _hotKeyTypeName;

    public string FunctionName { get; set; }

    public string ConfigPropertyName { get; set; }

    public Action<object?, KeyPressedEventArgs> OnKeyAction { get; set; }

    /// <summary>
    /// 全局热键配置
    /// </summary>
    public HotkeyHook? KeyBindInfo { get; set; }

    public HotKeySettingModel(string functionName, string configPropertyName, string hotkey, string hotKeyTypeCode, Action<object?, KeyPressedEventArgs> onKeyAction)
    {
        FunctionName = functionName;
        ConfigPropertyName = configPropertyName;
        HotKey = HotKey.FromString(hotkey);
        HotKeyType = (HotKeyTypeEnum)Enum.Parse(typeof(HotKeyTypeEnum), hotKeyTypeCode);
        HotKeyTypeName = HotKeyType.ToChineseName();
        OnKeyAction = onKeyAction;
    }

    public void RegisterHotKey()
    {
        if (HotKey.IsEmpty)
        {
            return;
        }

        if (HotKeyType == HotKeyTypeEnum.GlobalRegister)
        {
            try
            {
                Hotkey hotkey = new(HotKey.ToString());

                KeyBindInfo?.Dispose();
                KeyBindInfo = new HotkeyHook();
                KeyBindInfo.KeyPressed -= OnKeyPressed;
                KeyBindInfo.KeyPressed += OnKeyPressed;
                KeyBindInfo.RegisterHotKey(hotkey.ModifierKey, hotkey.Key);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                HotKey = HotKey.None;
            }
        }
        else
        {

        }

    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        OnKeyAction.Invoke(sender, e);
    }

    public void UnRegisterHotKey()
    {
        if (HotKeyType == HotKeyTypeEnum.GlobalRegister)
        {
            KeyBindInfo?.Dispose();
        }
        else
        {

        }
    }

    [RelayCommand]
    public void OnSwitchHotKeyType()
    {
        HotKeyType = HotKeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;
        HotKeyTypeName = HotKeyType.ToChineseName();
    }

}