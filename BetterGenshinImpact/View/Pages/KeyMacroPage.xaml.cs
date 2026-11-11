using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// 按键宏页面：管理和编辑不识图的键鼠宏
/// </summary>
public partial class KeyMacroPage : UserControl
{
    /// <summary>
    /// 页面 ViewModel
    /// </summary>
    public KeyMacroPageViewModel ViewModel { get; }

    /// <summary>
    /// 创建页面，ViewModel 由容器注入
    /// </summary>
    public KeyMacroPage(KeyMacroPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
