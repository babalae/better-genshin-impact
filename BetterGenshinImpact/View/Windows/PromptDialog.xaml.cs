using System.Drawing;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class PromptDialog
{
    public PromptDialog(string question, string title, UIElement uiElement, string defaultValue)
    {
        InitializeComponent();
        MyTitleBar.Title = title;
        TxtQuestion.Text = question;

        DynamicContent.Content = uiElement;
        if (DynamicContent.Content is TextBox textBox)
        {
            textBox.Text = defaultValue;
        }
        else if (DynamicContent.Content is ComboBox comboBox)
        {
            comboBox.Text = defaultValue;
        }

        this.Loaded += PromptDialogLoaded;
    }

    private void PromptDialogLoaded(object sender, RoutedEventArgs e)
    {
        DynamicContent.Focus();
    }

    public static string Prompt(string question, string title, string defaultValue = "")
    {
        var inst = new PromptDialog(question, title, new TextBox(), defaultValue);
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : defaultValue;
    }

    public static string Prompt(string question, string title, UIElement uiElement, string defaultValue = "")
    {
        var inst = new PromptDialog(question, title, uiElement, defaultValue);
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : defaultValue;
    }

    public string ResponseText
    {
        get
        {
            if (DynamicContent.Content is TextBox textBox)
            {
                return textBox.Text;
            }
            else if (DynamicContent.Content is ComboBox comboBox)
            {
                return comboBox.Text;
            }
            return string.Empty;
        }
    }

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
