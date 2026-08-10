using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System;

namespace BetterGenshinImpact.View.Windows;

public partial class MapViewer
{
    public MapViewerViewModel ViewModel { get; }

    public MapViewer(string mapName)
    {
        DataContext = ViewModel = new MapViewerViewModel(mapName);
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Dispose();
        DataContext = null;
        base.OnClosed(e);
    }
}
