using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Model;
using Fischless.WindowsInput;
using System;
using System.Windows;
using System.Windows.Input;
using static Vanara.PInvoke.User32;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.View.Controls.KeyBindings;

public class KeyBindingTextBox:TextBox
{

    public static readonly DependencyProperty KeyBindingProperty = DependencyProperty.Register(
        nameof(KeyBinding),
        typeof(KeyId),
        typeof(KeyBindingTextBox),
        new FrameworkPropertyMetadata(
            KeyId.None,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) =>
            {
                var control = (KeyBindingTextBox)sender;
                control.Text = control.KeyBinding.ToName();
            }
        )
    );

    /// <summary>
    /// 按键绑定
    /// </summary>
    public KeyId KeyBinding
    {
        get => (KeyId)GetValue(KeyBindingProperty);
        set => SetValue(KeyBindingProperty, value);
    }

    public KeyBindingTextBox() 
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;

        if (ContextMenu is not null)
            ContextMenu.Visibility = Visibility.Collapsed;

        Text = KeyBinding.ToName();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key;

        if (key == Key.System)
        {
            key = e.SystemKey;
        }
        try
        {
            // 将按键转换为KeyId
            KeyBinding = KeyIdConverter.FromInputKey(key);
        }
        catch   // 忽略某些按键用
        {
            return;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        var key = e.ChangedButton;

        // 首次点击（未获得焦点）时，忽略一次鼠标左键的输入
        if (key is MouseButton.Left && !IsFocused)
        {
            return;
        }

        try
        {
            // 将鼠标按键转换为KeyId
            KeyBinding = KeyIdConverter.FromMouseButton(key);
        }
        catch   // 忽略某些按键
        {
            return;
        }
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        Text = "等待按键...";
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        Text = KeyBinding.ToName();
        base.OnLostFocus(e);
    }
}
