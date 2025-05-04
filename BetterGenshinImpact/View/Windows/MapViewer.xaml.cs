using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    public MapViewerViewModel ViewModel { get; }

    public MapViewer(string mapName)
    {
        DataContext = ViewModel = new MapViewerViewModel(mapName);
        InitializeComponent();
    }
}
