using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>账号记录的「狛荷屋」页面，当前展示空内容占位画面。</summary>
public partial class AccountRecordsPage : UserControl
{
    /// <summary>页面使用的账号记录视图模型。</summary>
    public AccountRecordsPageViewModel ViewModel { get; }

    /// <summary>注入视图模型并初始化页面。</summary>
    public AccountRecordsPage(AccountRecordsPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
