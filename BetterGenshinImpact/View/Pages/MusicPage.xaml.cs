using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class MusicPage : UserControl
{
    public MusicPageViewModel ViewModel { get; }

    public MusicPage(MusicPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}
