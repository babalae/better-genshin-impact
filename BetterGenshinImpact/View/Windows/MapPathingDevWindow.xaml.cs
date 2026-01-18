using BetterGenshinImpact.Helpers.Ui;
using MapPathingDevViewModel = BetterGenshinImpact.ViewModel.Windows.MapPathingDevViewModel;

namespace BetterGenshinImpact.View.Windows;

public partial class MapPathingDevWindow
{
    private MapPathingDevViewModel ViewModel { get; }
    
    public MapPathingDevWindow()
    {
        DataContext = ViewModel = new MapPathingDevViewModel();
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
    }
}