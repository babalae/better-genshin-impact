using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// JsListPage.xaml 的交互逻辑
/// </summary>
public partial class JsListPage : Page
{
    private JsListViewModel ViewModel { get; }

    public JsListPage(JsListViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
