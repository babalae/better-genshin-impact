using BetterGenshinImpact.ViewModel.Windows;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class WaypointAdvancedEditorWindow
{
    public WaypointAdvancedEditorWindow(RecordedWaypointViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
