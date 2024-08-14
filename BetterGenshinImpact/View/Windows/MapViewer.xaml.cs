using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    public MapViewerViewModel ViewModel { get; }

    public MapViewer()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
