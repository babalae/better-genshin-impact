using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.ViewModel;
using System;
using System.Windows;
using System.Windows.Media;
using System.Drawing;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow, INavigationWindow
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel, IPageService pageService, INavigationService navigationService)
    {
        DataContext = ViewModel = viewModel;

        InitializeComponent();
        this.InitializeDpiAwareness();

        SetPageService(pageService);
        navigationService.SetNavigationControl(RootNavigation);

        Application.Current.MainWindow = this;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryApplySystemBackdrop();
    }

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }

    public void SetPageService(IPageService pageService)
    {
        RootNavigation.SetPageService(pageService);
    }

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    private void TryApplySystemBackdrop()
    {
        if (WindowBackdrop.IsSupported(WindowBackdropType.Mica))
        {
            Background = new SolidColorBrush(Colors.Transparent);
            WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Mica);
            return;
        }

        if (WindowBackdrop.IsSupported(WindowBackdropType.Tabbed))
        {
            Background = new SolidColorBrush(Colors.Transparent);
            WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Tabbed);
            return;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        this.TrayContextMenu.DataContext = ViewModel;
    }
}
