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
        typeof(VK),
        typeof(KeyBindingTextBox),
        new FrameworkPropertyMetadata(
            default(VK),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) =>
            {
                var control = (KeyBindingTextBox)sender;
                control.Text = VkToName(control.KeyBinding);
            }
        )
    );

    /// <summary>
    /// 按键绑定
    /// </summary>
    public VK KeyBinding
    {
        get => (VK)GetValue(KeyBindingProperty);
        set => SetValue(KeyBindingProperty, value);
    }

    public KeyBindingTextBox() 
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;

        if (ContextMenu is not null)
            ContextMenu.Visibility = Visibility.Collapsed;

        Text = VkToName(KeyBinding);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key;

        // 若未产生按键或按下Esc/按下Windows键/按下组合键，终止按键设置
        if (
            key is Key.None ||
            key is Key.Escape ||
            key is Key.LWin or Key.RWin ||
            (key == Key.System && Keyboard.Modifiers is not ModifierKeys.None)
        )
        {
            return;
        }

        try
        {
            // 将按键转换为VK
            KeyBinding = KeyToVK(key);
        }
        catch   // 忽略某些按键用
        {
            return;
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        var key = e.ChangedButton;

        // 忽略鼠标左键
        if (key is MouseButton.Left)
        {
            return;
        }

        try
        {
            // 将鼠标按键转换为VK
            KeyBinding = MouseButtonToVK(key);
        }
        catch   // 忽略某些按键
        {
            return;
        }
    }

    private static string VkToName(VK value)
    {
        return value switch
        {
            VK.VK_BACK => "Backspace",
            VK.VK_TAB => "Tab",
            VK.VK_CLEAR => "Clear",
            VK.VK_RETURN => "Enter",
            VK.VK_PAUSE => "Pause",
            VK.VK_ESCAPE => "Esc",
            VK.VK_SPACE => "Space",
            VK.VK_OEM_7 => "'",
            VK.VK_OEM_COMMA => ",",
            VK.VK_OEM_MINUS => "-",
            VK.VK_OEM_PLUS => "=",
            VK.VK_OEM_PERIOD => ".",
            VK.VK_OEM_2 => "/",
            VK.VK_0 => "0",
            VK.VK_1 => "1",
            VK.VK_2 => "2",
            VK.VK_3 => "3",
            VK.VK_4 => "4",
            VK.VK_5 => "5",
            VK.VK_6 => "6",
            VK.VK_7 => "7",
            VK.VK_8 => "8",
            VK.VK_9 => "9",
            VK.VK_OEM_1 => ";",
            VK.VK_OEM_4 => "[",
            VK.VK_OEM_102 => @"\",
            VK.VK_OEM_6 => "]",
            VK.VK_OEM_3 => "`",
            VK.VK_A => "A",
            VK.VK_B => "B",
            VK.VK_C => "C",
            VK.VK_D => "D",
            VK.VK_E => "E",
            VK.VK_F => "F",
            VK.VK_G => "G",
            VK.VK_H => "H",
            VK.VK_I => "I",
            VK.VK_J => "J",
            VK.VK_K => "K",
            VK.VK_L => "L",
            VK.VK_M => "M",
            VK.VK_N => "N",
            VK.VK_O => "O",
            VK.VK_P => "P",
            VK.VK_Q => "Q",
            VK.VK_R => "R",
            VK.VK_S => "S",
            VK.VK_T => "T",
            VK.VK_U => "U",
            VK.VK_V => "V",
            VK.VK_W => "W",
            VK.VK_X => "X",
            VK.VK_Y => "Y",
            VK.VK_Z => "Z",
            VK.VK_DELETE => "Delete",
            VK.VK_NUMPAD0 => "Num 0",
            VK.VK_NUMPAD1 => "Num 1",
            VK.VK_NUMPAD2 => "Num 2",
            VK.VK_NUMPAD3 => "Num 3",
            VK.VK_NUMPAD4 => "Num 4",
            VK.VK_NUMPAD5 => "Num 5",
            VK.VK_NUMPAD6 => "Num 6",
            VK.VK_NUMPAD7 => "Num 7",
            VK.VK_NUMPAD8 => "Num 8",
            VK.VK_NUMPAD9 => "Num 9",
            VK.VK_DECIMAL => "Num .",
            VK.VK_DIVIDE => "Num /",
            VK.VK_MULTIPLY => "Num *",
            VK.VK_SUBTRACT => "Num -",
            VK.VK_ADD => "Num +",
            (VK)VK2.VK_NUMPAD_ENTER => "Num Enter",
            VK.VK_UP => "↑",
            VK.VK_DOWN => "↓",
            VK.VK_RIGHT => "→",
            VK.VK_LEFT => "←",
            VK.VK_INSERT => "Insert",
            VK.VK_HOME => "Home",
            VK.VK_END => "End",
            VK.VK_PRIOR => "Page Up",
            VK.VK_NEXT => "Page Down",
            VK.VK_F1 => "F1",
            VK.VK_F2 => "F2",
            VK.VK_F3 => "F3",
            VK.VK_F4 => "F4",
            VK.VK_F5 => "F5",
            VK.VK_F6 => "F6",
            VK.VK_F7 => "F7",
            VK.VK_F8 => "F8",
            VK.VK_F9 => "F9",
            VK.VK_F10 => "F10",
            VK.VK_F11 => "F11",
            VK.VK_F12 => "F12",
            VK.VK_F13 => "F13",
            VK.VK_F14 => "F14",
            VK.VK_F15 => "F15",
            VK.VK_NUMLOCK => "NumLock",
            VK.VK_CAPITAL => "CapsLock",
            VK.VK_SCROLL => "ScrollLock",
            VK.VK_RSHIFT => "Right Shift",
            VK.VK_LSHIFT => "Left Shift",
            VK.VK_RCONTROL => "Right Ctrl",
            VK.VK_LCONTROL => "Left Ctrl",
            VK.VK_RMENU => "Right Alt",
            VK.VK_LMENU => "Left Alt",
            VK.VK_RWIN => "Right Win",
            VK.VK_LWIN => "Left Win",
            VK.VK_HELP => "Help",
            VK.VK_PRINT => "Print",
            VK.VK_LBUTTON => "鼠标左键",
            VK.VK_RBUTTON => "鼠标右键",
            VK.VK_MBUTTON => "鼠标中键",
            VK.VK_XBUTTON1 => "鼠标侧键1",
            VK.VK_XBUTTON2 => "鼠标侧键2",
            _ => "Unknown",
        };
    }

    private static VK KeyToVK(Key value)
    {
        return value switch
        { 
            Key.Back => VK.VK_BACK,
            Key.Tab => VK.VK_TAB,
            Key.Clear => VK.VK_CLEAR,
            Key.Enter => VK.VK_RETURN,
            Key.Pause => VK.VK_PAUSE,
            Key.Escape => VK.VK_ESCAPE,
            Key.Space => VK.VK_SPACE,
            Key.Oem7 => VK.VK_OEM_7,
            Key.OemComma => VK.VK_OEM_COMMA,
            Key.OemMinus => VK.VK_OEM_MINUS,
            Key.OemPlus => VK.VK_OEM_PLUS,
            Key.OemPeriod => VK.VK_OEM_PERIOD,
            Key.Oem2 => VK.VK_OEM_2,
            Key.D0 => VK.VK_0,
            Key.D1 => VK.VK_1,
            Key.D2 => VK.VK_2,
            Key.D3 => VK.VK_3,
            Key.D4 => VK.VK_4,
            Key.D5 => VK.VK_5,
            Key.D6 => VK.VK_6,
            Key.D7 => VK.VK_7,
            Key.D8 => VK.VK_8,
            Key.D9 => VK.VK_9,
            Key.Oem1 => VK.VK_OEM_1,
            Key.Oem4 => VK.VK_OEM_4,
            Key.Oem102 => VK.VK_OEM_102,
            Key.Oem6 => VK.VK_OEM_6,
            Key.Oem3 => VK.VK_OEM_3,
            Key.A => VK.VK_A,
            Key.B => VK.VK_B,
            Key.C => VK.VK_C,
            Key.D => VK.VK_D,
            Key.E => VK.VK_E,
            Key.F => VK.VK_F,
            Key.G => VK.VK_G,
            Key.H => VK.VK_H,
            Key.I => VK.VK_I,
            Key.J => VK.VK_J,
            Key.K => VK.VK_K,
            Key.L => VK.VK_L,
            Key.M => VK.VK_M,
            Key.N => VK.VK_N,
            Key.O => VK.VK_O,
            Key.P => VK.VK_P,
            Key.Q => VK.VK_Q,
            Key.R => VK.VK_R,
            Key.S => VK.VK_S,
            Key.T => VK.VK_T,
            Key.U => VK.VK_U,
            Key.V => VK.VK_V,
            Key.W => VK.VK_W,
            Key.X => VK.VK_X,
            Key.Y => VK.VK_Y,
            Key.Z => VK.VK_Z,
            Key.Delete => VK.VK_DELETE,
            Key.NumPad0 => VK.VK_NUMPAD0,
            Key.NumPad1 => VK.VK_NUMPAD1,
            Key.NumPad2 => VK.VK_NUMPAD2,
            Key.NumPad3 => VK.VK_NUMPAD3,
            Key.NumPad4 => VK.VK_NUMPAD4,
            Key.NumPad5 => VK.VK_NUMPAD5,
            Key.NumPad6 => VK.VK_NUMPAD6,
            Key.NumPad7 => VK.VK_NUMPAD7,
            Key.NumPad8 => VK.VK_NUMPAD8,
            Key.NumPad9 => VK.VK_NUMPAD9,
            Key.Decimal => VK.VK_DECIMAL,
            Key.Divide => VK.VK_DIVIDE,
            Key.Multiply => VK.VK_MULTIPLY,
            Key.Subtract => VK.VK_SUBTRACT,
            Key.Add => VK.VK_ADD,
            Key.Up => VK.VK_UP,
            Key.Down => VK.VK_DOWN,
            Key.Right => VK.VK_RIGHT,
            Key.Left => VK.VK_LEFT,
            Key.Insert => VK.VK_INSERT,
            Key.Home => VK.VK_HOME,
            Key.End => VK.VK_END,
            Key.Prior => VK.VK_PRIOR,
            Key.Next => VK.VK_NEXT,
            Key.F1 => VK.VK_F1,
            Key.F2 => VK.VK_F2,
            Key.F3 => VK.VK_F3,
            Key.F4 => VK.VK_F4,
            Key.F5 => VK.VK_F5,
            Key.F6 => VK.VK_F6,
            Key.F7 => VK.VK_F7,
            Key.F8 => VK.VK_F8,
            Key.F9 => VK.VK_F9,
            Key.F10 => VK.VK_F10,
            Key.F11 => VK.VK_F11,
            Key.F12 => VK.VK_F12,
            Key.F13 => VK.VK_F13,
            Key.F14 => VK.VK_F14,
            Key.F15 => VK.VK_F15,
            Key.NumLock => VK.VK_NUMLOCK,
            Key.Capital => VK.VK_CAPITAL,
            Key.Scroll => VK.VK_SCROLL,
            Key.RightShift => VK.VK_RSHIFT,
            Key.LeftShift => VK.VK_LSHIFT,
            Key.RightCtrl => VK.VK_RCONTROL,
            Key.LeftCtrl => VK.VK_LCONTROL,
            Key.RightAlt => VK.VK_RMENU,
            Key.LeftAlt => VK.VK_LMENU,
            Key.LWin => VK.VK_LWIN,
            Key.RWin => VK.VK_RWIN,
            Key.Help => VK.VK_HELP,
            Key.Print => VK.VK_PRINT,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static VK MouseButtonToVK(MouseButton value)
    {
        return value switch
        {
            MouseButton.Left => VK.VK_LBUTTON,
            MouseButton.Right => VK.VK_RBUTTON,
            MouseButton.Middle => VK.VK_MBUTTON,
            MouseButton.XButton1 => VK.VK_XBUTTON1,
            MouseButton.XButton2 => VK.VK_XBUTTON2,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

}
