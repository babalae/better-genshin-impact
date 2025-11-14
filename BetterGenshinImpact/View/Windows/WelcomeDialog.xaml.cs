using BetterGenshinImpact.Helpers.Ui;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Windows.System;

namespace BetterGenshinImpact.View.Windows;

public partial class WelcomeDialog
{
    public WelcomeDialog()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
        this.Loaded += WelcomeDialogLoaded;
    }

    private void WelcomeDialogLoaded(object sender, RoutedEventArgs e)
    {

    }

    public static void Prompt(string question, string title, string defaultValue = "")
    {
        var inst = new WelcomeDialog();
        inst.ShowDialog();
    }

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void HyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        string uri = e.Uri.AbsoluteUri;
        Launcher.LaunchUriAsync(new Uri(uri));
    }
}
