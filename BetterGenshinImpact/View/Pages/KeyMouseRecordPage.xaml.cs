using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class KeyMouseRecordPage : UserControl
{
    private KeyMouseRecordPageViewModel ViewModel { get; }

    public KeyMouseRecordPage(KeyMouseRecordPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
