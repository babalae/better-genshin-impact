using System.Linq;
using System.Windows;
using BetterGenshinImpact.ViewModel.Pages.Plugins;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 插件设置弹窗的视图层入口：创建 <see cref="PluginSettingsWindow"/> 并以模态方式显示。
/// </summary>
public sealed class PluginSettingsDialogs : IPluginSettingsDialogs
{
    /// <inheritdoc />
    public void Show(string pluginName, object settingsViewModel)
    {
        var window = new PluginSettingsWindow($"{pluginName} 设置", settingsViewModel) { Owner = GetOwner() };
        window.ShowDialog();
    }

    /// <summary>
    /// 优先使用活动窗口，使弹窗显示在打开入口的窗口上方。
    /// </summary>
    private static Window? GetOwner()
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current?.MainWindow;
}
