using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class PromptDialog
{
    public string DialogTitle { get; set; }

    public PromptDialog(string question, string title, string defaultValue = "")
    {
        InitializeComponent();
        DialogTitle = title;
        TxtQuestion.Text = question;
        TxtResponse.Text = defaultValue;
        this.Loaded += PromptDialogLoaded;
    }

    private void PromptDialogLoaded(object sender, RoutedEventArgs e)
    {
        TxtResponse.Focus();
    }

    public static string Prompt(string question, string title, string defaultValue = "")
    {
        var inst = new PromptDialog(question, title, defaultValue);
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : defaultValue;
    }

    public string ResponseText => TxtResponse.Text;

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
