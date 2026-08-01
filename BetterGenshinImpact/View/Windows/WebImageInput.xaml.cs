using BetterGenshinImpact.ViewModel.Windows;
using System;

namespace BetterGenshinImpact.View.Windows;

public partial class WebImageInput
{
    public WebImageInputViewModel ViewModel { get; }

    public WebImageInput(WebImageInputViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        ViewModel.RequestClose += OnRequestClose;
        Closed += OnClosed;
    }

    private void OnRequestClose() => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        ViewModel.RequestClose -= OnRequestClose;
        ViewModel.CancelDownload();
    }
}
