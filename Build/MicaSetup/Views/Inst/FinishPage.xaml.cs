using MicaSetup.ViewModels;
using System.Windows.Controls;

namespace MicaSetup.Views;

public partial class FinishPage : UserControl
{
    public FinishViewModel ViewModel { get; }

    public FinishPage()
    {
        DataContext = ViewModel = new();
        InitializeComponent();
    }
}
