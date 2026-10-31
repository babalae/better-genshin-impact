using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages.Plugins;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// 插件页：功能选择与已安装插件。
/// </summary>
public partial class PluginsPage : Page
{
    /// <summary>
    /// 页面 ViewModel。
    /// </summary>
    private PluginsPageViewModel ViewModel { get; }

    /// <summary>
    /// 通过构造函数注入 ViewModel，并设置为 DataContext。
    /// </summary>
    public PluginsPage(PluginsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
