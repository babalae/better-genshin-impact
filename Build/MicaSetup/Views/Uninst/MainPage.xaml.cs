using MicaSetup.ViewModels;
using System.Windows.Controls;

namespace MicaSetup.Views;

public partial class MainPage : UserControl
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
