using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages.View;

namespace BetterGenshinImpact.View.Pages.View;

public partial class HardwareAccelerationView : UserControl
{
    private HardwareAccelerationViewModel ViewModel { get; }

    public HardwareAccelerationView(HardwareAccelerationViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}