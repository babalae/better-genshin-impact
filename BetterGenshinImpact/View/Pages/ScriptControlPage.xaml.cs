using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

public partial class ScriptControlPage
{
    public ScriptControlViewModel ViewModel { get; }

    public ScriptControlPage(ScriptControlViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
    }
}
