using BetterGenshinImpact.ViewModel.Windows;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class WaypointCustomFieldPickerWindow
{
    public WaypointCustomFieldPickerWindow(RecordedWaypointViewModel viewModel, bool refreshOnOpen = true)
    {
        DataContext = viewModel;
        InitializeComponent();

        if (refreshOnOpen)
        {
            viewModel.OpenCustomFieldPickerCommand.Execute(null);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordedWaypointViewModel viewModel)
        {
            viewModel.CancelAddCustomFieldsCommand.Execute(null);
        }

        Close();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecordedWaypointViewModel viewModel)
        {
            viewModel.ConfirmAddSelectedCustomFieldsCommand.Execute(null);
        }

        Close();
    }
}
