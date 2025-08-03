using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 对话框配置类，用于控制对话框中的元素显示
/// </summary>
public class PromptDialogConfig
{
    /// <summary>
    /// 是否显示左下角按钮
    /// </summary>
    public bool ShowLeftButton { get; set; } = false;

    /// <summary>
    /// 左下角按钮的文本
    /// </summary>
    public string LeftButtonText { get; set; } = "左下角按钮";

    /// <summary>
    /// 左下角按钮的点击事件
    /// </summary>
    public RoutedEventHandler? LeftButtonClick { get; set; }
}

public partial class PromptDialog
{
    private readonly PromptDialogConfig _config;

    public PromptDialog(string question, string title, UIElement uiElement, string? defaultValue, PromptDialogConfig? config = null)
    {
        InitializeComponent();
        MyTitleBar.Title = title;
        TxtQuestion.Text = question;
        _config = config ?? new PromptDialogConfig();

        DynamicContent.Content = uiElement;
        if (DynamicContent.Content is TextBox textBox && defaultValue != null)
        {
            textBox.Text = defaultValue;
        }
        else if (DynamicContent.Content is ComboBox comboBox && defaultValue != null)
        {
            comboBox.Text = defaultValue;
        }

        // 配置左下角按钮
        ConfigureLeftButton();

        this.Loaded += PromptDialogLoaded;
    }

    private void ConfigureLeftButton()
    {
        if (_config.ShowLeftButton)
        {
            BtnLeftBottom.Content = _config.LeftButtonText;
            if (_config.LeftButtonClick != null)
            {
                BtnLeftBottom.Click += _config.LeftButtonClick;
            }
        }
        else
        {
            BtnLeftBottom.Visibility = Visibility.Collapsed;
        }
    }

    private void PromptDialogLoaded(object sender, RoutedEventArgs e)
    {
        DynamicContent.Focus();
    }

    public static string Prompt(string question, string title, string defaultValue = "", PromptDialogConfig? config = null)
    {
        var textBox = new TextBox
        {
            VerticalAlignment = VerticalAlignment.Top,
        };
        var inst = new PromptDialog(question, title, textBox, defaultValue, config);
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : "";
    }

    public static string Prompt(string question, string title, UIElement uiElement, string defaultValue = "", PromptDialogConfig? config = null)
    {
        var inst = new PromptDialog(question, title, uiElement, defaultValue, config);
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : "";
    }

    public static string Prompt(string question, string title, UIElement uiElement, Size size, PromptDialogConfig? config = null)
    {
        var inst = new PromptDialog(question, title, uiElement, "", config)
        {
            Width = size.Width,
            Height = size.Height 
        };
        inst.ShowDialog();
        return inst.DialogResult == true ? inst.ResponseText : "";
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
            else
            {
                return "true";
            }
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
