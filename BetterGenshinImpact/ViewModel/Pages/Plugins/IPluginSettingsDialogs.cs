namespace BetterGenshinImpact.ViewModel.Pages.Plugins;

/// <summary>
/// 插件设置弹窗。ViewModel 只提交插件名称和设置 VM，窗口由视图层创建。
/// </summary>
public interface IPluginSettingsDialogs
{
    /// <summary>
    /// 以模态窗口显示插件设置，关闭窗口后返回。插件设置在修改时由插件自己保存。
    /// </summary>
    /// <param name="pluginName">插件名称，用于窗口标题。</param>
    /// <param name="settingsViewModel">插件的设置 VM，窗口按 VM 类型匹配插件注册的视图。</param>
    void Show(string pluginName, object settingsViewModel);
}
