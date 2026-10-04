using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.I18n;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace BetterGenshinImpact.View.Controls.HotKey;

public class HotKeyTextBox : TextBox
{
    public static readonly DependencyProperty HotKeyTypeProperty = DependencyProperty.Register(
        nameof(HotKeyType),
        typeof(HotKeyTypeEnum),
        typeof(HotKeyTextBox),
        new FrameworkPropertyMetadata(
            HotKeyTypeEnum.KeyboardMonitor,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault
        )
    );

    /// <summary>
    /// 热键类型。键鼠监听不支持组合键，因此需要按类型给出不同的输入规则与提示。
    /// </summary>
    public HotKeyTypeEnum HotKeyType
    {
        get => (HotKeyTypeEnum)GetValue(HotKeyTypeProperty);
        set => SetValue(HotKeyTypeProperty, value);
    }

    public static readonly DependencyProperty CanSwitchHotKeyTypeProperty = DependencyProperty.Register(
        nameof(CanSwitchHotKeyType),
        typeof(bool),
        typeof(HotKeyTextBox),
        new FrameworkPropertyMetadata(true)
    );

    /// <summary>
    /// 是否允许把「键鼠监听」自动切换为「全局热键」。长按类功能只能使用键鼠监听，此时为 false。
    /// </summary>
    public bool CanSwitchHotKeyType
    {
        get => (bool)GetValue(CanSwitchHotKeyTypeProperty);
        set => SetValue(CanSwitchHotKeyTypeProperty, value);
    }

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(Model.HotKey),
        typeof(HotKeyTextBox),
        new FrameworkPropertyMetadata(
            default(Model.HotKey),
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (sender, _) =>
            {
                var control = (HotKeyTextBox)sender;
                control.Text = control.Hotkey.ToString();
            }
        )
    );

    public Model.HotKey Hotkey
    {
        get => (Model.HotKey)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private readonly ToolTip _hintToolTip = new()
    {
        Placement = PlacementMode.Bottom,
        StaysOpen = true,
    };

    private DispatcherTimer? _hintTimer;

    public HotKeyTextBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;

        if (ContextMenu is not null)
            ContextMenu.Visibility = Visibility.Collapsed;

        Text = Hotkey.ToString();
    }

    private static bool HasKeyChar(Key key) =>
        key
            is
            // A - Z
            >= Key.A
            and <= Key.Z
            or
            // 0 - 9
            >= Key.D0
            and <= Key.D9
            or
            // Numpad 0 - 9
            >= Key.NumPad0
            and <= Key.NumPad9
            or
            // The rest
            Key.OemQuestion
            or Key.OemQuotes
            or Key.OemPlus
            or Key.OemOpenBrackets
            or Key.OemCloseBrackets
            or Key.OemMinus
            or Key.DeadCharProcessed
            or Key.Oem1
            or Key.Oem5
            or Key.Oem7
            or Key.OemPeriod
            or Key.OemComma
            or Key.Add
            or Key.Divide
            or Key.Multiply
            or Key.Subtract
            or Key.Oem102
            or Key.Decimal;

    /// <summary>
    /// 按当前快捷键类型校验输入：不合法时给出提示并保留原值，必要时自动切换类型
    /// </summary>
    /// <param name="args">按键事件参数</param>
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        args.Handled = true;

        // Get modifiers and key data
        var modifiers = Keyboard.Modifiers;
        var key = args.Key;

        // If nothing was pressed - return
        if (key == Key.None)
            return;

        // If Alt is used as modifier - the key needs to be extracted from SystemKey
        if (key == Key.System)
            key = args.SystemKey;

        // If Delete/Backspace/Escape is pressed without modifiers - clear current value and return
        if (key is Key.Delete or Key.Back or Key.Escape && modifiers == ModifierKeys.None)
        {
            Hotkey = Model.HotKey.None;
            return;
        }

        // If the only key pressed is one of the modifier keys - return
        if (
            key
            is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin
            or Key.Clear
            or Key.OemClear
            or Key.Apps
        )
            return;

        // If Enter/Space/Tab is pressed without modifiers - return
        if (key is Key.Enter or Key.Tab && modifiers == ModifierKeys.None)
            return;

        if (HotKeyType == HotKeyTypeEnum.GlobalRegister && key is Key.Enter or Key.Space or Key.Tab && modifiers == ModifierKeys.None)
            return;

        // 键鼠监听只支持单键和鼠标侧键，组合键需要切换为全局热键
        var targetType = HotKeyType;
        var autoSwitched = false;
        if (targetType == HotKeyTypeEnum.KeyboardMonitor && modifiers != ModifierKeys.None)
        {
            if (!CanSwitchHotKeyType)
            {
                ShowHint(I18nService.Instance.Translate("此功能需要长按触发，只能使用键鼠监听的单键或鼠标侧键。"));
                return;
            }

            targetType = HotKeyTypeEnum.GlobalRegister;
            autoSwitched = true;
        }

        // If key has a character and pressed without modifiers or only with Shift - return
        if (targetType == HotKeyTypeEnum.GlobalRegister && HasKeyChar(key) && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            ShowHint(autoSwitched
                ? I18nService.Instance.Translate("键鼠监听不支持组合键，而全局热键不支持 Shift + 字符，请改用 Ctrl / Alt / Win 组合键或功能键。")
                : I18nService.Instance.Translate("全局热键不支持单字符按键，请使用组合键或功能键（如 F8）。"));
            return;
        }

        if (autoSwitched)
        {
            HotKeyType = HotKeyTypeEnum.GlobalRegister;
            ShowHint(I18nService.Instance.Translate("键鼠监听不支持组合键，已自动切换为全局热键。"));
        }

        // Set value
        Hotkey = new Model.HotKey(key, modifiers);
    }

    /// <summary>
    /// 支持鼠标侧键配置
    /// </summary>
    /// <param name="args"></param>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs args)
    {
        if (args.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            // 全局热键不支持鼠标侧键，自动切换为键鼠监听（键鼠监听支持任意单键和鼠标侧键，该方向总是安全的）
            if (HotKeyType == HotKeyTypeEnum.GlobalRegister)
            {
                HotKeyType = HotKeyTypeEnum.KeyboardMonitor;
                ShowHint(I18nService.Instance.Translate("全局热键不支持鼠标侧键，已自动切换为键鼠监听。"));
            }

            Hotkey = new Model.HotKey(Key.None, ModifierKeys.None, args.ChangedButton);
        }
    }

    /// <summary>
    /// 输入被拒绝或自动切换类型时给出可见反馈。
    /// 此前这些场景是静默处理的，用户只会看到快捷键莫名其妙变成 &lt; None &gt;。
    /// </summary>
    private void ShowHint(string message)
    {
        _hintToolTip.Content = message;
        _hintToolTip.PlacementTarget = this;
        ToolTip = _hintToolTip;

        // 每次提示都重新计时，避免连续输入时提示立刻消失
        _hintTimer ??= CreateHintTimer();
        _hintTimer.Stop();
        _hintTimer.Start();

        _hintToolTip.IsOpen = false;
        _hintToolTip.IsOpen = true;
    }

    /// <summary>
    /// 创建用于自动关闭提示的计时器
    /// </summary>
    private DispatcherTimer CreateHintTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _hintToolTip.IsOpen = false;
        };
        return timer;
    }
}
