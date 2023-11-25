using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Windows;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.HotkeyCapture;
using Gma.System.MouseKeyHook.HotKeys;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 在页面展示快捷键配置的对象
/// </summary>
public partial class HotKeySettingModel : ObservableObject
{
    [ObservableProperty] private HotKey _hotKey;

    public string FunctionName { get; set; }

    public string ConfigPropertyName { get; set; }

    public Action<object?, KeyPressedEventArgs> OnKeyAction { get; set; }

    public HotkeyHook? KeyBindInfo { get; set; }

    public HotKeySettingModel(string functionName, string configPropertyName, string hotkey, Action<object?, KeyPressedEventArgs> onKeyAction)
    {
        FunctionName = functionName;
        ConfigPropertyName = configPropertyName;
        HotKey = HotKey.FromString(hotkey);
        OnKeyAction = onKeyAction;
    }

    public void RegisterHotKey()
    {
        if (HotKey.IsEmpty)
        {
            return;
        }

        try
        {
            Fischless.HotkeyCapture.Hotkey hotkey = new(HotKey.ToString());

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

    private  void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        OnKeyAction.Invoke(sender, e);
    }

    public void UnRegisterHotKey()
    {
        KeyBindInfo?.Dispose();
    }
}