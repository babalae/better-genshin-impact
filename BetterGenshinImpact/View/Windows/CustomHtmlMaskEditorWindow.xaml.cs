using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Windows;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class CustomHtmlMaskEditorWindow : FluentWindow
{
    public const string WindowTag = nameof(CustomHtmlMaskEditorWindow);

    public CustomHtmlMaskEditorViewModel ViewModel { get; }

    public CustomHtmlMaskEditorWindow(CustomHtmlMaskService customHtmlMaskService, MaskWindowConfig maskWindowConfig)
    {
        DataContext = ViewModel = new CustomHtmlMaskEditorViewModel(customHtmlMaskService, maskWindowConfig);
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.ConfirmClose(this))
        {
            e.Cancel = true;
            return;
        }

        ViewModel.ClosePreview();
    }

    public static void ShowEditor(CustomHtmlMaskService customHtmlMaskService, MaskWindowConfig maskWindowConfig)
    {
        var existing = Application.Current.Windows
            .Cast<Window>()
            .FirstOrDefault(window => window.Tag?.Equals(WindowTag) ?? false);
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new CustomHtmlMaskEditorWindow(customHtmlMaskService, maskWindowConfig)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }
}
