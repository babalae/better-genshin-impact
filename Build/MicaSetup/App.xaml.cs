using MicaSetup.Helper;
using System.Windows;

namespace MicaSetup;

public partial class App : Application, IApp
{
    public App()
    {
        InitializeComponent();
    }
}

public interface IApp
{
    public int Run();
}
