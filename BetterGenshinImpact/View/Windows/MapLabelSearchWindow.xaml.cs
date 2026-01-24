using BetterGenshinImpact.ViewModel;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class MapLabelSearchWindow : FluentWindow
{
    public MapLabelSearchWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    public void AttachViewModel(MaskWindowViewModel viewModel)
    {
        DataContext = viewModel;
    }

    public void FocusSearch()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
