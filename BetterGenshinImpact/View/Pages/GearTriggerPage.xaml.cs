using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

/// <summary>
/// GearTriggerPage.xaml 的交互逻辑
/// </summary>
public partial class GearTriggerPage
{
    public GearTriggerPageViewModel ViewModel { get; }

    public GearTriggerPage(GearTriggerPageViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}