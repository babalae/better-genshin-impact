using MapPathingDevViewModel = BetterGenshinImpact.ViewModel.Windows.MapPathingDevViewModel;

namespace BetterGenshinImpact.View.Windows;

public partial class MapPathingDevWindow
{
    private MapPathingDevViewModel ViewModel { get; }
    
    public MapPathingDevWindow()
    {
        DataContext = ViewModel = new MapPathingDevViewModel();
        InitializeComponent();
    }
}