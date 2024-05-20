using MicaSetup.Design.Controls;
using MicaSetup.Helper;
using System.ComponentModel;
using System.Windows.Media;

namespace MicaSetup.Views;

public partial class MainWindow : WindowX
{
    public static ImageSource? Favicon => new ImageSourceConverter().ConvertFromString($"pack://application:,,,/MicaSetup;component/Resources/Images/Favicon{(Option.Current.IsUninst ? "Uninst" : "Setup")}.ico") as ImageSource;
    public static string SetupName => Option.Current.SetupName;

    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (Option.Current.IsUninst)
        {
            if (Option.Current.Uninstalling)
            {
                e.Cancel = true;
                _ = MessageBoxX.Info(this, Mui("UninstNotCompletedTips"));
            }
        }
        else
        {
            if (Option.Current.Installing)
            {
                if (MessageBoxX.Question(this, Mui("InstNotCompletedTips")) != WindowDialogResult.Yes)
                {
                    e.Cancel = true;
                }
            }
        }

        if (!e.Cancel)
        {
            TempPathForkHelper.Clean();
        }
    }
}
