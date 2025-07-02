using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Grid = System.Windows.Controls.Grid;
using StackPanel = System.Windows.Controls.StackPanel;

namespace BetterGenshinImpact.View.Controls;

public class WpfUiWindow : FluentWindow
{
    public ContentControl DynamicContent { get; set; }

    public WpfUiWindow(ContentControl dynamicContent)
    {
        DynamicContent = dynamicContent;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        MinWidth = 400;
        MinHeight = 200;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var stackPanel = new StackPanel { Margin = new Thickness(12) };
        Grid.SetRow(stackPanel, 1);

        stackPanel.Children.Add(DynamicContent);

        grid.Children.Add(stackPanel);

        var titleBar = new TitleBar
        {
            Icon = new ImageIcon { Source = new BitmapImage(new Uri(@"pack://application:,,,/Resources/Images/logo.png", UriKind.Absolute)) }
        };
        Grid.SetRow(titleBar, 0);
        grid.Children.Add(titleBar);

        Content = grid;
    }
}
