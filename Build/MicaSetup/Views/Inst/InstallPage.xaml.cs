using MicaSetup.ViewModels;
using System.Windows.Controls;

namespace MicaSetup.Views;

public partial class InstallPage : UserControl
{
    public InstallViewModel ViewModel { get; }

    public InstallPage()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
