using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Windows;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model;

/// <summary>
/// 在页面展示快捷键配置的对象
/// </summary>
public partial class HotKeySettingModel : ObservableObject
{
    [ObservableProperty] private HotKey _hotKey;

    public string FunctionName { get; set; }

    public string ConfigPropertyName { get; set; }

    public Action<mrousavy.HotKey> OnKeyAction { get; set; }

    public mrousavy.HotKey? KeyBindInfo { get; set; }

    public HotKeySettingModel(string functionName, string configPropertyName, string hotkey, Action<mrousavy.HotKey> onKeyAction)
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
            KeyBindInfo = new mrousavy.HotKey(
                HotKey.Modifiers,
                HotKey.Key,
                UIDispatcherHelper.MainWindow,
                OnKeyAction
            );
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            HotKey = HotKey.None;
        }

    }

    public void UnRegisterHotKey()
    {
        KeyBindInfo?.Dispose();
    }
}