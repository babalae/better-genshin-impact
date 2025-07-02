using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

public partial class SchedulerPage
{
    public SchedulerViewModel ViewModel { get; }

    
    public SchedulerPage(SchedulerViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;

        InitializeComponent();
    }
}