using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class TravelLogWindow
{
    public TravelLogWindowViewModel ViewModel { get; }

    public TravelLogWindow()
    {
        DataContext = ViewModel = new TravelLogWindowViewModel();
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        Closed += (s, e) => ViewModel.Dispose();
    }
}
