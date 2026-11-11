using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// Pulonia 账户页：本实例当前账户、账户列表与账户详情，取代原账户管理窗口。
/// </summary>
public partial class PuloniaAccountsPage : UserControl
{
    /// <summary>页面使用的账户页视图模型。</summary>
    public PuloniaAccountsPageViewModel ViewModel { get; }

    /// <summary>
    /// 建立页面并使用页面自身作为绑定根，与其他 Pulonia 页面一致。
    /// </summary>
    /// <param name="viewModel">账户页视图模型</param>
    public PuloniaAccountsPage(PuloniaAccountsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
