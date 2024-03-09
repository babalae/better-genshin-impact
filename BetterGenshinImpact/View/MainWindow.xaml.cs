using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.ViewModel;
using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray.Controls;

namespace BetterGenshinImpact.View;

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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        App.GetService<NotifyIconViewModel>()?.Exit();
    }

    private void OnNotifyIconLeftDoubleClick(NotifyIcon sender, RoutedEventArgs e)
    {
        App.GetService<NotifyIconViewModel>()?.ShowOrHide();
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
}
