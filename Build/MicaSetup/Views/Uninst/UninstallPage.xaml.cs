using MicaSetup.ViewModels;
using System.Windows.Controls;

namespace MicaSetup.Views;

public partial class UninstallPage : UserControl
{
    public UninstallViewModel ViewModel { get; }

    public UninstallPage()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
