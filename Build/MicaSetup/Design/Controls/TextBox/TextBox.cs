using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace MicaSetup.Design.Controls;

public class TextBoxEx : TextBox
{
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(TextBoxEx),
        new PropertyMetadata(string.Empty)
    );

    public static readonly DependencyProperty PlaceholderEnabledProperty = DependencyProperty.Register(
        nameof(PlaceholderEnabled),
        typeof(bool),
        typeof(TextBoxEx),
        new PropertyMetadata(true)
    );

    public static readonly DependencyProperty IsTextSelectionEnabledProperty = DependencyProperty.Register(
        nameof(IsTextSelectionEnabled),
        typeof(bool),
        typeof(TextBoxEx),
        new PropertyMetadata(false)
    );

    public static readonly DependencyProperty TemplateButtonCommandProperty = DependencyProperty.Register(
        nameof(TemplateButtonCommand),
        typeof(IRelayCommand),
        typeof(TextBoxEx),
        new PropertyMetadata(null)
    );

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool PlaceholderEnabled
    {
        get => (bool)GetValue(PlaceholderEnabledProperty);
        set => SetValue(PlaceholderEnabledProperty, value);
    }

    /// <summary>
    /// TODO
    /// </summary>
    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty);
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    public IRelayCommand TemplateButtonCommand => (IRelayCommand)GetValue(TemplateButtonCommandProperty);

    public TextBoxEx()
    {
        SetValue(TemplateButtonCommandProperty, new RelayCommand<string>(OnTemplateButtonClick));
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);

        if (PlaceholderEnabled && Text.Length > 0)
            PlaceholderEnabled = false;

        if (!PlaceholderEnabled && Text.Length < 1)
            PlaceholderEnabled = true;
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);

        CaretIndex = Text.Length;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
    }

    protected virtual void OnClearButtonClick()
    {
        if (Text.Length > 0)
        {
            Text = string.Empty;
        }
    }

    protected virtual void OnTemplateButtonClick(string? parameter)
    {
        Debug.WriteLine($"INFO: {typeof(TextBoxEx)} button clicked");
        OnClearButtonClick();
    }
}
