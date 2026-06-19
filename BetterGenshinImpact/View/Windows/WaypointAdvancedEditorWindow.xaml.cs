using BetterGenshinImpact.ViewModel.Windows;
using System.Windows;
using System.Windows.Controls;

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

    private void OpenCustomFieldPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordedWaypointViewModel viewModel)
        {
            return;
        }

        var dialog = new WaypointCustomFieldPickerWindow(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void PrepareCustomFieldDefinitionButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordedWaypointViewModel viewModel
            || sender is not Button { Tag: WaypointCustomFieldViewModel field })
        {
            return;
        }

        viewModel.PrepareCustomFieldDefinitionCommand.Execute(field);
        var dialog = new WaypointCustomFieldPickerWindow(viewModel, refreshOnOpen: false)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
