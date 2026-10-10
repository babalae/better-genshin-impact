using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Behavior;

public sealed class CommitTextOnFocusLostBehavior : Behavior<TextBox>
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(CommitTextOnFocusLostBehavior));

    public static readonly DependencyProperty PreviewControlsProperty = DependencyProperty.Register(
        nameof(PreviewControls), typeof(FrameworkElement), typeof(CommitTextOnFocusLostBehavior));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public FrameworkElement? PreviewControls
    {
        get => (FrameworkElement?)GetValue(PreviewControlsProperty);
        set => SetValue(PreviewControlsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.LostKeyboardFocus += OnLostKeyboardFocus;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.LostKeyboardFocus -= OnLostKeyboardFocus;
        base.OnDetaching();
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 点击或用键盘切到测试按钮时仅预览草稿，不触发失焦保存；按钮仍可被键盘访问。
        for (var element = e.NewFocus as DependencyObject; element != null;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element == PreviewControls || element == AssociatedObject)
            {
                return;
            }
        }

        if (Command?.CanExecute(null) == true)
        {
            Command.Execute(null);
        }
    }
}
