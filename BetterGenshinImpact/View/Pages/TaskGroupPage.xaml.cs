using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages;

public partial class TaskGroupPage
{
    public TaskGroupPage(TaskGroupPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}