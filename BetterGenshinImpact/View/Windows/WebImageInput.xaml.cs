using BetterGenshinImpact.ViewModel.Windows;
namespace BetterGenshinImpact.View.Windows;

public partial class WebImageInput
{
    public WebImageInputViewModel ViewModel { get; }

    public WebImageInput(WebImageInputViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        ViewModel.RequestClose += () => Close();
        InitializeComponent();
    }
}