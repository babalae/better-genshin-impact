using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class RouteGraphStudioWindow
{
    public RouteGraphStudioViewModel ViewModel { get; }

    public RouteGraphStudioWindow(
        string? graphDirectory = null,
        string? initialMapName = null,
        RouteGraphPoint? currentTargetPoint = null)
    {
        DataContext = ViewModel = new RouteGraphStudioViewModel(graphDirectory, initialMapName, currentTargetPoint);
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }
}
