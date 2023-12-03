using MicaSetup.ViewModels;
using System.Windows.Controls;

namespace MicaSetup.Views;

public partial class ShellPage : UserControl
{
    public ShellViewModel ViewModel { get; }

    public ShellPage()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
