using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System;

namespace BetterGenshinImpact.View.Windows;

public partial class RecognitionTemplateEditorWindow
{
    public RecognitionTemplateEditorViewModel ViewModel { get; }

    public RecognitionTemplateEditorWindow(RecognitionTemplateEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        ViewModel.RequestClose += OnRequestClose;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.TryApplySystemBackdrop(this);
    }

    private void OnRequestClose()
    {
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel.RequestClose -= OnRequestClose;
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
        ViewModel.Dispose();
    }
}
