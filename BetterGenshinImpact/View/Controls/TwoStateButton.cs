using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Controls;

public class TwoStateButton : Button
{
    private bool _isEnabled;

    public TwoStateButton()
    {
        if (TryFindResource(typeof(Button)) is Style style)
        {
            Style = style;
        }

        Click += OnClick;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Content = EnableContent;
        Icon = EnableIcon;
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (_isEnabled)
        {
            DisableCommand?.Execute(null);
            Content = EnableContent;
            Icon = EnableIcon;
        }
        else
        {
            EnableCommand?.Execute(null);
            Content = DisableContent;
            Icon = DisableIcon;
        }
        _isEnabled = !_isEnabled;
    }

    private static readonly DependencyProperty EnableContentProperty = DependencyProperty.Register(nameof(EnableContent), typeof(object), typeof(TwoStateButton), new PropertyMetadata("启动"));

    public object EnableContent
    {
        get => (string)GetValue(EnableContentProperty);
        set => SetValue(EnableContentProperty, value);
    }

    private static readonly DependencyProperty EnableIconProperty = DependencyProperty.Register(nameof(EnableIcon), typeof(IconElement), typeof(TwoStateButton), new PropertyMetadata(null));

    public IconElement EnableIcon
    {
        get => (IconElement)GetValue(EnableIconProperty);
        set => SetValue(EnableIconProperty, value);
    }

    private static readonly DependencyProperty EnableCommandProperty = DependencyProperty.Register(nameof(EnableCommand), typeof(ICommand), typeof(TwoStateButton), new PropertyMetadata(null));

    public ICommand EnableCommand
    {
        get => (ICommand)GetValue(EnableCommandProperty);
        set => SetValue(EnableCommandProperty, value);
    }

    private static readonly DependencyProperty DisableContentProperty = DependencyProperty.Register(nameof(DisableContent), typeof(object), typeof(TwoStateButton), new PropertyMetadata("停止"));

    public object DisableContent
    {
        get => (string)GetValue(DisableContentProperty);
        set => SetValue(DisableContentProperty, value);
    }

    private static readonly DependencyProperty DisableIconProperty = DependencyProperty.Register(nameof(DisableIcon), typeof(IconElement), typeof(TwoStateButton), new PropertyMetadata(null));

    public IconElement DisableIcon
    {
        get => (IconElement)GetValue(DisableIconProperty);
        set => SetValue(DisableIconProperty, value);
    }

    private static readonly DependencyProperty DisableCommandProperty = DependencyProperty.Register(nameof(DisableCommand), typeof(ICommand), typeof(TwoStateButton), new PropertyMetadata(null));

    public ICommand DisableCommand
    {
        get => (ICommand)GetValue(DisableCommandProperty);
        set => SetValue(DisableCommandProperty, value);
    }
}