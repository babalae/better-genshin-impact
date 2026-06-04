using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.ComponentModel;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    public MapViewerViewModel ViewModel { get; }

    public MapViewer(string mapName)
    {
        DataContext = ViewModel = new MapViewerViewModel(mapName);
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        Closing += MapViewer_Closing;
        WeakReferenceMessenger.Default.Register<PropertyChangedMessage<object>>(this, (_, msg) =>
        {
            if (msg.PropertyName == "OpenWaypointAdvancedEditor" && msg.NewValue is RecordedWaypointViewModel waypoint)
            {
                var window = new WaypointAdvancedEditorWindow(waypoint)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
        });
    }

    private void TitleSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsViewSettingsOpen = !ViewModel.IsViewSettingsOpen;
    }

    private void ViewSettingsPopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel.IsViewSettingsOpen = false;
    }

    private void MapViewer_Closing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.ConfirmJsonEditsBeforeLeavingRecorder(this))
        {
            e.Cancel = true;
            return;
        }

        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
