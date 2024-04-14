using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace BetterGenshinImpact.View.Windows;

public partial class JsonMonoDialog : FluentWindow
{
    public JsonMonoViewModel ViewModel { get; }

    public JsonMonoDialog(string path)
    {
        DataContext = ViewModel = new(path);
        InitializeComponent();

        // Manual MVVM binding
        JsonCodeBox.TextChanged += (_, _) => ViewModel.JsonText = JsonCodeBox.Text;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryApplySystemBackdrop();
    }

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
        }
    }

    public static void Show(string path)
    {
        JsonMonoDialog dialog = new(path)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.Show();
    }
}
