using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// 悬浮小窗设置页：常用开关、外观、联动、收藏与历史、快捷键。
/// </summary>
public partial class GuideWindowSettingsPage : Page
{
    /// <summary>
    /// 页面 ViewModel。
    /// </summary>
    private GuideWindowSettingsPageViewModel ViewModel { get; }

    /// <summary>
    /// 通过构造函数注入 ViewModel，并设置为 DataContext。
    /// </summary>
    public GuideWindowSettingsPage(GuideWindowSettingsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
