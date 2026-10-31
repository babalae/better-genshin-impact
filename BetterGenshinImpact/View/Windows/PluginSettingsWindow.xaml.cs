using System;
using System.Windows;
using BetterGenshinImpact.Helpers.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 插件设置窗口：只提供外框，内容为插件的设置 VM，按 VM 类型匹配插件注册的视图。
/// </summary>
public partial class PluginSettingsWindow : FluentWindow
{
    /// <summary>
    /// 创建插件设置窗口。
    /// </summary>
    /// <param name="title">窗口标题。</param>
    /// <param name="settingsViewModel">插件的设置 VM，作为窗口的 DataContext。</param>
    public PluginSettingsWindow(string title, object settingsViewModel)
    {
        Title = title;
        DataContext = settingsViewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// 跟随应用主题启用系统背景。
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e) => WindowHelper.TryApplySystemBackdrop(this);

    /// <summary>
    /// 关闭窗口。设置在修改时已经保存。
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
