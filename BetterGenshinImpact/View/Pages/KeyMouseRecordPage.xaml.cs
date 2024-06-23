using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// KeyMouseRecordPage.xaml 的交互逻辑
/// </summary>
public partial class KeyMouseRecordPage : Page
{
    private KeyMouseRecordPageViewModel ViewModel { get; }

    public KeyMouseRecordPage(KeyMouseRecordPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
