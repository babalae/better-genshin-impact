using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using System.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class JsonMonoDialog : FluentWindow
{
    public JsonMonoViewModel ViewModel { get; }

    public JsonMonoDialog(string path)
    {
        DataContext = ViewModel = new(path);
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        // Manual MVVM binding
        JsonCodeBox.TextChanged += (_, _) => ViewModel.JsonText = JsonCodeBox.Text;
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
